from __future__ import annotations

import json
from pathlib import Path

import pandas as pd
import pytest
from PIL import Image

from su_report.charts.mayoristas_historico import (
    LEGEND_ORDER,
    STACK_ORDER,
    HistoricalChartError,
    prepare_historical_shares,
    render_historical_chart,
)
from su_report.validation import _validate_historical_manifest


def _monthly_frame(periods: pd.PeriodIndex) -> pd.DataFrame:
    provider_volumes = (
        ("ORGANIZACION TERPEL S.A. (4123)", 25.0),
        ("TERPEL COMBUSTIBLES", 15.0),
        ("PRIMAX COLOMBIA S.A.", 20.0),
        ("CHEVRON PETROLEUM COMPANY", 15.0),
        ("BIOMAX BIOCOMBUSTIBLES S.A.", 10.0),
        ("PETROMIL S.A.S.", 5.0),
        ("OTRO MAYORISTA", 10.0),
    )
    rows: list[dict[str, object]] = []
    for period in periods:
        total = sum(volume for _provider, volume in provider_volumes)
        for provider, volume in provider_volumes:
            rows.append(
                {
                    "PERIODO": str(period),
                    "ANO": period.year,
                    "MES": period.month,
                    "NOMBRE": provider,
                    "VOLUMEN_DESPACHADO": volume,
                    "PARTICIPACION_TOTAL": volume / total * 100,
                }
            )
    return pd.DataFrame(rows)


def test_aliases_collide_and_otros_is_exact_residual() -> None:
    periods = pd.period_range("2026-01", "2026-03", freq="M")

    result = prepare_historical_shares(_monthly_frame(periods), "2026-01", "2026-03")

    assert tuple(result.shares.columns) == STACK_ORDER
    assert result.shares.loc[pd.Period("2026-01"), "TERPEL"] == pytest.approx(40.0)
    assert result.shares.loc[pd.Period("2026-01"), "Otros"] == pytest.approx(10.0)
    assert result.shares.sum(axis=1).tolist() == pytest.approx([100.0, 100.0, 100.0])


def test_accepts_exact_186_month_window_without_july() -> None:
    periods = pd.period_range("2011-01", "2026-06", freq="M")

    result = prepare_historical_shares(_monthly_frame(periods), "2011-01", "2026-06")

    assert len(result.shares) == 186
    assert str(result.shares.index[0]) == "2011-01"
    assert str(result.shares.index[-1]) == "2026-06"
    assert pd.Period("2026-07", freq="M") not in result.shares.index


def test_rejects_missing_complete_month() -> None:
    periods = pd.period_range("2026-01", "2026-03", freq="M")
    frame = _monthly_frame(periods)
    frame = frame.loc[frame["PERIODO"] != "2026-02"]

    with pytest.raises(HistoricalChartError, match="Faltan meses completos"):
        prepare_historical_shares(frame, "2026-01", "2026-03")


def test_rejects_duplicate_provider_inside_month() -> None:
    frame = _monthly_frame(pd.period_range("2026-01", "2026-01", freq="M"))
    frame = pd.concat([frame, frame.iloc[[0]]], ignore_index=True)

    with pytest.raises(HistoricalChartError, match="duplicados"):
        prepare_historical_shares(frame, "2026-01", "2026-01")


@pytest.mark.parametrize("bad_volume", [-1.0, float("nan"), float("inf")])
def test_rejects_invalid_volume(bad_volume: float) -> None:
    frame = _monthly_frame(pd.period_range("2026-01", "2026-01", freq="M"))
    frame.loc[0, "VOLUMEN_DESPACHADO"] = bad_volume

    with pytest.raises(HistoricalChartError):
        prepare_historical_shares(frame, "2026-01", "2026-01")


def test_rejects_records_after_cutoff() -> None:
    frame = _monthly_frame(pd.period_range("2026-01", "2026-07", freq="M"))

    with pytest.raises(HistoricalChartError, match="fuera del intervalo"):
        prepare_historical_shares(frame, "2026-01", "2026-06")


def test_rejects_source_participation_mismatch() -> None:
    frame = _monthly_frame(pd.period_range("2026-01", "2026-01", freq="M"))
    frame.loc[0, "PARTICIPACION_TOTAL"] += 1

    with pytest.raises(HistoricalChartError, match="no coincide"):
        prepare_historical_shares(frame, "2026-01", "2026-01")


def test_renders_svg_png_and_validation_json(tmp_path: Path) -> None:
    periods = pd.period_range("2025-01", "2026-06", freq="M")
    prepared = prepare_historical_shares(_monthly_frame(periods), "2025-01", "2026-06")

    artifacts = render_historical_chart(prepared, tmp_path, "2025-01", "2026-06")

    assert artifacts.svg_path.is_file()
    assert artifacts.png_path.is_file()
    assert artifacts.validation_path.is_file()
    svg = artifacts.svg_path.read_text(encoding="utf-8")
    assert "Participación Mayoristas - Histórico" in svg
    with Image.open(artifacts.png_path) as image:
        assert image.width == 3954
        assert image.height == 2100
    validation = json.loads(artifacts.validation_path.read_text(encoding="utf-8"))
    assert validation["month_count"] == 18
    assert validation["stack_order"] == list(STACK_ORDER)
    assert validation["legend_order"] == list(LEGEND_ORDER)
    assert validation["monthly_share_sum_min"] == pytest.approx(100.0)
    assert validation["monthly_share_sum_max"] == pytest.approx(100.0)


def test_rejects_incompatible_historical_manifest(tmp_path: Path) -> None:
    manifest_path = tmp_path / "mayoristas-historico-manifest.json"
    manifest_path.write_text(
        json.dumps(
            {
                "SchemaVersion": 1,
                "Report": "mayoristas_historico",
                "Status": "complete",
                "Catalog": "SBI-Ordenes-Pedidos",
                "Cube": "Ordenes-Pedidos",
                "Measure": "accepted",
                "Product": "all",
                "BuyerScope": "eds_fluvial",
                "ProviderType": "DISTRIBUIDOR MAYORISTA",
                "StartPeriod": "2011-01",
                "EndPeriod": "2026-06",
                "MonthlyRowCount": 3305,
            }
        ),
        encoding="utf-8",
    )

    with pytest.raises(ValueError, match="Measure"):
        _validate_historical_manifest(manifest_path, "2011-01", "2026-06", 3305)
