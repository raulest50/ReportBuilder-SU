from __future__ import annotations

from pathlib import Path

import pandas as pd
import pytest

from su_report.charts.eds_top import prepare_eds_top
from su_report.charts.mayoristas_historico import STACK_ORDER, HistoricalChartData
from su_report.reporting.context import ReportContext
from su_report.reporting.data import ReportData, ZfdData
from su_report.reporting.style import configure_matplotlib


@pytest.fixture(scope="session", autouse=True)
def configure_report_matplotlib() -> None:
    configure_matplotlib(())


@pytest.fixture
def report_context(tmp_path: Path) -> ReportContext:
    project_root = Path(__file__).resolve().parents[2]
    report = {
        "title": "Mercado de combustibles líquidos en Colombia",
        "cutoff_date": "2026-06-30",
    }
    return ReportContext(
        config={"report": report},
        report=report,
        project_dir=project_root,
        input_dir=tmp_path / "processed",
        brand_logo=project_root / "assets" / "brand" / "nuevo_logo.png",
        geography_path=project_root / "assets" / "geography" / "colombiaMod.json",
        cover_art=project_root / "assets" / "cover" / "cover-illustration-ai-v1-2k.png",
        pages_dir=tmp_path / "pages",
        metrics_path=tmp_path / "metrics.json",
        historical_validation_path=tmp_path / "historical-validation.json",
        font_dirs=(),
        year=2026,
        previous_year=2025,
        period_kind="quarter",
        period_number=2,
        period_label="segundo trimestre de 2026",
        period_title="Segundo trimestre de 2026",
        period_noun="trimestre",
        period_short="Trim. 2",
        page_count=14,
        months=(4, 5, 6),
        history_start_year=2022,
        historical_start_period="2025-01",
        historical_end_period="2026-06",
        partial=False,
    )


def _monthly() -> pd.DataFrame:
    base = {
        "GASOLINA MOTOR CORRIENTE": 180_000_000,
        "BIODIESEL CON MEZCLA": 165_000_000,
        "GASOLINA MOTOR EXTRA": 5_000_000,
    }
    rows = []
    for product, product_base in base.items():
        for year in range(2022, 2027):
            for month in (4, 5, 6):
                rows.append(
                    {
                        "anio_despacho": year,
                        "mes_despacho": month,
                        "producto": product,
                        "volumen_total": product_base + (year - 2022) * 2_000_000 + month * 100_000,
                    }
                )
    return pd.DataFrame(rows)


def _geography(column: str, names: list[str]) -> pd.DataFrame:
    rows = []
    for product in ("GASOLINA MOTOR CORRIENTE", "BIODIESEL CON MEZCLA"):
        for index, name in enumerate(names):
            for year in (2025, 2026):
                rows.append(
                    {
                        column: name,
                        "anio_despacho": year,
                        "producto": product,
                        "volumen_total": 10_000_000 + index * 800_000 + (year - 2025) * (500_000 - index * 70_000),
                    }
                )
    return pd.DataFrame(rows)


def _mayoristas() -> pd.DataFrame:
    providers = ["TERPEL", "PRIMAX", "CHEVRON", "BIOMAX", "PETROMIL", "OTRO"]
    rows = []
    for provider_index, provider in enumerate(providers):
        for product in (
            "GASOLINA MOTOR CORRIENTE",
            "BIODIESEL CON MEZCLA",
            "GASOLINA MOTOR EXTRA",
        ):
            for month in (4, 5, 6):
                rows.append(
                    {
                        "provider": provider,
                        "provider_short": provider,
                        "month": month,
                        "year": 2026,
                        "product": product,
                        "buyer_subtype": "ESTACION DE SERVICIO AUTOMOTRIZ",
                        "accepted_volume": 10_000_000 - provider_index * 800_000 + month * 10_000,
                    }
                )
    return pd.DataFrame(rows)


def _historical() -> HistoricalChartData:
    periods = pd.period_range("2025-01", "2026-06", freq="M")
    shares = pd.DataFrame(index=periods)
    values = {
        "Otros": 10.0,
        "PETROMIL": 10.0,
        "BIOMAX": 10.0,
        "CHEVRON": 15.0,
        "PRIMAX": 20.0,
        "TERPEL": 35.0,
    }
    for name in STACK_ORDER:
        shares[name] = values[name]
    volumes = shares * 1_000_000
    return HistoricalChartData(shares, volumes, input_row_count=len(periods) * 6, max_source_share_delta=0.0)


def _eds_top():
    rows = []
    for index in range(20):
        volume = 20_000_000 - index * 500_000
        rows.append(
            {
                "RANK_EDS": index + 1,
                "CODIGO_SICOM_COMPRADOR": str(620000 + index),
                "NOMBRE_COMERCIAL_COMPRADOR": f"EDS PRUEBA {index + 1} ({620000 + index})",
                "MUNICIPIO": "BOGOTA D.C.",
                "DEPARTAMENTO": "CUNDINAMARCA",
                "VOLUMEN_DESPACHADO": volume,
                "MEDIDA_RANKING": "dispatched",
                "VOLUMEN_RANKING": volume,
                "PARTICIPACION_NACIONAL_PCT": volume * 100 / 1_000_000_000,
            }
        )
    return prepare_eds_top(pd.DataFrame(rows))


def _eds_frames() -> tuple[pd.DataFrame, pd.DataFrame]:
    active_rows = []
    volume_rows = []
    for index in range(30):
        code = f"{11000 + index:05d}"
        active_rows.append(
            {
                "CODIGO_DANE_MUNICIPIO": code,
                "DEPARTAMENTO": "CUNDINAMARCA",
                "MUNICIPIO": f"MUNICIPIO {index + 1}",
                "ES_ZONA_FRONTERA": "NO",
                "EDS_AUTOMOTRIZ_ACTIVAS": 100 - index,
            }
        )
        for month in (4, 5, 6):
            for product in ("corriente", "diesel", "extra"):
                volume_rows.append(
                    {
                        "CODIGO_DANE_MUNICIPIO": code,
                        "ANO": 2026,
                        "MES": month,
                        "PRODUCTO_CANONICO": product,
                        "VOLUMEN_ACEPTADO": 1_000_000 + index * 10_000,
                    }
                )
    return pd.DataFrame(active_rows), pd.DataFrame(volume_rows)


def _zfd() -> ZfdData:
    summary = pd.DataFrame(
        [
            {
                "ZONA_FRONTERA": "SI",
                "VOLUMEN_DESPACHADO_ANIO_ANTERIOR": 166_620_000,
                "VOLUMEN_DESPACHADO": 164_210_000,
                "CAMBIO_ABSOLUTO_DESPACHADO": -2_410_000,
                "VAR_INTERANUAL_DESPACHADO_PCT": -1.45,
                "EDS_ACTIVAS": 1_541,
                "PARTICIPACION_EDS_NACIONAL_PCT": 25.10,
                "PARTICIPACION_VOLUMEN_NACIONAL_PCT": 15.50,
                "GAL_MES_EDS_DESPACHADO": 35_520,
            },
            {
                "ZONA_FRONTERA": "NO",
                "VOLUMEN_DESPACHADO_ANIO_ANTERIOR": 852_110_000,
                "VOLUMEN_DESPACHADO": 895_160_000,
                "CAMBIO_ABSOLUTO_DESPACHADO": 43_050_000,
                "VAR_INTERANUAL_DESPACHADO_PCT": 5.05,
                "EDS_ACTIVAS": 4_598,
                "PARTICIPACION_EDS_NACIONAL_PCT": 74.90,
                "PARTICIPACION_VOLUMEN_NACIONAL_PCT": 84.50,
                "GAL_MES_EDS_DESPACHADO": 64_895,
            },
            {
                "ZONA_FRONTERA": "TOTAL NACIONAL",
                "VOLUMEN_DESPACHADO_ANIO_ANTERIOR": 1_018_730_000,
                "VOLUMEN_DESPACHADO": 1_059_370_000,
                "CAMBIO_ABSOLUTO_DESPACHADO": 40_640_000,
                "VAR_INTERANUAL_DESPACHADO_PCT": 3.99,
                "EDS_ACTIVAS": 6_139,
                "PARTICIPACION_EDS_NACIONAL_PCT": 100.0,
                "PARTICIPACION_VOLUMEN_NACIONAL_PCT": 100.0,
                "GAL_MES_EDS_DESPACHADO": 57_521,
            },
        ]
    )
    products = pd.DataFrame(
        [
            {"ZONA_FRONTERA": zone, "PRODUCTO_CANONICO": product, "PARTICIPACION_PRODUCTO_EN_ZONA_PCT": share}
            for zone, values in {
                "SI": (("corriente", 47.11), ("diesel", 52.60), ("extra", 0.29)),
                "NO": (("corriente", 53.56), ("diesel", 44.32), ("extra", 2.12)),
            }.items()
            for product, share in values
        ]
    )
    municipalities = pd.DataFrame(
        [
            {
                "RANK_MUNICIPIO": rank,
                "DEPARTAMENTO": department,
                "MUNICIPIO": municipality,
                "VOLUMEN_DESPACHADO": volume,
                "PARTICIPACION_VOLUMEN_FRONTERA_PCT": share,
            }
            for rank, (municipality, department, volume, share) in enumerate(
                [
                    ("VALLEDUPAR", "CESAR", 12_900_000, 7.85),
                    ("AGUACHICA", "CESAR", 12_780_000, 7.78),
                    ("PASTO", "NARIÑO", 12_690_000, 7.73),
                    ("SAN JOSE DE CUCUTA", "NORTE DE SANTANDER", 10_840_000, 6.60),
                    ("LOS PATIOS", "NORTE DE SANTANDER", 6_110_000, 3.72),
                ],
                start=1,
            )
        ]
    )
    return ZfdData(summary, products, municipalities, "2026-07-22T15:00:00Z")


@pytest.fixture
def report_data() -> ReportData:
    departments = [
        "BOGOTA D.C.",
        "ANTIOQUIA",
        "VALLE DEL CAUCA",
        "CUNDINAMARCA",
        "ATLANTICO",
        "SANTANDER",
    ]
    municipalities = ["BOGOTA D.C.", "MEDELLIN", "CALI", "SOACHA", "BARRANQUILLA", "BUCARAMANGA"]
    eds_active, eds_volume = _eds_frames()
    return ReportData(
        monthly=_monthly(),
        departments=_geography("departamento", departments),
        municipalities=_geography("municipio", municipalities),
        mayoristas=_mayoristas(),
        mayoristas_historico=_historical(),
        eds_active=eds_active,
        eds_volume=eds_volume,
        eds_top=_eds_top(),
        zfd=_zfd(),
    )
