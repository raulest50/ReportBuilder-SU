from __future__ import annotations

import matplotlib.pyplot as plt
import numpy as np
from matplotlib.lines import Line2D
from matplotlib.ticker import FuncFormatter, MultipleLocator

from su_report.reporting.context import PageResult, ReportContext
from su_report.reporting.data import ReportData, comparison_table, monthly_product
from su_report.reporting.style import (
    COLORS,
    MONTH_LABELS,
    PRODUCTS,
    add_header,
    add_note,
    millions,
    new_page,
    style_chart,
)
from su_report.reporting.tables import draw_monthly_comparison_table


def _set_rounded_y_axis(axis: plt.Axes, values: list[float], step: float) -> None:
    finite = np.asarray(values, dtype=float)
    finite = finite[np.isfinite(finite)]
    if finite.size == 0:
        return
    lower = np.floor(finite.min() / step) * step
    upper = finite.max() + step * 0.18
    if upper <= lower:
        upper = lower + step
    axis.set_ylim(lower, upper)
    axis.yaxis.set_major_locator(MultipleLocator(step))


def _draw_product_share_pie(axis: plt.Axes, totals: list[float]) -> None:
    colors = [PRODUCTS[product][1] for product in PRODUCTS]
    wedges, _texts = axis.pie(totals, colors=colors, startangle=90, counterclock=False, radius=1.0)
    grand_total = float(sum(totals))
    for wedge, value in zip(wedges, totals, strict=True):
        angle = np.deg2rad((wedge.theta1 + wedge.theta2) / 2)
        x_direction = float(np.cos(angle))
        y_direction = float(np.sin(angle))
        percentage = 0.0 if grand_total == 0 else value / grand_total * 100
        start = (x_direction, y_direction)
        elbow = (1.13 * x_direction, 1.13 * y_direction)
        if abs(x_direction) < 0.22:
            label_x, label_y, end_x, alignment = -0.28, 1.12, -0.18, "right"
        else:
            label_x = 1.34 if x_direction > 0 else -1.34
            label_y = elbow[1]
            end_x = label_x - 0.08 if x_direction > 0 else label_x + 0.08
            alignment = "left" if x_direction > 0 else "right"
        axis.plot(
            [start[0], elbow[0], end_x],
            [start[1], elbow[1], elbow[1]],
            color=COLORS["muted"],
            linewidth=0.65,
        )
        axis.text(
            label_x,
            label_y,
            f"{percentage:.2f}%",
            ha=alignment,
            va="center",
            fontsize=7.6,
            color=COLORS["muted"],
        )
    axis.set_xlim(-1.52, 1.52)
    axis.set_ylim(-1.28, 1.28)
    axis.set_aspect("equal")


def _render_annual(context: ReportContext, data: ReportData) -> PageResult:
    monthly = data.monthly
    figure = new_page()
    add_header(
        figure,
        context,
        f"Comportamiento de combustibles líquidos — {context.period_title}",
        width=0.59,
    )
    month_ticks = [MONTH_LABELS[month][:3].title() for month in context.months]

    axis = figure.add_axes((0.055, 0.56, 0.37, 0.25))
    main_values: list[float] = []
    for product in ("GASOLINA MOTOR CORRIENTE", "BIODIESEL CON MEZCLA"):
        pivot = monthly_product(context, monthly, product)
        label, color = PRODUCTS[product]
        axis.plot(
            context.months,
            pivot[context.year],
            marker="o",
            markersize=3,
            linewidth=1.8,
            color=color,
            label=label,
        )
        main_values.extend(float(value) for value in pivot[context.year])
    axis.set_title(f"Consumo Corriente y ACPM — {context.year}", loc="left", pad=18)
    axis.set_xticks(context.months, month_ticks, rotation=35, ha="right", fontsize=6.5)
    axis.yaxis.set_major_formatter(FuncFormatter(millions))
    axis.set_ylabel("Volumen (gal)")
    _set_rounded_y_axis(axis, main_values, 10_000_000)
    axis.legend(loc="upper left", bbox_to_anchor=(0, 1.16), ncols=2, frameon=False, handlelength=0.5)
    style_chart(axis)

    extra_axis = figure.add_axes((0.475, 0.56, 0.245, 0.25))
    extra = monthly_product(context, monthly, "GASOLINA MOTOR EXTRA")
    extra_axis.plot(
        context.months,
        extra[context.year],
        marker="o",
        markersize=3,
        linewidth=1.8,
        color=COLORS["gray"],
    )
    extra_axis.set_title(f"Consumo Extra — {context.year}", loc="left", pad=18)
    extra_axis.set_xticks(context.months, month_ticks, rotation=35, ha="right", fontsize=6.5)
    extra_axis.yaxis.set_major_formatter(FuncFormatter(lambda value, _: f"{value / 1_000_000:.1f}M"))
    _set_rounded_y_axis(extra_axis, [float(value) for value in extra[context.year]], 100_000)
    style_chart(extra_axis)

    pie_axis = figure.add_axes((0.775, 0.555, 0.17, 0.25))
    totals = [monthly_product(context, monthly, product)[context.year].sum() for product in PRODUCTS]
    _draw_product_share_pie(pie_axis, [float(total) for total in totals])
    figure.text(0.86, 0.82, "Participación por producto", ha="center", fontsize=11)
    handles = [
        Line2D(
            [0],
            [0],
            marker="o",
            color="w",
            markerfacecolor=PRODUCTS[product][1],
            markersize=6,
            label=PRODUCTS[product][0],
        )
        for product in PRODUCTS
    ]
    figure.legend(
        handles=handles,
        loc="upper center",
        bbox_to_anchor=(0.86, 0.79),
        ncols=3,
        frameon=False,
        handlelength=0,
        handletextpad=0.25,
        columnspacing=0.55,
        fontsize=7,
    )
    for product, x in zip(PRODUCTS, (0.035, 0.355, 0.675), strict=True):
        label, _color = PRODUCTS[product]
        figure.text(x + 0.145, 0.465, f"Comparación mensual — {label}", ha="center", fontsize=10)
        table_axis = figure.add_axes((x, 0.105, 0.29, 0.33))
        draw_monthly_comparison_table(
            table_axis,
            comparison_table(context, monthly, product),
            col_widths=[0.17, 0.27, 0.27, 0.29],
            font_size=5.25,
        )
    figure.text(
        0.04,
        0.035,
        "Fuente: Datos Abiertos de combustibles líquidos. Alcance: EDS automotrices y fluviales, y "
        "comercializadores industriales. Medida: volumen despachado.",
        fontsize=7.1,
        color=COLORS["muted"],
        va="bottom",
    )
    return PageResult(figure)


def render(context: ReportContext, data: ReportData) -> PageResult:
    if context.period_kind == "annual":
        return _render_annual(context, data)
    monthly = data.monthly
    figure = new_page()
    add_header(
        figure,
        context,
        f"Comportamiento de combustibles líquidos para el {context.period_label}",
        width=0.585,
    )
    axis = figure.add_axes((0.055, 0.53, 0.37, 0.27))
    main_values: list[float] = []
    for product in ("GASOLINA MOTOR CORRIENTE", "BIODIESEL CON MEZCLA"):
        pivot = monthly_product(context, monthly, product)
        label, color = PRODUCTS[product]
        axis.plot(context.months, pivot[context.year], marker="o", linewidth=2, color=color, label=label)
        main_values.extend(float(value) for value in pivot[context.year])
    axis.set_title(
        f"Consumo Corriente y ACPM - {context.period_short} {context.year}", loc="left", pad=22
    )
    axis.set_xticks(context.months, [MONTH_LABELS[month] for month in context.months])
    axis.yaxis.set_major_formatter(FuncFormatter(millions))
    axis.set_ylabel("Volumen (gal)")
    axis.set_xlabel("Tiempo", fontweight="bold")
    _set_rounded_y_axis(axis, main_values, 10_000_000)
    axis.legend(
        loc="upper left",
        bbox_to_anchor=(0, 1.16),
        ncols=2,
        frameon=False,
        handlelength=0.5,
        handletextpad=0.3,
    )
    style_chart(axis)

    extra_axis = figure.add_axes((0.055, 0.12, 0.37, 0.25))
    extra = monthly_product(context, monthly, "GASOLINA MOTOR EXTRA")
    extra_axis.plot(context.months, extra[context.year], marker="o", linewidth=2, color=COLORS["gray"])
    extra_axis.set_title(f"Consumo Extra - {context.period_short} {context.year}", loc="left", pad=18)
    extra_axis.set_xticks(context.months, [MONTH_LABELS[month] for month in context.months])
    extra_axis.yaxis.set_major_formatter(FuncFormatter(lambda value, _: f"{value / 1_000_000:.1f}M"))
    extra_axis.set_ylabel("Volumen (gal)")
    extra_axis.set_xlabel("Tiempo", fontweight="bold")
    _set_rounded_y_axis(extra_axis, [float(value) for value in extra[context.year]], 100_000)
    style_chart(extra_axis)

    table_positions = [(0.445, 0.650), (0.445, 0.430), (0.445, 0.180)]
    for product, (x, y) in zip(PRODUCTS, table_positions, strict=True):
        label, _color = PRODUCTS[product]
        figure.text(x + 0.115, y + 0.165, f"Comparación mes a mes - {label}", ha="center", fontsize=11.5)
        table_axis = figure.add_axes((x, y, 0.23, 0.135))
        draw_monthly_comparison_table(
            table_axis,
            comparison_table(context, monthly, product),
            col_widths=[0.16, 0.27, 0.27, 0.30],
            font_size=7.7,
        )

    pie_axis = figure.add_axes((0.745, 0.445, 0.19, 0.30))
    totals = [monthly_product(context, monthly, product)[context.year].sum() for product in PRODUCTS]
    _draw_product_share_pie(pie_axis, [float(total) for total in totals])
    figure.text(0.84, 0.815, "Participación por producto", ha="center", fontsize=12)
    handles = [
        Line2D(
            [0],
            [0],
            marker="o",
            color="w",
            markerfacecolor=PRODUCTS[product][1],
            markersize=7,
            label=PRODUCTS[product][0],
        )
        for product in PRODUCTS
    ]
    figure.legend(
        handles=handles,
        loc="upper center",
        bbox_to_anchor=(0.84, 0.785),
        ncols=3,
        frameon=False,
        handlelength=0,
        handletextpad=0.3,
        columnspacing=0.8,
    )
    note = (
        "• Alcance: Estación de Servicio Automotriz,\n"
        "  Comercializador Industrial y Estación de Servicio\n"
        "  Fluvial, salvo indicación contraria.\n"
        "• Fuente: Datos Abiertos de combustibles líquidos\n"
        "  a nivel nacional.\n"
        "• Medida: volumen despachado."
    )
    add_note(figure, note, (0.715, 0.115, 0.22, 0.20), fontsize=7.8)
    return PageResult(figure)
