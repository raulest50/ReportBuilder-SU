from __future__ import annotations

from su_report.reporting.context import PageResult, ReportContext
from su_report.reporting.data import ReportData, comparison_display, geo_comparison
from su_report.reporting.style import PRODUCTS, add_header, new_page
from su_report.reporting.tables import draw_table


def render(context: ReportContext, data: ReportData) -> PageResult:
    figure = new_page()
    add_header(
        figure,
        context,
        f"Comparación entre {context.period_noun}s - Municipios",
        width=0.43,
    )
    for product, x in (("GASOLINA MOTOR CORRIENTE", 0.045), ("BIODIESEL CON MEZCLA", 0.53)):
        label, _color = PRODUCTS[product]
        figure.text(x + 0.215, 0.84, label, ha="center", fontsize=13)
        comparison = geo_comparison(context, data.municipalities, "municipio", product)
        table_axis = figure.add_axes((x, 0.08, 0.43, 0.72))
        draw_table(
            table_axis,
            comparison_display(context, comparison, "Municipio", rows=29),
            col_widths=[0.31, 0.25, 0.25, 0.19],
            font_size=6.9,
            header_font_size=8.2,
            header_rule="cell-bottom",
            scale_y=1.0,
        )
    return PageResult(figure)
