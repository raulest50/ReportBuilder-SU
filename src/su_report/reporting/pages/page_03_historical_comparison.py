from __future__ import annotations

from matplotlib.ticker import FuncFormatter

from su_report.reporting.context import PageResult, ReportContext
from su_report.reporting.data import ReportData, annual_product, annual_table
from su_report.reporting.style import COLORS, PRODUCTS, add_header, add_note, millions, new_page, style_chart
from su_report.reporting.tables import draw_table


def render(context: ReportContext, data: ReportData) -> PageResult:
    monthly = data.monthly
    figure = new_page()
    period_reference = (
        f"el {context.period_noun} {context.period_number}"
        if context.period_number is not None
        else f"cada {context.period_noun}"
    )
    add_header(
        figure,
        context,
        f"Comparación del volumen despachado para {period_reference}, "
        f"{context.history_start_year}-{context.year}",
        width=0.77,
    )
    main_axis = figure.add_axes((0.07, 0.46, 0.39, 0.33))
    for product in ("GASOLINA MOTOR CORRIENTE", "BIODIESEL CON MEZCLA"):
        annual = annual_product(context, monthly, product)
        label, color = PRODUCTS[product]
        main_axis.plot(
            annual["anio_despacho"], annual["volumen_total"], marker="o", linewidth=2, color=color, label=label
        )
    main_axis.set_title(
        f"{context.period_title.replace(f' de {context.year}', '')} de cada año - Corriente y ACPM",
        fontsize=12,
    )
    main_axis.set_xticks(range(context.history_start_year, context.year + 1))
    main_axis.yaxis.set_major_formatter(FuncFormatter(millions))
    main_axis.set_ylabel("gal")
    main_axis.legend(
        loc="upper left",
        bbox_to_anchor=(0, 1.06),
        frameon=False,
        ncols=2,
        handlelength=0.5,
        handletextpad=0.3,
    )
    style_chart(main_axis)

    extra_axis = figure.add_axes((0.59, 0.56, 0.35, 0.23))
    extra = annual_product(context, monthly, "GASOLINA MOTOR EXTRA")
    extra_axis.plot(
        extra["anio_despacho"], extra["volumen_total"], marker="o", linewidth=2, color=COLORS["gray"]
    )
    extra_axis.set_title(
        f"{context.period_title.replace(f' de {context.year}', '')} de cada año - Extra", fontsize=12
    )
    extra_axis.set_xticks(range(context.history_start_year, context.year + 1))
    extra_axis.yaxis.set_major_formatter(FuncFormatter(millions))
    extra_axis.set_ylabel("gal")
    style_chart(extra_axis)

    for product, x, y in [
        ("GASOLINA MOTOR CORRIENTE", 0.055, 0.11),
        ("BIODIESEL CON MEZCLA", 0.30, 0.11),
        ("GASOLINA MOTOR EXTRA", 0.65, 0.235),
    ]:
        label, _color = PRODUCTS[product]
        series_label = "Serie anual" if context.period_kind == "annual" else f"{context.period_short} de cada año"
        figure.text(x + 0.105, y + 0.23, f"{label} - {series_label}", ha="center", fontsize=11.5)
        table_axis = figure.add_axes((x, y, 0.21, 0.20))
        draw_table(
            table_axis,
            annual_table(context, monthly, product),
            col_widths=[0.18, 0.48, 0.34],
            font_size=7.7,
            header_rule="cell-bottom",
        )
    note = (
        "• Alcance comprador: EDS automotriz, comercializador industrial y EDS fluvial.\n"
        "• Fuente: Datos Abiertos; medida: volumen despachado.\n"
        + (
            "• La variación compara cada año con el año anterior."
            if context.period_kind == "annual"
            else f"• La variación compara cada {context.period_noun} con el mismo periodo del año anterior."
        )
    )
    add_note(figure, note, (0.575, 0.055, 0.39, 0.105), fontsize=7.9)
    return PageResult(figure)
