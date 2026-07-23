from __future__ import annotations

import matplotlib.pyplot as plt
import pandas as pd
from matplotlib.colors import to_rgba

from su_report.reporting.context import ReportContext
from su_report.reporting.data import ReportData, comparison_table
from su_report.reporting.style import COLORS
from su_report.reporting.tables import draw_monthly_comparison_table, draw_table


def test_comparison_table_keeps_calculated_values(
    report_context: ReportContext, report_data: ReportData
) -> None:
    result = comparison_table(report_context, report_data.monthly, "GASOLINA MOTOR CORRIENTE")
    assert list(result.iloc[:, 0]) == ["abril", "mayo", "junio", "Total"]
    assert result.iloc[-1, -1] != "n/d"


def test_monthly_table_preserves_total_hierarchy() -> None:
    frame = pd.DataFrame(
        [
            ["abril", "1.00M", "2.00M", "100.00"],
            ["mayo", "2.00M", "3.00M", "50.00"],
            ["junio", "3.00M", "4.00M", "33.33"],
            ["Total", "6.00M", "9.00M", "50.00"],
        ],
        columns=["Mes", "2025", "2026", "Var."],
    )
    figure, axis = plt.subplots()
    try:
        table = draw_monthly_comparison_table(axis, frame)
        cells = table.get_celld()
        assert cells[(2, 0)].get_facecolor() == to_rgba(COLORS["stripe"])
        assert cells[(4, 0)].get_text().get_color() == COLORS["green"]
        assert cells[(4, 0)].get_text().get_weight() == "bold"
    finally:
        plt.close(figure)


def test_generic_table_keeps_striping_and_header_rule() -> None:
    frame = pd.DataFrame({"Nombre": ["A", "B", "C", "D"], "Valor": [1, 2, 3, 4]})
    figure, axis = plt.subplots()
    try:
        table = draw_table(axis, frame, header_rule="cell-bottom", header_font_size=9)
        assert table.get_celld()[(2, 0)].get_facecolor() == to_rgba(COLORS["stripe"])
        assert table.get_celld()[(0, 0)].get_text().get_fontsize() == 9
        assert len(axis.lines) == 1
    finally:
        plt.close(figure)
