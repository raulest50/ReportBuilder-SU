from __future__ import annotations

import argparse
import json
import os
import re
import unicodedata
import uuid
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path

os.environ.setdefault("MPLCONFIGDIR", str(Path(__file__).resolve().parents[3] / ".matplotlib"))

import matplotlib as mpl

mpl.use("Agg")

import matplotlib.pyplot as plt
import numpy as np
import pandas as pd
from matplotlib.patches import Patch
from matplotlib.ticker import PercentFormatter

VOLUME_COLUMN = "VOLUMEN_DESPACHADO"
PARTICIPATION_COLUMN = "PARTICIPACION_TOTAL"
REQUIRED_COLUMNS = ("PERIODO", "ANO", "MES", "NOMBRE", VOLUME_COLUMN, PARTICIPATION_COLUMN)
FEATURED_PROVIDERS = ("TERPEL", "PRIMAX", "CHEVRON", "BIOMAX", "PETROMIL")
STACK_ORDER = ("Otros", "PETROMIL", "BIOMAX", "CHEVRON", "PRIMAX", "TERPEL")
LEGEND_ORDER = ("TERPEL", "PRIMAX", "CHEVRON", "BIOMAX", "PETROMIL", "Otros")
COLORS = {
    "TERPEL": "#F3262C",
    "PRIMAX": "#ED7137",
    "CHEVRON": "#0B70C4",
    "BIOMAX": "#30A84A",
    "PETROMIL": "#7B50C7",
    "Otros": "#B6B6B6",
}
SHARE_TOLERANCE = 1e-6


class HistoricalChartError(ValueError):
    """Raised when the historical monthly input violates its data contract."""


@dataclass(frozen=True)
class HistoricalChartData:
    shares: pd.DataFrame
    volumes: pd.DataFrame
    input_row_count: int
    max_source_share_delta: float


@dataclass(frozen=True)
class HistoricalChartArtifacts:
    svg_path: Path
    png_path: Path
    validation_path: Path
    validation: dict[str, object]


HISTORICAL_NOTE = (
    "Fuente: Cubo Ordenes-Pedidos del SICOM, Ministerio de Minas y Energía.\n"
    "Nota: participación sobre volumen despachado a EDS automotrices y fluviales; incluye gasolina motor "
    "corriente, gasolina motor extra y biodiésel con mezcla."
)


def parse_period(value: str, option_name: str) -> pd.Period:
    if not re.fullmatch(r"\d{4}-\d{2}", value):
        raise HistoricalChartError(f"{option_name} debe usar el formato YYYY-MM: {value!r}.")
    try:
        period = pd.Period(value, freq="M")
    except ValueError as error:
        raise HistoricalChartError(f"{option_name} no es un periodo mensual válido: {value!r}.") from error
    if period.month < 1 or period.month > 12:
        raise HistoricalChartError(f"{option_name} contiene un mes inválido: {value!r}.")
    return period


def provider_alias(value: str) -> str:
    normalized = unicodedata.normalize("NFKD", value.strip().upper())
    normalized = "".join(character for character in normalized if not unicodedata.combining(character))
    for provider in FEATURED_PROVIDERS:
        if provider in normalized:
            return provider
    return "Otros"


def prepare_historical_shares(
    frame: pd.DataFrame,
    start_period: str,
    end_period: str,
    *,
    tolerance: float = SHARE_TOLERANCE,
) -> HistoricalChartData:
    start = parse_period(start_period, "--start-period")
    end = parse_period(end_period, "--end-period")
    if end < start:
        raise HistoricalChartError("El periodo final no puede ser anterior al periodo inicial.")

    missing_columns = sorted(set(REQUIRED_COLUMNS) - set(frame.columns))
    if missing_columns:
        raise HistoricalChartError(f"Faltan columnas requeridas: {', '.join(missing_columns)}.")

    data = frame.loc[:, REQUIRED_COLUMNS].copy()
    raw_periods = data["PERIODO"].astype("string")
    if raw_periods.isna().any() or not raw_periods.str.fullmatch(r"\d{4}-\d{2}").all():
        raise HistoricalChartError("PERIODO debe contener exclusivamente valores YYYY-MM.")
    try:
        data["period"] = pd.PeriodIndex(raw_periods, freq="M")
    except ValueError as error:
        raise HistoricalChartError("PERIODO contiene un mes inválido.") from error

    data["year"] = pd.to_numeric(data["ANO"], errors="coerce")
    data["month"] = pd.to_numeric(data["MES"], errors="coerce")
    if data[["year", "month"]].isna().any().any():
        raise HistoricalChartError("ANO y MES deben ser enteros válidos.")
    if not np.equal(data["year"], np.floor(data["year"])).all() or not np.equal(
        data["month"], np.floor(data["month"])
    ).all():
        raise HistoricalChartError("ANO y MES deben ser enteros válidos.")
    data["year"] = data["year"].astype(int)
    data["month"] = data["month"].astype(int)
    period_years = data["period"].map(lambda period: period.year)
    period_months = data["period"].map(lambda period: period.month)
    if not data["year"].eq(period_years).all() or not data["month"].eq(period_months).all():
        raise HistoricalChartError("PERIODO no coincide con las columnas ANO y MES.")

    providers = data["NOMBRE"].astype("string").str.strip()
    if providers.isna().any() or providers.eq("").any():
        raise HistoricalChartError("NOMBRE no puede estar vacío.")
    data["provider"] = providers
    duplicate_keys = pd.DataFrame(
        {
            "period": data["period"].astype(str),
            "provider": providers.str.casefold(),
        }
    )
    if duplicate_keys.duplicated().any():
        raise HistoricalChartError("El CSV contiene proveedores duplicados dentro de un mismo mes.")

    data["volume"] = pd.to_numeric(data[VOLUME_COLUMN], errors="coerce")
    data["source_share"] = pd.to_numeric(data[PARTICIPATION_COLUMN], errors="coerce")
    if data[["volume", "source_share"]].isna().any().any():
        raise HistoricalChartError("Las columnas de volumen y participación deben ser numéricas y no nulas.")
    if not np.isfinite(data[["volume", "source_share"]].to_numpy(dtype=float)).all():
        raise HistoricalChartError("Las columnas de volumen y participación deben contener valores finitos.")
    if data["volume"].lt(0).any():
        raise HistoricalChartError("VOLUMEN_DESPACHADO no puede contener valores negativos.")
    if data["source_share"].lt(0).any() or data["source_share"].gt(100).any():
        raise HistoricalChartError("PARTICIPACION_TOTAL debe estar entre 0 y 100.")

    outside = data.loc[data["period"].lt(start) | data["period"].gt(end), "period"]
    if not outside.empty:
        values = ", ".join(sorted({str(value) for value in outside}))
        raise HistoricalChartError(f"El CSV contiene periodos fuera del intervalo solicitado: {values}.")

    expected_periods = pd.period_range(start, end, freq="M")
    observed_periods = pd.PeriodIndex(data["period"].unique(), freq="M").sort_values()
    missing_periods = expected_periods.difference(observed_periods)
    if len(missing_periods) > 0:
        raise HistoricalChartError(
            "Faltan meses completos en el histórico: " + ", ".join(str(period) for period in missing_periods) + "."
        )

    monthly_totals = data.groupby("period", observed=True)["volume"].transform("sum")
    if monthly_totals.le(0).any():
        zero_periods = sorted({str(period) for period in data.loc[monthly_totals.le(0), "period"]})
        raise HistoricalChartError("El volumen mensual total debe ser positivo: " + ", ".join(zero_periods) + ".")
    recomputed_source_share = data["volume"].div(monthly_totals).mul(100)
    deltas = recomputed_source_share.sub(data["source_share"]).abs()
    max_delta = float(deltas.max()) if not deltas.empty else 0.0
    if max_delta > tolerance:
        offending = data.loc[deltas.gt(tolerance), ["PERIODO", "NOMBRE"]].iloc[0]
        raise HistoricalChartError(
            "PARTICIPACION_TOTAL no coincide con el volumen mensual para "
            f"{offending['PERIODO']} / {offending['NOMBRE']} (diferencia máxima {max_delta:.10g})."
        )

    data["series"] = data["provider"].map(provider_alias)
    grouped = data.groupby(["period", "series"], observed=True)["volume"].sum().unstack(fill_value=0.0)
    volumes = grouped.reindex(index=expected_periods, columns=STACK_ORDER, fill_value=0.0).astype(float)
    totals = volumes.sum(axis=1)
    shares = volumes.div(totals, axis=0).mul(100)
    share_sums = shares.sum(axis=1)
    if not np.allclose(share_sums.to_numpy(dtype=float), 100.0, rtol=0.0, atol=tolerance):
        raise HistoricalChartError("Las series agrupadas no suman 100 % en todos los meses.")

    shares.index.name = "PERIODO"
    volumes.index.name = "PERIODO"
    return HistoricalChartData(
        shares=shares,
        volumes=volumes,
        input_row_count=len(data),
        max_source_share_delta=max_delta,
    )


def draw_historical_stack(axis: plt.Axes, data: HistoricalChartData) -> None:
    """Draw the reusable vector stack used by both the standalone figure and page 5."""
    dates = data.shares.index.to_timestamp()
    axis.stackplot(
        dates,
        *[data.shares[series].to_numpy(dtype=float) for series in STACK_ORDER],
        colors=[COLORS[series] for series in STACK_ORDER],
        baseline="zero",
        edgecolor="white",
        linewidth=0.22,
    )
    axis.set_ylim(0, 100)
    axis.set_yticks([0, 25, 50, 75, 100])
    axis.yaxis.set_major_formatter(PercentFormatter(xmax=100, decimals=0))
    axis.set_ylabel("Participación de mercado (%)")
    axis.set_xlim(dates[0], dates[-1])
    tick_years = range(data.shares.index[0].year, data.shares.index[-1].year + 1, 2)
    tick_dates = [pd.Timestamp(year=year, month=1, day=1) for year in tick_years]
    axis.set_xticks(tick_dates, [str(value.year) for value in tick_dates])
    axis.grid(axis="y", color="#D7D7D7", linewidth=0.55, alpha=0.75)
    axis.set_axisbelow(True)
    axis.tick_params(axis="both", length=0, colors="#3F3F3F")
    for spine in axis.spines.values():
        spine.set_visible(False)


def historical_legend_handles() -> list[Patch]:
    return [Patch(facecolor=COLORS[series], edgecolor="none", label=series) for series in LEGEND_ORDER]


def historical_validation_payload(
    data: HistoricalChartData,
    start_period: str,
    end_period: str,
    *,
    outputs: dict[str, str] | None = None,
) -> dict[str, object]:
    share_sums = data.shares.sum(axis=1)
    return {
        "schema_version": 1,
        "source": VOLUME_COLUMN,
        "generated_at_utc": datetime.now(UTC).isoformat(),
        "start_period": start_period,
        "end_period": end_period,
        "month_count": len(data.shares),
        "input_row_count": data.input_row_count,
        "featured_providers": list(FEATURED_PROVIDERS),
        "stack_order": list(STACK_ORDER),
        "legend_order": list(LEGEND_ORDER),
        "monthly_share_sum_min": float(share_sums.min()),
        "monthly_share_sum_max": float(share_sums.max()),
        "max_source_share_delta": data.max_source_share_delta,
        "outputs": outputs or {},
    }


def write_historical_validation(path: Path, validation: dict[str, object]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    _write_json_atomic(path, validation)


def render_historical_chart(
    data: HistoricalChartData,
    output_dir: Path,
    start_period: str,
    end_period: str,
) -> HistoricalChartArtifacts:
    output_dir.mkdir(parents=True, exist_ok=True)
    mpl.rcParams.update(
        {
            "font.family": "Segoe UI",
            "font.size": 10,
            "axes.titlesize": 16,
            "axes.labelsize": 11,
            "svg.hashsalt": "reportbuilder-su-mayoristas-historico",
        }
    )

    figure = plt.figure(figsize=(13.18, 7.0), facecolor="white")
    axis = figure.add_axes((0.085, 0.245, 0.88, 0.64))
    draw_historical_stack(axis, data)

    figure.text(
        0.045,
        0.955,
        "Participación Mayoristas - Histórico",
        ha="left",
        va="top",
        fontsize=16,
        fontweight="bold",
        color="#2F3A40",
    )
    figure.add_artist(
        plt.Line2D([0.04, 0.46], [0.915, 0.915], transform=figure.transFigure, color="#6D6D6D", linewidth=1.1)
    )
    figure.legend(
        handles=historical_legend_handles(),
        loc="lower center",
        bbox_to_anchor=(0.53, 0.115),
        ncols=3,
        frameon=False,
        columnspacing=1.2,
        handlelength=1.5,
        handleheight=1.5,
        fontsize=10.5,
    )
    figure.text(
        0.085,
        0.04,
        HISTORICAL_NOTE,
        ha="left",
        va="bottom",
        fontsize=8.5,
        color="#6C6C6C",
    )

    svg_path = output_dir / "mayoristas-historico.svg"
    png_path = output_dir / "mayoristas-historico.png"
    validation_path = output_dir / "mayoristas-historico-validation.json"
    try:
        _save_figure_atomic(figure, svg_path, format_name="svg", dpi=300)
        _save_figure_atomic(figure, png_path, format_name="png", dpi=300)
    finally:
        plt.close(figure)

    validation = historical_validation_payload(
        data,
        start_period,
        end_period,
        outputs={
            "svg": svg_path.name,
            "png": png_path.name,
        },
    )
    write_historical_validation(validation_path, validation)
    return HistoricalChartArtifacts(svg_path, png_path, validation_path, validation)


def build_historical_chart(
    input_path: Path,
    output_dir: Path,
    start_period: str,
    end_period: str,
) -> HistoricalChartArtifacts:
    frame = pd.read_csv(input_path)
    data = prepare_historical_shares(frame, start_period, end_period)
    return render_historical_chart(data, output_dir, start_period, end_period)


def _save_figure_atomic(figure: plt.Figure, path: Path, *, format_name: str, dpi: int) -> None:
    temporary = path.with_name(f".{path.name}.{uuid.uuid4().hex}.tmp")
    metadata = {"Creator": "ReportBuilder-SU", "Date": None} if format_name == "svg" else None
    try:
        figure.savefig(
            temporary,
            format=format_name,
            dpi=dpi,
            facecolor="white",
            bbox_inches=None,
            metadata=metadata,
        )
        temporary.replace(path)
    finally:
        temporary.unlink(missing_ok=True)


def _write_json_atomic(path: Path, value: dict[str, object]) -> None:
    temporary = path.with_name(f".{path.name}.{uuid.uuid4().hex}.tmp")
    try:
        temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        temporary.replace(path)
    finally:
        temporary.unlink(missing_ok=True)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Genera la participación histórica mensual de mayoristas.")
    parser.add_argument("--input", required=True, type=Path, help="CSV mensual producido por mayoristas_historico.")
    parser.add_argument("--output-dir", required=True, type=Path, help="Directorio para SVG, PNG y validación JSON.")
    parser.add_argument("--start-period", default="2011-01", help="Primer mes incluido, formato YYYY-MM.")
    parser.add_argument("--end-period", default="2026-06", help="Último mes incluido, formato YYYY-MM.")
    return parser


def main(argv: list[str] | None = None) -> int:
    arguments = build_parser().parse_args(argv)
    artifacts = build_historical_chart(
        arguments.input,
        arguments.output_dir,
        arguments.start_period,
        arguments.end_period,
    )
    print(f"SVG: {artifacts.svg_path.resolve()}")
    print(f"PNG: {artifacts.png_path.resolve()}")
    print(f"Validación: {artifacts.validation_path.resolve()}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
