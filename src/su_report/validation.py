from __future__ import annotations

import json
import math
from dataclasses import dataclass
from pathlib import Path
from typing import Any, cast

import pandas as pd

from su_report.charts.mayoristas_historico import prepare_historical_shares
from su_report.models import PeriodKind, PeriodSpec

TOP_EDS_COLUMNS = {
    "RANK_EDS",
    "CODIGO_SICOM_COMPRADOR",
    "NOMBRE_COMERCIAL_COMPRADOR",
    "MUNICIPIO",
    "DEPARTAMENTO",
    "ZONA_FRONTERA",
    "VOLUMEN_ACEPTADO",
    "VOLUMEN_DESPACHADO",
    "MEDIDA_RANKING",
    "VOLUMEN_RANKING",
    "PARTICIPACION_NACIONAL_PCT",
}

ZFD_SUMMARY_COLUMNS = {
    "ZONA_FRONTERA",
    "VOLUMEN_DESPACHADO_ANIO_ANTERIOR",
    "VOLUMEN_DESPACHADO",
    "CAMBIO_ABSOLUTO_DESPACHADO",
    "VAR_INTERANUAL_DESPACHADO_PCT",
    "EDS_ACTIVAS",
    "PARTICIPACION_EDS_NACIONAL_PCT",
    "PARTICIPACION_VOLUMEN_NACIONAL_PCT",
    "GAL_MES_EDS_DESPACHADO",
}

ZFD_PRODUCT_COLUMNS = {
    "ZONA_FRONTERA",
    "PRODUCTO",
    "PRODUCTO_CANONICO",
    "VOLUMEN_DESPACHADO",
    "PARTICIPACION_PRODUCTO_EN_ZONA_PCT",
}

ZFD_MUNICIPALITY_COLUMNS = {
    "RANK_MUNICIPIO",
    "CODIGO_DANE_DEPARTAMENTO",
    "DEPARTAMENTO",
    "CODIGO_DANE_MUNICIPIO",
    "MUNICIPIO",
    "VOLUMEN_DESPACHADO",
    "PARTICIPACION_VOLUMEN_FRONTERA_PCT",
}


@dataclass(frozen=True, slots=True)
class ValidationResult:
    partial: bool
    warnings: tuple[str, ...]
    months_by_source: dict[str, tuple[int, ...]]
    historical_market_share: dict[str, object]


def _require_columns(path: Path, columns: set[str]) -> pd.DataFrame:
    if not path.exists():
        raise ValueError(f"No existe el insumo requerido: {path}")
    frame = pd.read_csv(path)
    missing = columns - set(frame.columns)
    if missing:
        raise ValueError(f"{path.name} no contiene: {', '.join(sorted(missing))}")
    return frame


def _validate_historical_manifest(path: Path, start_period: str, end_period: str, row_count: int) -> dict[str, Any]:
    if not path.exists():
        raise ValueError(f"No existe el manifiesto histórico requerido: {path}")
    try:
        manifest = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise ValueError(f"El manifiesto histórico no es JSON válido: {path}") from error
    if not isinstance(manifest, dict):
        raise ValueError("El manifiesto histórico debe ser un objeto JSON.")
    expected = {
        "SchemaVersion": 1,
        "Report": "mayoristas_historico",
        "Status": "complete",
        "Catalog": "SBI-Ordenes-Pedidos",
        "Cube": "Ordenes-Pedidos",
        "Measure": "dispatched",
        "Product": "all",
        "BuyerScope": "eds_fluvial",
        "ProviderType": "DISTRIBUIDOR MAYORISTA",
        "StartPeriod": start_period,
        "EndPeriod": end_period,
        "MonthlyRowCount": row_count,
    }
    mismatches = [key for key, value in expected.items() if manifest.get(key) != value]
    if mismatches:
        raise ValueError("El manifiesto histórico es incompatible en: " + ", ".join(mismatches) + ".")
    return manifest


def _validate_eds_top(period: PeriodSpec, processed_dir: Path) -> tuple[pd.DataFrame, dict[str, Any]]:
    frame = _require_columns(processed_dir / "eds-top-volumen.csv", TOP_EDS_COLUMNS)
    if len(frame) != 20:
        raise ValueError(f"eds-top-volumen.csv contiene {len(frame)} filas; se esperaban 20.")

    raw_codes = frame["CODIGO_SICOM_COMPRADOR"]
    codes = raw_codes.astype(str).str.strip()
    if raw_codes.isna().any() or codes.eq("").any() or codes.duplicated().any():
        raise ValueError("Top EDS debe contener 20 códigos SICOM únicos y no vacíos.")

    ranks = pd.to_numeric(frame["RANK_EDS"], errors="coerce")
    if ranks.isna().any() or ranks.tolist() != list(range(1, 21)):
        raise ValueError("RANK_EDS debe ser consecutivo de 1 a 20 y estar en ese orden.")

    numeric_columns = (
        "VOLUMEN_ACEPTADO",
        "VOLUMEN_DESPACHADO",
        "VOLUMEN_RANKING",
        "PARTICIPACION_NACIONAL_PCT",
    )
    numeric: dict[str, pd.Series] = {}
    for column in numeric_columns:
        values = pd.to_numeric(frame[column], errors="coerce")
        if values.isna().any() or not values.map(math.isfinite).all():
            raise ValueError(f"Top EDS contiene valores no numéricos en {column}.")
        if (values < 0).any():
            raise ValueError(f"Top EDS contiene valores negativos en {column}.")
        numeric[column] = values.astype(float)

    measures = frame["MEDIDA_RANKING"].astype(str).str.strip().str.lower()
    if not measures.eq("dispatched").all():
        raise ValueError("MEDIDA_RANKING debe ser dispatched en todas las filas del informe.")
    if not numeric["VOLUMEN_RANKING"].equals(numeric["VOLUMEN_DESPACHADO"]):
        delta = (numeric["VOLUMEN_RANKING"] - numeric["VOLUMEN_DESPACHADO"]).abs().max()
        if delta > 1e-6:
            raise ValueError("VOLUMEN_RANKING no coincide con VOLUMEN_DESPACHADO.")

    expected_order = sorted(
        range(len(frame)),
        key=lambda index: (-numeric["VOLUMEN_RANKING"].iloc[index], codes.iloc[index]),
    )
    if expected_order != list(range(len(frame))):
        raise ValueError("Top EDS no está ordenado de forma descendente con desempate por código SICOM.")

    if ((numeric["PARTICIPACION_NACIONAL_PCT"] < 0) | (numeric["PARTICIPACION_NACIONAL_PCT"] > 100)).any():
        raise ValueError("PARTICIPACION_NACIONAL_PCT debe estar entre 0 y 100.")
    for column in (
        "NOMBRE_COMERCIAL_COMPRADOR",
        "MUNICIPIO",
        "DEPARTAMENTO",
    ):
        if frame[column].isna().any() or frame[column].astype(str).str.strip().eq("").any():
            raise ValueError(f"Top EDS contiene valores vacíos en {column}.")

    manifest_path = processed_dir / "eds-top-manifest.json"
    if not manifest_path.exists():
        raise ValueError(f"No existe el manifiesto requerido: {manifest_path}")
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise ValueError("eds-top-manifest.json no es JSON válido.") from error
    expected_manifest = {
        "SchemaVersion": 1,
        "Report": "eds_top",
        "Status": "complete",
        "Catalog": "SBI-Ordenes-Pedidos",
        "Cube": "Ordenes-Pedidos",
        "Year": period.year,
        "Months": list(period.months),
        "PeriodLabel": period.code,
        "Measure": "dispatched",
        "Product": "all",
        "Products": [
            "BIODIESEL CON MEZCLA",
            "GASOLINA MOTOR CORRIENTE",
            "GASOLINA MOTOR EXTRA",
        ],
        "BuyerScope": "ESTACION DE SERVICIO AUTOMOTRIZ",
        "Top": 20,
        "RowCount": 20,
    }
    mismatches = [key for key, value in expected_manifest.items() if manifest.get(key) != value]
    if mismatches:
        raise ValueError("El manifiesto Top EDS es incompatible en: " + ", ".join(mismatches) + ".")
    national_volume = pd.to_numeric(pd.Series([manifest.get("NationalVolume")]), errors="coerce").iloc[0]
    if pd.isna(national_volume) or float(national_volume) <= 0:
        raise ValueError("El manifiesto Top EDS no contiene un volumen nacional válido.")
    recalculated = numeric["VOLUMEN_RANKING"] * 100.0 / float(national_volume)
    if (recalculated - numeric["PARTICIPACION_NACIONAL_PCT"]).abs().max() > 1e-6:
        raise ValueError("La participación nacional del Top EDS no es recalculable desde el manifiesto.")
    return frame, manifest


def _numeric_series(frame: pd.DataFrame, column: str, label: str) -> pd.Series:
    values = pd.to_numeric(frame[column], errors="coerce")
    if values.isna().any() or not values.map(math.isfinite).all():
        raise ValueError(f"{label} contiene valores no numéricos en {column}.")
    return values.astype(float)


def _validate_zfd(period: PeriodSpec, processed_dir: Path) -> dict[str, Any]:
    summary = _require_columns(processed_dir / "zfd-resumen.csv", ZFD_SUMMARY_COLUMNS)
    products = _require_columns(processed_dir / "zfd-productos.csv", ZFD_PRODUCT_COLUMNS)
    municipalities = _require_columns(
        processed_dir / "zfd-municipios.csv", ZFD_MUNICIPALITY_COLUMNS
    )

    expected_zones = ["SI", "NO", "TOTAL NACIONAL"]
    zones = summary["ZONA_FRONTERA"].astype(str).str.strip().str.upper().tolist()
    if zones != expected_zones:
        raise ValueError("zfd-resumen.csv debe contener SI, NO y TOTAL NACIONAL en ese orden.")

    nonnegative_summary = (
        "VOLUMEN_DESPACHADO_ANIO_ANTERIOR",
        "VOLUMEN_DESPACHADO",
        "EDS_ACTIVAS",
        "PARTICIPACION_EDS_NACIONAL_PCT",
        "PARTICIPACION_VOLUMEN_NACIONAL_PCT",
        "GAL_MES_EDS_DESPACHADO",
    )
    summary_numeric: dict[str, pd.Series] = {}
    for column in nonnegative_summary:
        values = _numeric_series(summary, column, "Resumen ZFD")
        if (values < 0).any():
            raise ValueError(f"Resumen ZFD contiene valores negativos en {column}.")
        summary_numeric[column] = values
    changes = _numeric_series(summary, "CAMBIO_ABSOLUTO_DESPACHADO", "Resumen ZFD")
    previous = summary_numeric["VOLUMEN_DESPACHADO_ANIO_ANTERIOR"]
    current = summary_numeric["VOLUMEN_DESPACHADO"]
    if ((current - previous - changes).abs() > 1e-6).any():
        raise ValueError("CAMBIO_ABSOLUTO_DESPACHADO no coincide con los volúmenes ZFD.")
    variation = pd.to_numeric(summary["VAR_INTERANUAL_DESPACHADO_PCT"], errors="coerce")
    invalid_variation = previous.gt(0) & variation.isna()
    if invalid_variation.any():
        raise ValueError("La variación ZFD solo puede ser n/d cuando el volumen anterior es cero.")
    expected_variation = (current - previous) * 100 / previous.where(previous.ne(0))
    comparable = previous.gt(0)
    if (variation[comparable] - expected_variation[comparable]).abs().max() > 1e-5:
        raise ValueError("VAR_INTERANUAL_DESPACHADO_PCT no es recalculable.")
    national = summary.iloc[2]
    for column in ("VOLUMEN_DESPACHADO_ANIO_ANTERIOR", "VOLUMEN_DESPACHADO", "EDS_ACTIVAS"):
        components = float(summary.loc[:1, column].sum())
        if not math.isclose(float(national[column]), components, rel_tol=0, abs_tol=1e-6):
            raise ValueError(f"El total nacional ZFD no coincide con SI + NO en {column}.")
    national_current = float(national["VOLUMEN_DESPACHADO"])
    national_active = float(national["EDS_ACTIVAS"])
    for index, row in summary.iterrows():
        current_volume = float(row["VOLUMEN_DESPACHADO"])
        active_eds = float(row["EDS_ACTIVAS"])
        expected_volume_share = current_volume * 100 / national_current if national_current else 0.0
        expected_eds_share = active_eds * 100 / national_active if national_active else 0.0
        expected_intensity = (
            current_volume / len(period.months) / active_eds if active_eds else 0.0
        )
        if not math.isclose(
            float(summary_numeric["PARTICIPACION_VOLUMEN_NACIONAL_PCT"].iloc[index]),
            expected_volume_share,
            rel_tol=0,
            abs_tol=1e-4,
        ):
            raise ValueError("La participación nacional de volumen ZFD no es recalculable.")
        if not math.isclose(
            float(summary_numeric["PARTICIPACION_EDS_NACIONAL_PCT"].iloc[index]),
            expected_eds_share,
            rel_tol=0,
            abs_tol=1e-4,
        ):
            raise ValueError("La participación nacional de EDS ZFD no es recalculable.")
        if not math.isclose(
            float(summary_numeric["GAL_MES_EDS_DESPACHADO"].iloc[index]),
            expected_intensity,
            rel_tol=0,
            abs_tol=1e-4,
        ):
            raise ValueError("La intensidad gal/mes/EDS ZFD no es recalculable.")

    product_zones = products["ZONA_FRONTERA"].astype(str).str.strip().str.upper()
    product_names = products["PRODUCTO_CANONICO"].astype(str).str.strip().str.lower()
    expected_products = {"corriente", "diesel", "extra"}
    for zone in ("SI", "NO"):
        found = set(product_names.loc[product_zones.eq(zone)])
        if found != expected_products:
            raise ValueError(f"zfd-productos.csv no contiene los tres productos para {zone}.")
    product_volumes = _numeric_series(products, "VOLUMEN_DESPACHADO", "Productos ZFD")
    product_shares = _numeric_series(
        products, "PARTICIPACION_PRODUCTO_EN_ZONA_PCT", "Productos ZFD"
    )
    if (product_volumes < 0).any() or ((product_shares < 0) | (product_shares > 100)).any():
        raise ValueError("Productos ZFD contiene volúmenes o participaciones fuera de rango.")
    current_by_zone = summary.set_index("ZONA_FRONTERA")["VOLUMEN_DESPACHADO"]
    for zone in ("SI", "NO"):
        zone_mask = product_zones.eq(zone)
        zone_total = float(current_by_zone.loc[zone])
        if not math.isclose(
            float(product_volumes[zone_mask].sum()), zone_total, rel_tol=0, abs_tol=1e-4
        ):
            raise ValueError(f"Los productos ZFD no suman el volumen de la zona {zone}.")
        if not math.isclose(float(product_shares[zone_mask].sum()), 100.0, abs_tol=1e-4):
            raise ValueError(f"Las participaciones de producto ZFD no suman 100% para {zone}.")
        expected_shares = product_volumes[zone_mask] * 100 / zone_total
        if not (product_shares[zone_mask] - expected_shares).abs().le(1e-4).all():
            raise ValueError(f"Las participaciones de producto ZFD no son recalculables para {zone}.")

    if len(municipalities) != 5:
        raise ValueError(f"zfd-municipios.csv contiene {len(municipalities)} filas; se esperaban 5.")
    ranks = _numeric_series(municipalities, "RANK_MUNICIPIO", "Municipios ZFD")
    if ranks.tolist() != [1, 2, 3, 4, 5]:
        raise ValueError("RANK_MUNICIPIO debe ser consecutivo de 1 a 5.")
    municipal_volumes = _numeric_series(
        municipalities, "VOLUMEN_DESPACHADO", "Municipios ZFD"
    )
    municipal_shares = _numeric_series(
        municipalities, "PARTICIPACION_VOLUMEN_FRONTERA_PCT", "Municipios ZFD"
    )
    if (municipal_volumes <= 0).any() or not municipal_volumes.is_monotonic_decreasing:
        raise ValueError("El Top 5 municipal ZFD debe estar ordenado por volumen descendente.")
    if ((municipal_shares < 0) | (municipal_shares > 100)).any():
        raise ValueError("La participación municipal ZFD debe estar entre 0 y 100.")
    frontier_total = float(current_by_zone.loc["SI"])
    expected_municipal_shares = municipal_volumes * 100 / frontier_total
    if not (municipal_shares - expected_municipal_shares).abs().le(1e-4).all():
        raise ValueError("La participación municipal ZFD no es recalculable.")

    manifest_path = processed_dir / "zfd-manifest.json"
    if not manifest_path.exists():
        raise ValueError(f"No existe el manifiesto requerido: {manifest_path}")
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise ValueError("zfd-manifest.json no es JSON válido.") from error
    if not isinstance(manifest, dict):
        raise ValueError("zfd-manifest.json debe contener un objeto JSON.")
    expected_manifest = {
        "SchemaVersion": 1,
        "Report": "eds_frontera",
        "Status": "complete",
        "Catalog": "SBI-Ordenes-Pedidos",
        "Cube": "Ordenes-Pedidos",
        "AgentsCatalog": "SBI-Agentes",
        "AgentsCube": "Agentes",
        "Year": period.year,
        "Months": list(period.months),
        "PeriodLabel": period.code,
        "Measure": "dispatched",
        "Products": [
            "BIODIESEL CON MEZCLA",
            "GASOLINA MOTOR CORRIENTE",
            "GASOLINA MOTOR EXTRA",
        ],
        "BuyerScope": "ESTACION DE SERVICIO AUTOMOTRIZ",
        "TopMunicipalities": 5,
        "SummaryRowCount": 3,
        "ProductRowCount": 6,
        "MunicipalityRowCount": 5,
    }
    mismatches = [key for key, value in expected_manifest.items() if manifest.get(key) != value]
    if mismatches:
        raise ValueError("El manifiesto ZFD es incompatible en: " + ", ".join(mismatches) + ".")
    if not str(manifest.get("ActiveEdsQueriedAtUtc", "")).strip():
        raise ValueError("El manifiesto ZFD no contiene la fecha de consulta de EDS activas.")
    return cast(dict[str, Any], manifest)


def validate_processed(
    period: PeriodSpec,
    processed_dir: Path,
    historical_start_period: str = "2011-01",
) -> ValidationResult:
    national = _require_columns(
        processed_dir / "national-monthly.csv",
        {"volumen_total", "anio_despacho", "mes_despacho", "producto"},
    )
    departments = _require_columns(
        processed_dir / "geography-departments.csv",
        {"volumen_total", "producto", "departamento", "anio_despacho"},
    )
    municipalities = _require_columns(
        processed_dir / "geography-municipalities.csv",
        {"volumen_total", "producto", "municipio", "anio_despacho"},
    )
    mayoristas = _require_columns(
        processed_dir / "mayoristas.csv",
        {"provider", "month", "year", "product", "buyer_subtype", "accepted_volume"},
    )
    eds = _require_columns(
        processed_dir / "eds-municipios.csv",
        {"ANO", "MES", "PRODUCTO_CANONICO", "VOLUMEN_ACEPTADO"},
    )
    active_eds = _require_columns(
        processed_dir / "eds-activas.csv",
        {"CODIGO_DANE_MUNICIPIO", "EDS_AUTOMOTRIZ_ACTIVAS"},
    )
    historical = _require_columns(
        processed_dir / "mayoristas-historico-mensual.csv",
        {"PERIODO", "ANO", "MES", "NOMBRE", "VOLUMEN_DESPACHADO", "PARTICIPACION_TOTAL"},
    )
    _, eds_top_manifest = _validate_eds_top(period, processed_dir)
    zfd_manifest = _validate_zfd(period, processed_dir)
    historical_end_period = f"{period.year:04d}-{period.months[-1]:02d}"
    historical_data = prepare_historical_shares(
        historical,
        historical_start_period,
        historical_end_period,
    )
    _validate_historical_manifest(
        processed_dir / "mayoristas-historico-manifest.json",
        historical_start_period,
        historical_end_period,
        len(historical),
    )

    expected = set(period.months)
    months_by_source = {
        "datos_abiertos": tuple(
            sorted(
                pd.to_numeric(
                    national.loc[pd.to_numeric(national["anio_despacho"], errors="coerce").eq(period.year), "mes_despacho"],
                    errors="coerce",
                )
                .dropna()
                .astype(int)
                .unique()
                .tolist()
            )
        ),
        "sicom_mayoristas": tuple(
            sorted(
                pd.to_numeric(
                    mayoristas.loc[pd.to_numeric(mayoristas["year"], errors="coerce").eq(period.year), "month"],
                    errors="coerce",
                )
                .dropna()
                .astype(int)
                .unique()
                .tolist()
            )
        ),
        "sicom_eds": tuple(
            sorted(
                pd.to_numeric(
                    eds.loc[pd.to_numeric(eds["ANO"], errors="coerce").eq(period.year), "MES"],
                    errors="coerce",
                )
                .dropna()
                .astype(int)
                .unique()
                .tolist()
            )
        ),
        "sicom_mayoristas_historico": tuple(
            sorted(
                pd.to_numeric(
                    historical.loc[pd.to_numeric(historical["ANO"], errors="coerce").eq(period.year), "MES"],
                    errors="coerce",
                )
                .dropna()
                .astype(int)
                .unique()
                .tolist()
            )
        ),
        "sicom_eds_top": tuple(int(month) for month in eds_top_manifest["Months"]),
        "sicom_zfd": tuple(int(month) for month in zfd_manifest["Months"]),
    }
    warnings: list[str] = []
    coverage_is_partial = False
    for source, found in months_by_source.items():
        if not found:
            raise ValueError(f"{source}: no contiene datos para {period.code}.")
        missing = expected - set(found)
        if missing:
            if period.kind is PeriodKind.ANNUAL:
                raise ValueError(
                    f"{source}: el informe anual requiere enero-diciembre completos; "
                    f"faltan meses {', '.join(map(str, sorted(missing)))}."
                )
            coverage_is_partial = True
            warnings.append(f"{source}: faltan meses {', '.join(map(str, sorted(missing)))}")

    required_national_products = {
        "GASOLINA MOTOR CORRIENTE",
        "GASOLINA MOTOR EXTRA",
        "BIODIESEL CON MEZCLA",
    }
    for year in (period.comparison_year, period.year):
        national_year = national.loc[pd.to_numeric(national["anio_despacho"], errors="coerce").eq(year)]
        missing_national_products = required_national_products - set(national_year["producto"].astype(str))
        if missing_national_products:
            raise ValueError(
                f"Datos Abiertos nacional no contiene productos de {year}: "
                + ", ".join(sorted(missing_national_products))
            )
    national_current = national.loc[pd.to_numeric(national["anio_despacho"], errors="coerce").eq(period.year)]

    geographic_products = {"GASOLINA MOTOR CORRIENTE", "BIODIESEL CON MEZCLA"}
    for label, frame in (("departamentos", departments), ("municipios", municipalities)):
        for year in (period.comparison_year, period.year):
            selected = frame.loc[pd.to_numeric(frame["anio_despacho"], errors="coerce").eq(year)]
            missing_products = geographic_products - set(selected["producto"].astype(str))
            if missing_products:
                raise ValueError(f"Datos Abiertos {label} no contiene todos los productos de {year}.")

    mayoristas_current = mayoristas.loc[pd.to_numeric(mayoristas["year"], errors="coerce").eq(period.year)]
    missing_mayoristas_products = required_national_products - set(mayoristas_current["product"].astype(str))
    if missing_mayoristas_products:
        raise ValueError("SICOM mayoristas no contiene todos los productos requeridos del periodo.")

    required_eds_products = {"corriente", "extra", "diesel"}
    eds_current = eds.loc[pd.to_numeric(eds["ANO"], errors="coerce").eq(period.year)]
    missing_eds_products = required_eds_products - set(eds_current["PRODUCTO_CANONICO"].astype(str).str.lower())
    if missing_eds_products:
        raise ValueError("SICOM EDS no contiene todos los productos requeridos del periodo.")
    if active_eds.empty:
        raise ValueError("SICOM Agentes no contiene el censo de EDS activas.")

    product_month_sources = (
        ("Datos Abiertos", national_current, "producto", "mes_despacho", required_national_products),
        ("SICOM mayoristas", mayoristas_current, "product", "month", required_national_products),
        ("SICOM EDS", eds_current, "PRODUCTO_CANONICO", "MES", required_eds_products),
    )
    for label, frame, product_column, month_column, products in product_month_sources:
        normalized_products = frame[product_column].astype(str)
        if product_column == "PRODUCTO_CANONICO":
            normalized_products = normalized_products.str.lower()
        for product in sorted(products):
            found_months = set(
                pd.to_numeric(frame.loc[normalized_products.eq(product), month_column], errors="coerce")
                .dropna()
                .astype(int)
            )
            missing_months = expected - found_months
            if missing_months:
                if period.kind is PeriodKind.ANNUAL:
                    raise ValueError(
                        f"{label} / {product}: el informe anual requiere enero-diciembre completos; "
                        f"faltan meses {', '.join(map(str, sorted(missing_months)))}."
                    )
                coverage_is_partial = True
                warnings.append(
                    f"{label} / {product}: faltan meses {', '.join(map(str, sorted(missing_months)))}"
                )

    numeric_checks = (
        (national, "volumen_total", "Datos Abiertos nacional"),
        (departments, "volumen_total", "Datos Abiertos departamentos"),
        (municipalities, "volumen_total", "Datos Abiertos municipios"),
        (mayoristas, "accepted_volume", "SICOM mayoristas"),
        (eds, "VOLUMEN_ACEPTADO", "SICOM EDS"),
        (active_eds, "EDS_AUTOMOTRIZ_ACTIVAS", "SICOM EDS activas"),
    )
    for frame, column, label in numeric_checks:
        values = pd.to_numeric(frame[column], errors="coerce")
        if values.isna().any():
            raise ValueError(f"{label} contiene valores no numéricos en {column}.")
        if (values < 0).any():
            warnings.append(f"{label} contiene volúmenes negativos.")

    share_sums = historical_data.shares.sum(axis=1)
    historical_summary: dict[str, object] = {
        "start_period": historical_start_period,
        "end_period": historical_end_period,
        "month_count": len(historical_data.shares),
        "input_row_count": historical_data.input_row_count,
        "monthly_share_sum_min": float(share_sums.min()),
        "monthly_share_sum_max": float(share_sums.max()),
        "max_source_share_delta": historical_data.max_source_share_delta,
    }
    return ValidationResult(coverage_is_partial, tuple(warnings), months_by_source, historical_summary)
