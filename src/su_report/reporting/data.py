from __future__ import annotations

import json
from dataclasses import dataclass

import numpy as np
import pandas as pd

from su_report.charts.eds_top import EdsTopChartData, prepare_eds_top
from su_report.charts.mayoristas_historico import HistoricalChartData, prepare_historical_shares
from su_report.reporting.context import ReportContext
from su_report.reporting.style import MONTH_LABELS, fmt_millions, fmt_variation


@dataclass(frozen=True, slots=True)
class ZfdData:
    summary: pd.DataFrame
    products: pd.DataFrame
    municipalities: pd.DataFrame
    active_eds_queried_at_utc: str


@dataclass(frozen=True, slots=True)
class ReportData:
    monthly: pd.DataFrame
    departments: pd.DataFrame
    municipalities: pd.DataFrame
    mayoristas: pd.DataFrame
    mayoristas_historico: HistoricalChartData
    eds_active: pd.DataFrame
    eds_volume: pd.DataFrame
    eds_top: EdsTopChartData
    zfd: ZfdData


def provider_alias(value: str) -> str:
    upper = value.upper()
    keywords = {
        "TERPEL": "TERPEL",
        "PRIMAX": "PRIMAX",
        "CHEVRON": "CHEVRON",
        "BIOMAX": "BIOMAX",
        "PETROMIL": "PETROMIL",
        "PETROBRAS": "PETROBRAS",
        "ZEUSS": "ZEUSS",
        "DISCOM": "DISCOM",
        "PUMA": "PUMA ENERGY",
        "ECOSPETROLEO": "C.I. ECOSPETROLEO",
        "MULTIACTIVA": "COOMULPINORT",
        "AYATAWACOOP": "AYATAWACOOP",
        "PETRODECOL": "PETRODECOL",
        "PLUS MAS": "PLUS MAS ENERGY",
        "MARKETING OIL": "MARKETING OIL",
        "PROXXON": "PROXXON",
        "WAYUU": "COMBUSTIBLES WAYUU",
        "ZAPATA": "ZAPATA Y VELASQUEZ",
        "P Y B": "P Y B PETROLEOS",
    }
    for keyword, label in keywords.items():
        if keyword in upper:
            return label
    return value.split("(")[0].strip()[:24]


def load_data(context: ReportContext) -> ReportData:
    monthly_source = pd.read_csv(context.input_dir / "national-monthly.csv")
    monthly_source["anio_despacho"] = pd.to_numeric(monthly_source["anio_despacho"])
    monthly_source["mes_despacho"] = pd.to_numeric(monthly_source["mes_despacho"])
    monthly = (
        monthly_source.loc[monthly_source["mes_despacho"].isin(context.months)]
        .groupby(["anio_despacho", "mes_despacho", "producto"], as_index=False)["volumen_total"]
        .sum()
    )

    departments = pd.read_csv(context.input_dir / "geography-departments.csv")
    municipalities = pd.read_csv(context.input_dir / "geography-municipalities.csv")
    for frame in (departments, municipalities):
        frame["volumen_total"] = pd.to_numeric(frame["volumen_total"], errors="coerce")
        frame["anio_despacho"] = pd.to_numeric(frame["anio_despacho"], errors="coerce")

    mayoristas = pd.read_csv(context.input_dir / "mayoristas.csv")
    required_mayoristas = ["provider", "month", "year", "product", "buyer_subtype", "accepted_volume"]
    mayoristas = mayoristas[required_mayoristas].copy()
    mayoristas["month"] = pd.to_numeric(mayoristas["month"], errors="coerce")
    mayoristas["year"] = pd.to_numeric(mayoristas["year"], errors="coerce")
    mayoristas["accepted_volume"] = pd.to_numeric(
        mayoristas["accepted_volume"], errors="coerce"
    ).fillna(0)
    mayoristas["provider_short"] = mayoristas["provider"].map(provider_alias)

    historical = prepare_historical_shares(
        pd.read_csv(context.input_dir / "mayoristas-historico-mensual.csv"),
        context.historical_start_period,
        context.historical_end_period,
    )
    eds_active = pd.read_csv(
        context.input_dir / "eds-activas.csv",
        dtype={"CODIGO_DANE_DEPARTAMENTO": str, "CODIGO_DANE_MUNICIPIO": str},
    )
    eds_volume = pd.read_csv(
        context.input_dir / "eds-municipios.csv",
        dtype={"CODIGO_DANE_DEPARTAMENTO": str, "CODIGO_DANE_MUNICIPIO": str},
    )
    eds_top = prepare_eds_top(
        pd.read_csv(context.input_dir / "eds-top-volumen.csv", dtype={"CODIGO_SICOM_COMPRADOR": str})
    )
    zfd_manifest = json.loads(
        (context.input_dir / "zfd-manifest.json").read_text(encoding="utf-8")
    )
    zfd = ZfdData(
        summary=pd.read_csv(context.input_dir / "zfd-resumen.csv"),
        products=pd.read_csv(context.input_dir / "zfd-productos.csv"),
        municipalities=pd.read_csv(
            context.input_dir / "zfd-municipios.csv",
            dtype={
                "CODIGO_DANE_DEPARTAMENTO": str,
                "CODIGO_DANE_MUNICIPIO": str,
            },
        ),
        active_eds_queried_at_utc=str(zfd_manifest["ActiveEdsQueriedAtUtc"]),
    )
    return ReportData(
        monthly=monthly,
        departments=departments,
        municipalities=municipalities,
        mayoristas=mayoristas,
        mayoristas_historico=historical,
        eds_active=eds_active,
        eds_volume=eds_volume,
        eds_top=eds_top,
        zfd=zfd,
    )


def monthly_product(context: ReportContext, monthly: pd.DataFrame, product: str) -> pd.DataFrame:
    selected = monthly.loc[monthly["producto"].eq(product)]
    pivot = selected.pivot_table(
        index="mes_despacho", columns="anio_despacho", values="volumen_total", aggfunc="sum"
    ).reindex(context.months)
    for year in (context.previous_year, context.year):
        if year not in pivot:
            pivot[year] = np.nan
    return pivot


def comparison_table(context: ReportContext, monthly: pd.DataFrame, product: str) -> pd.DataFrame:
    pivot = monthly_product(context, monthly, product)
    rows: list[list[str]] = []
    for month in context.months:
        old = float(pivot.loc[month, context.previous_year])
        new = float(pivot.loc[month, context.year])
        rows.append([MONTH_LABELS[month], fmt_millions(old), fmt_millions(new), fmt_variation(new, old)])
    old_total = float(pivot[context.previous_year].sum())
    new_total = float(pivot[context.year].sum())
    rows.append(["Total", fmt_millions(old_total), fmt_millions(new_total), fmt_variation(new_total, old_total)])
    return pd.DataFrame(
        rows,
        columns=[
            "Mes",
            f"{context.previous_year} (gal)",
            f"{context.year} (gal)",
            "Var. relativa (%)",
        ],
    )


def annual_product(context: ReportContext, monthly: pd.DataFrame, product: str) -> pd.DataFrame:
    selected = (
        monthly.loc[monthly["producto"].eq(product)]
        .groupby("anio_despacho", as_index=False)["volumen_total"]
        .sum()
    )
    selected = selected.loc[
        selected["anio_despacho"].between(context.history_start_year, context.year)
    ].sort_values("anio_despacho")
    selected["variation"] = selected["volumen_total"].pct_change().mul(100).fillna(0)
    return selected


def annual_table(context: ReportContext, monthly: pd.DataFrame, product: str) -> pd.DataFrame:
    frame = annual_product(context, monthly, product)
    return pd.DataFrame(
        {
            "Año": frame["anio_despacho"].astype(int),
            f"Vol. {context.period_short} (gal)": frame["volumen_total"].map(fmt_millions),
            "Variación (%)": frame["variation"].map(
                lambda value: "n/d" if pd.isna(value) else f"{value:.2f}"
            ),
        }
    )


def geo_comparison(
    context: ReportContext, frame: pd.DataFrame, geography: str, product: str
) -> pd.DataFrame:
    selected = frame.loc[frame["producto"].eq(product)]
    pivot = selected.pivot_table(
        index=geography,
        columns="anio_despacho",
        values="volumen_total",
        aggfunc="sum",
        fill_value=0,
    )
    pivot = pivot.loc[pivot.index.astype(str) != "-"].copy()
    for year in (context.previous_year, context.year):
        if year not in pivot:
            pivot[year] = 0.0
    pivot["variation"] = np.where(
        pivot[context.previous_year] > 0,
        (pivot[context.year] / pivot[context.previous_year] - 1) * 100,
        np.nan,
    )
    return pivot.sort_values(context.year, ascending=False).reset_index()


def comparison_display(
    context: ReportContext,
    comparison: pd.DataFrame,
    geography_label: str,
    *,
    rows: int,
) -> pd.DataFrame:
    subset = comparison.head(rows)
    return pd.DataFrame(
        {
            geography_label: subset.iloc[:, 0].astype(str).str.upper(),
            f"{context.period_short} {context.previous_year} (gal)": subset[context.previous_year].map(
                fmt_millions
            ),
            f"{context.period_short} {context.year} (gal)": subset[context.year].map(fmt_millions),
            "Var. relativa (%)": subset["variation"].map(
                lambda value: "—" if pd.isna(value) else f"{value:.2f}"
            ),
        }
    )


def eds_summary(context: ReportContext, active: pd.DataFrame, volume: pd.DataFrame) -> pd.DataFrame:
    selected = volume.loc[
        volume["ANO"].eq(context.year)
        & volume["MES"].isin(context.months)
        & volume["PRODUCTO_CANONICO"].isin(["corriente", "diesel", "extra"])
    ]
    volume_by_municipality = selected.groupby("CODIGO_DANE_MUNICIPIO", as_index=False)[
        "VOLUMEN_ACEPTADO"
    ].sum()
    active_unique = (
        active.sort_values("EDS_AUTOMOTRIZ_ACTIVAS", ascending=False)
        .drop_duplicates("CODIGO_DANE_MUNICIPIO")
        [[
            "CODIGO_DANE_MUNICIPIO",
            "DEPARTAMENTO",
            "MUNICIPIO",
            "ES_ZONA_FRONTERA",
            "EDS_AUTOMOTRIZ_ACTIVAS",
        ]]
    )
    merged = active_unique.merge(volume_by_municipality, on="CODIGO_DANE_MUNICIPIO", how="left")
    merged["VOLUMEN_ACEPTADO"] = merged["VOLUMEN_ACEPTADO"].fillna(0)
    merged["GAL_MES_EDS"] = np.where(
        merged["EDS_AUTOMOTRIZ_ACTIVAS"] > 0,
        merged["VOLUMEN_ACEPTADO"] / (len(context.months) * merged["EDS_AUTOMOTRIZ_ACTIVAS"]),
        0,
    )
    merged["SHARE_EDS"] = (
        merged["EDS_AUTOMOTRIZ_ACTIVAS"] / merged["EDS_AUTOMOTRIZ_ACTIVAS"].sum() * 100
    )
    return merged.sort_values(["EDS_AUTOMOTRIZ_ACTIVAS", "VOLUMEN_ACEPTADO"], ascending=False)


def geo_value(comparison: pd.DataFrame, name: str, column: int | str) -> float:
    return float(
        comparison.loc[comparison.iloc[:, 0].astype(str).str.upper().eq(name.upper()), column].iloc[0]
    )
