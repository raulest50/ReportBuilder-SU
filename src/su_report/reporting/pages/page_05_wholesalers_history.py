from __future__ import annotations

from su_report.charts.mayoristas_historico import (
    HISTORICAL_NOTE,
    draw_historical_stack,
    historical_legend_handles,
)
from su_report.reporting.context import PageResult, ReportContext
from su_report.reporting.data import ReportData
from su_report.reporting.style import add_header, new_page


def render(context: ReportContext, data: ReportData) -> PageResult:
    figure = new_page()
    add_header(figure, context, "Participación de mayoristas - Histórico", width=0.44)
    axis = figure.add_axes((0.085, 0.285, 0.88, 0.55))
    draw_historical_stack(axis, data.mayoristas_historico)
    figure.legend(
        handles=historical_legend_handles(),
        loc="lower center",
        bbox_to_anchor=(0.53, 0.145),
        ncols=3,
        frameon=False,
        columnspacing=1.2,
        handlelength=1.5,
        handleheight=1.5,
        fontsize=9.5,
    )
    figure.text(0.085, 0.045, HISTORICAL_NOTE, ha="left", va="bottom", fontsize=7.8, color="#6C6C6C")
    return PageResult(figure)
