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

    pd.DataFrame(
        [
            {
                "ZONA_FRONTERA": "SI",
                "VOLUMEN_DESPACHADO_ANIO_ANTERIOR": 1_000,
                "VOLUMEN_DESPACHADO": 1_100,
                "CAMBIO_ABSOLUTO_DESPACHADO": 100,
                "VAR_INTERANUAL_DESPACHADO_PCT": 10,
                "EDS_ACTIVAS": 10,
                "PARTICIPACION_EDS_NACIONAL_PCT": 20,
                "PARTICIPACION_VOLUMEN_NACIONAL_PCT": 22,
                "GAL_MES_EDS_DESPACHADO": 1_100 / 12 / 10,
            },
            {
                "ZONA_FRONTERA": "NO",
                "VOLUMEN_DESPACHADO_ANIO_ANTERIOR": 4_000,
                "VOLUMEN_DESPACHADO": 3_900,
                "CAMBIO_ABSOLUTO_DESPACHADO": -100,
                "VAR_INTERANUAL_DESPACHADO_PCT": -2.5,
                "EDS_ACTIVAS": 40,
                "PARTICIPACION_EDS_NACIONAL_PCT": 80,
                "PARTICIPACION_VOLUMEN_NACIONAL_PCT": 78,
                "GAL_MES_EDS_DESPACHADO": 3_900 / 12 / 40,
            },
            {
                "ZONA_FRONTERA": "TOTAL NACIONAL",
                "VOLUMEN_DESPACHADO_ANIO_ANTERIOR": 5_000,
                "VOLUMEN_DESPACHADO": 5_000,
                "CAMBIO_ABSOLUTO_DESPACHADO": 0,
                "VAR_INTERANUAL_DESPACHADO_PCT": 0,
                "EDS_ACTIVAS": 50,
                "PARTICIPACION_EDS_NACIONAL_PCT": 100,
                "PARTICIPACION_VOLUMEN_NACIONAL_PCT": 100,
                "GAL_MES_EDS_DESPACHADO": 5_000 / 12 / 50,
            },
        ]
    ).to_csv(directory / "zfd-resumen.csv", index=False)
    pd.DataFrame(
        [
            {
                "ZONA_FRONTERA": zone,
                "PRODUCTO": product_name,
                "PRODUCTO_CANONICO": canonical,
                "VOLUMEN_DESPACHADO": zone_total * share / 100,
                "PARTICIPACION_PRODUCTO_EN_ZONA_PCT": share,
            }
            for zone, zone_total in (("SI", 1_100), ("NO", 3_900))
            for product_name, canonical, share in (
                ("GASOLINA MOTOR CORRIENTE", "corriente", 50),
                ("BIODIESEL CON MEZCLA", "diesel", 40),
                ("GASOLINA MOTOR EXTRA", "extra", 10),
            )
        ]
    ).to_csv(directory / "zfd-productos.csv", index=False)
    frontier_total = 1_100
    pd.DataFrame(
        [
            {
                "RANK_MUNICIPIO": rank,
                "CODIGO_DANE_DEPARTAMENTO": "20",
                "DEPARTAMENTO": "CESAR",
                "CODIGO_DANE_MUNICIPIO": f"2000{rank}",
                "MUNICIPIO": f"MUNICIPIO {rank}",
                "VOLUMEN_DESPACHADO": volume,
                "PARTICIPACION_VOLUMEN_FRONTERA_PCT": volume * 100 / frontier_total,
            }
            for rank, volume in enumerate((250, 220, 200, 180, 150), start=1)
        ]
    ).to_csv(directory / "zfd-municipios.csv", index=False)
    (directory / "zfd-manifest.json").write_text(
        json.dumps(
            {
                "SchemaVersion": 1,
                "Report": "eds_frontera",
                "Status": "complete",
                "Catalog": "SBI-Ordenes-Pedidos",
                "Cube": "Ordenes-Pedidos",
                "AgentsCatalog": "SBI-Agentes",
                "AgentsCube": "Agentes",
                "Year": 2025,
                "Months": list(range(1, 13)),
                "PeriodLabel": "2025-A",
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
                "ActiveEdsQueriedAtUtc": "2026-07-22T15:00:00Z",
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


def test_validate_processed_rejects_zfd_percentage_mismatch(tmp_path: Path) -> None:
    _write_annual_inputs(tmp_path)
    path = tmp_path / "zfd-resumen.csv"
    frame = pd.read_csv(path)
    frame.loc[0, "PARTICIPACION_VOLUMEN_NACIONAL_PCT"] = 99
    frame.to_csv(path, index=False)
    with pytest.raises(ValueError, match="participación nacional de volumen"):
        validate_processed(PeriodSpec.parse("2025-A"), tmp_path, "2025-01")


def test_validate_processed_accepts_undefined_zfd_variation_for_zero_base(
    tmp_path: Path,
) -> None:
    _write_annual_inputs(tmp_path)
    path = tmp_path / "zfd-resumen.csv"
    frame = pd.read_csv(path)
    frame.loc[0, ["VOLUMEN_DESPACHADO_ANIO_ANTERIOR", "CAMBIO_ABSOLUTO_DESPACHADO"]] = [
        0,
        1_100,
    ]
    frame.loc[0, "VAR_INTERANUAL_DESPACHADO_PCT"] = float("nan")
    frame.loc[2, ["VOLUMEN_DESPACHADO_ANIO_ANTERIOR", "CAMBIO_ABSOLUTO_DESPACHADO"]] = [
        4_000,
        1_000,
    ]
    frame.loc[2, "VAR_INTERANUAL_DESPACHADO_PCT"] = 25
    frame.to_csv(path, index=False)

    result = validate_processed(PeriodSpec.parse("2025-A"), tmp_path, "2025-01")

    assert result.partial is False
