from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

import pandas as pd

from su_report.models import PeriodSpec


@dataclass(frozen=True, slots=True)
class ValidationResult:
    partial: bool
    warnings: tuple[str, ...]
    months_by_source: dict[str, tuple[int, ...]]


def _require_columns(path: Path, columns: set[str]) -> pd.DataFrame:
    if not path.exists():
        raise ValueError(f"No existe el insumo requerido: {path}")
    frame = pd.read_csv(path)
    missing = columns - set(frame.columns)
    if missing:
        raise ValueError(f"{path.name} no contiene: {', '.join(sorted(missing))}")
    return frame


def validate_processed(period: PeriodSpec, processed_dir: Path) -> ValidationResult:
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
    }
    warnings: list[str] = []
    coverage_is_partial = False
    for source, found in months_by_source.items():
        if not found:
            raise ValueError(f"{source}: no contiene datos para {period.code}.")
        missing = expected - set(found)
        if missing:
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

    return ValidationResult(coverage_is_partial, tuple(warnings), months_by_source)
