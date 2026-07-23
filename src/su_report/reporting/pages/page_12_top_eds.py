from __future__ import annotations

from su_report.charts.eds_top import draw_eds_top
from su_report.reporting.context import PageResult, ReportContext
from su_report.reporting.data import ReportData
from su_report.reporting.style import COLORS, MONTH_LABELS, add_header, new_page


def render(context: ReportContext, data: ReportData) -> PageResult:
    figure = new_page()
    add_header(figure, context, "Las 20 EDS de mayor volumen despachado", width=0.51)
    figure.text(0.04, 0.875, context.period_title, fontsize=10.5, color=COLORS["muted"], va="top")
    axis = figure.add_axes((0.19, 0.16, 0.77, 0.68))
    draw_eds_top(axis, data.eds_top)
    if len(context.months) == 1:
        months_text = f"{MONTH_LABELS[context.months[0]]} de {context.year}"
    else:
        months_text = (
            f"{MONTH_LABELS[context.months[0]]}–{MONTH_LABELS[context.months[-1]]} de {context.year}"
        )
    figure.text(
        0.19,
        0.045,
        "Fuente: SICOM, cubo Órdenes-Pedidos. EDS automotrices; gasolina corriente, gasolina extra y "
        f"biodiésel con mezcla; volumen despachado; {months_text}.",
        fontsize=7.1,
        color=COLORS["muted"],
        va="bottom",
    )
    return PageResult(figure)
