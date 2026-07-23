from __future__ import annotations

import json
from pathlib import Path

import pandas as pd
import pytest

from su_report.models import PeriodSpec
from su_report.validation import validate_processed

PRODUCTS = (
    "GASOLINA MOTOR CORRIENTE",
    "GASOLINA MOTOR EXTRA",
    "BIODIESEL CON MEZCLA",
)


def _write_annual_inputs(directory: Path, *, missing_national_month: int | None = None) -> None:
    directory.mkdir(parents=True, exist_ok=True)
    national_rows = []
    for year in (2024, 2025):
        months = range(1, 13) if year == 2025 else (1,)
        for month in months:
            if year == 2025 and month == missing_national_month:
                continue
            for product in PRODUCTS:
                national_rows.append(
                    {
                        "volumen_total": 1_000_000,
                        "anio_despacho": year,
                        "mes_despacho": month,
                        "producto": product,
                    }
                )
    pd.DataFrame(national_rows).to_csv(directory / "national-monthly.csv", index=False)

    geography_rows = [
        {"volumen_total": 1_000_000, "producto": product, "departamento": "ANTIOQUIA", "anio_despacho": year}
        for year in (2024, 2025)
        for product in ("GASOLINA MOTOR CORRIENTE", "BIODIESEL CON MEZCLA")
    ]
    pd.DataFrame(geography_rows).to_csv(directory / "geography-departments.csv", index=False)
    pd.DataFrame(
        [
            {
                "volumen_total": row["volumen_total"],
                "producto": row["producto"],
                "municipio": "MEDELLIN",
                "anio_despacho": row["anio_despacho"],
            }
            for row in geography_rows
        ]
    ).to_csv(directory / "geography-municipalities.csv", index=False)

    pd.DataFrame(
        [
            {
                "provider": "PROVEEDOR",
                "month": month,
                "year": 2025,
                "product": product,
                "buyer_subtype": "ESTACION DE SERVICIO AUTOMOTRIZ",
                "accepted_volume": 1_000,
            }
            for month in range(1, 13)
            for product in PRODUCTS
        ]
    ).to_csv(directory / "mayoristas.csv", index=False)

    canonical = ("corriente", "extra", "diesel")
    pd.DataFrame(
        [
            {"ANO": 2025, "MES": month, "PRODUCTO_CANONICO": product, "VOLUMEN_ACEPTADO": 1_000}
            for month in range(1, 13)
            for product in canonical
        ]
    ).to_csv(directory / "eds-municipios.csv", index=False)
    pd.DataFrame(
        [{"CODIGO_DANE_MUNICIPIO": "05001", "EDS_AUTOMOTRIZ_ACTIVAS": 50}]
    ).to_csv(directory / "eds-activas.csv", index=False)

    historical = pd.DataFrame(
        [
            {
                "PERIODO": f"2025-{month:02d}",
                "ANO": 2025,
                "MES": month,
                "NOMBRE": "ORGANIZACION TERPEL S.A.",
                "VOLUMEN_DESPACHADO": 1_000,
                "PARTICIPACION_TOTAL": 100.0,
            }
            for month in range(1, 13)
        ]
    )
    historical.to_csv(directory / "mayoristas-historico-mensual.csv", index=False)
    (directory / "mayoristas-historico-manifest.json").write_text(
        json.dumps(
            {
                "SchemaVersion": 1,
                "Report": "mayoristas_historico",
                "Status": "complete",
                "Catalog": "SBI-Ordenes-Pedidos",
                "Cube": "Ordenes-Pedidos",
                "Measure": "dispatched",
                "Product": "all",
                "BuyerScope": "eds_fluvial",
                "ProviderType": "DISTRIBUIDOR MAYORISTA",
                "StartPeriod": "2025-01",
                "EndPeriod": "2025-12",
                "MonthlyRowCount": len(historical),
            }
        ),
        encoding="utf-8",
    )

    national_volume = 1_000_000.0
    top_rows = []
    for index in range(20):
        volume = 20_000 - index * 100
        top_rows.append(
            {
                "RANK_EDS": index + 1,
                "CODIGO_SICOM_COMPRADOR": str(620000 + index),
                "NOMBRE_COMERCIAL_COMPRADOR": f"EDS {index + 1}",
                "MUNICIPIO": "MEDELLIN",
                "DEPARTAMENTO": "ANTIOQUIA",
                "ZONA_FRONTERA": "NO",
                "VOLUMEN_ACEPTADO": volume,
                "VOLUMEN_DESPACHADO": volume,
                "MEDIDA_RANKING": "dispatched",
                "VOLUMEN_RANKING": volume,
                "PARTICIPACION_NACIONAL_PCT": volume * 100 / national_volume,
                "PROVEEDOR_DOMINANTE": "ORGANIZACION TERPEL S.A.",
                "PARTICIPACION_PROVEEDOR_DOMINANTE_PCT": 90,
                "PLANTA_DOMINANTE": "PLANTA CENTRAL",
                "PARTICIPACION_PLANTA_DOMINANTE_PCT": 80,
            }
        )
    pd.DataFrame(top_rows).to_csv(directory / "eds-top-volumen.csv", index=False)
    (directory / "eds-top-manifest.json").write_text(
        json.dumps(
            {
                "SchemaVersion": 1,
                "Report": "eds_top",
                "Status": "complete",
                "Catalog": "SBI-Ordenes-Pedidos",
                "Cube": "Ordenes-Pedidos",
                "Year": 2025,
                "Months": list(range(1, 13)),
                "PeriodLabel": "2025-A",
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
                "NationalVolume": national_volume,
            }
        ),
        encoding="utf-8",
    )


def test_validate_processed_accepts_complete_annual_period(tmp_path: Path) -> None:
    _write_annual_inputs(tmp_path)
    result = validate_processed(PeriodSpec.parse("2025-A"), tmp_path, "2025-01")
    assert result.partial is False
    assert result.months_by_source["sicom_eds_top"] == tuple(range(1, 13))


def test_validate_processed_rejects_incomplete_annual_period(tmp_path: Path) -> None:
    _write_annual_inputs(tmp_path, missing_national_month=7)
    with pytest.raises(ValueError, match="enero-diciembre completos"):
        validate_processed(PeriodSpec.parse("2025-A"), tmp_path, "2025-01")


def test_validate_processed_rejects_top_eds_measure_mismatch(tmp_path: Path) -> None:
    _write_annual_inputs(tmp_path)
    path = tmp_path / "eds-top-volumen.csv"
    frame = pd.read_csv(path)
    frame.loc[0, "MEDIDA_RANKING"] = "accepted"
    frame.to_csv(path, index=False)
    with pytest.raises(ValueError, match="MEDIDA_RANKING"):
        validate_processed(PeriodSpec.parse("2025-A"), tmp_path, "2025-01")
