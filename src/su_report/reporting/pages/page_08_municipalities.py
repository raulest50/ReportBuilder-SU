from __future__ import annotations

from su_report.reporting.context import PageResult, ReportContext
from su_report.reporting.data import ReportData, geo_comparison
from su_report.reporting.pages.page_06_geography_current import geo_bar
from su_report.reporting.style import PRODUCTS, add_header, new_page


def render(context: ReportContext, data: ReportData) -> PageResult:
    figure = new_page()
    add_header(figure, context, "Concentración espacial del consumo", width=0.45)
    layouts = (
        ("GASOLINA MOTOR CORRIENTE", 0.105, 0.335),
        ("BIODIESEL CON MEZCLA", 0.565, 0.375),
    )
    for product, x, width in layouts:
        comparison = geo_comparison(context, data.municipalities, "municipio", product)
        label, color = PRODUCTS[product]
        axis = figure.add_axes((x, 0.08, width, 0.76))
        geo_bar(
            axis,
            context,
            comparison,
            color,
            f"Participación por municipio - {label} (%)",
            rows=28,
        )
    return PageResult(figure)
