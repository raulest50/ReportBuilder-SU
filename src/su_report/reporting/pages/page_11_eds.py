from __future__ import annotations

import pandas as pd

from su_report.reporting.context import PageResult, ReportContext
from su_report.reporting.data import ReportData, eds_summary
from su_report.reporting.style import add_header, fmt_millions, new_page
from su_report.reporting.tables import draw_table


def render(context: ReportContext, data: ReportData) -> PageResult:
    figure = new_page()
    add_header(figure, context, "Estaciones de Servicio Automotriz", width=0.45)
    summary = eds_summary(context, data.eds_active, data.eds_volume).head(27)
    display = pd.DataFrame(
        {
            "Departamento": summary["DEPARTAMENTO"],
            "Municipio": summary["MUNICIPIO"],
            "EDS activas": summary["EDS_AUTOMOTRIZ_ACTIVAS"].map(lambda value: f"{int(value):,}"),
            f"{context.period_short}: Corriente + ACPM + Extra (gal)": summary["VOLUMEN_ACEPTADO"].map(
                fmt_millions
            ),
            f"Gal/mes/EDS {context.period_short}": summary["GAL_MES_EDS"].map(
                lambda value: f"{value / 1000:.0f}K"
            ),
            "Zona frontera": summary["ES_ZONA_FRONTERA"],
            "Participación EDS (%)": summary["SHARE_EDS"].map(lambda value: f"{value:.2f}%"),
        }
    )
    table_axis = figure.add_axes((0.045, 0.08, 0.92, 0.76))
    draw_table(
        table_axis,
        display,
        col_widths=[0.13, 0.15, 0.10, 0.19, 0.14, 0.13, 0.16],
        font_size=6.8,
        header_font_size=8.2,
        header_rule="cell-bottom",
        scale_y=1.0,
    )
    return PageResult(figure)
