from __future__ import annotations

import matplotlib.pyplot as plt
import pandas as pd
from matplotlib.ticker import FuncFormatter

from su_report.maps import draw_variation_map
from su_report.reporting.context import PageResult, ReportContext
from su_report.reporting.data import ReportData, geo_comparison
from su_report.reporting.style import COLORS, PRODUCTS, add_header, new_page, style_chart


def geo_bar(
    axis: plt.Axes,
    context: ReportContext,
    comparison: pd.DataFrame,
    color: str,
    title: str,
    *,
    rows: int = 27,
) -> None:
    selected = comparison.head(rows).iloc[::-1]
    shares = selected[context.year] / comparison[context.year].sum() * 100
    bars = axis.barh(selected.iloc[:, 0].astype(str), shares, color=color, height=0.74)
    axis.set_title(title, fontsize=12, pad=12)
    axis.xaxis.set_major_formatter(FuncFormatter(lambda value, _: f"{value:.0f}%"))
    style_chart(axis, grid_axis="x")
    axis.bar_label(
        bars,
        labels=[f"{value:.2f}%" for value in shares],
        padding=4,
        fontsize=7.2,
        color=COLORS["muted"],
    )
    axis.margins(x=0.14)


def render_product(context: ReportContext, data: ReportData, product: str) -> PageResult:
    figure = new_page()
    add_header(figure, context, "Concentración espacial del consumo", width=0.45)
    map_axis = figure.add_axes((-0.02, 0.04, 0.47, 0.76))
    comparison = geo_comparison(context, data.departments, "departamento", product)
    draw_variation_map(
        map_axis,
        context.geography_path,
        comparison,
        product=product,
        base_year=context.previous_year,
        comparison_year=context.year,
        period_short=context.period_short,
    )
    label, color = PRODUCTS[product]
    bar_axis = figure.add_axes((0.69, 0.10, 0.27, 0.72))
    geo_bar(
        bar_axis,
        context,
        comparison,
        color,
        f"Participación por departamento - {label} (%)",
    )
    return PageResult(figure)


def render(context: ReportContext, data: ReportData) -> PageResult:
    return render_product(context, data, "GASOLINA MOTOR CORRIENTE")
