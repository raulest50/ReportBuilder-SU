from __future__ import annotations

import matplotlib

matplotlib.use("Agg")

import matplotlib.pyplot as plt
import pandas as pd
import pytest

from su_report.charts.eds_top import (
    BAR_COLOR,
    EdsTopChartError,
    draw_eds_top,
    prepare_eds_top,
    strip_trailing_identifier,
)


def _frame() -> pd.DataFrame:
    rows = []
    for index in range(20):
        volume = 20_000_000 - index * 500_000
        rows.append(
            {
                "RANK_EDS": index + 1,
                "CODIGO_SICOM_COMPRADOR": f"{620000 + index}",
                "NOMBRE_COMERCIAL_COMPRADOR": f"EDS PRUEBA {index + 1} ({620000 + index})",
                "MUNICIPIO": "BOGOTA D.C.",
                "DEPARTAMENTO": "CUNDINAMARCA",
                "VOLUMEN_DESPACHADO": volume,
                "MEDIDA_RANKING": "dispatched",
                "VOLUMEN_RANKING": volume,
                "PARTICIPACION_NACIONAL_PCT": volume * 100 / 1_000_000_000,
            }
        )
    return pd.DataFrame(rows)


def test_prepare_eds_top_cleans_names_and_formats_national_share() -> None:
    data = prepare_eds_top(_frame())

    assert strip_trailing_identifier("EDS CENTRAL (620111)") == "EDS CENTRAL"
    assert data.frame.iloc[0]["station_label"] == "EDS PRUEBA 1"
    assert "2,00% del volumen nacional" in data.frame.iloc[0]["annotation"]


def test_draw_eds_top_keeps_rank_one_at_top_and_uses_one_color() -> None:
    data = prepare_eds_top(_frame())
    figure, axis = plt.subplots()
    try:
        draw_eds_top(axis, data)
        assert axis.yaxis_inverted()
        assert len(axis.patches) == 20
        assert all(patch.get_facecolor() == matplotlib.colors.to_rgba(BAR_COLOR) for patch in axis.patches)
    finally:
        plt.close(figure)


@pytest.mark.parametrize(
    ("column", "value"),
    [
        ("MEDIDA_RANKING", "accepted"),
        ("VOLUMEN_DESPACHADO", -1),
        ("VOLUMEN_RANKING", float("nan")),
    ],
)
def test_prepare_eds_top_rejects_invalid_measure_and_volumes(column: str, value: object) -> None:
    frame = _frame()
    frame.loc[0, column] = value
    with pytest.raises(EdsTopChartError):
        prepare_eds_top(frame)
