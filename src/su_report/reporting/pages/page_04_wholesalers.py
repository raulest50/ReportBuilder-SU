from __future__ import annotations

import textwrap

import matplotlib.pyplot as plt
import numpy as np
import squarify
from matplotlib.ticker import FuncFormatter

from su_report.reporting.context import PageResult, ReportContext
from su_report.reporting.data import ReportData
from su_report.reporting.style import COLORS, PRODUCTS, add_header, add_note, millions, new_page, style_chart

PROVIDER_COLORS = {
    "TERPEL": COLORS["red"],
    "PRIMAX": COLORS["orange"],
    "CHEVRON": COLORS["blue"],
    "BIOMAX": "#30A84A",
    "PETROMIL": COLORS["purple"],
    "PETROBRAS": "#AD9200",
    "ZEUSS": "#66A9E9",
    "DISCOM": "#5B3A92",
}


def render(context: ReportContext, data: ReportData) -> PageResult:
    figure = new_page()
    add_header(
        figure,
        context,
        f"Ventas de combustibles {context.period_label} - Mayoristas",
        width=0.45,
    )
    selected = data.mayoristas.loc[
        data.mayoristas["year"].eq(context.year)
        & data.mayoristas["month"].isin(context.months)
    ].copy()
    current = (
        selected.loc[selected["product"].eq("GASOLINA MOTOR CORRIENTE")]
        .groupby("provider_short", as_index=False)["accepted_volume"]
        .sum()
        .sort_values("accepted_volume", ascending=False)
    )
    current["share"] = current["accepted_volume"] / current["accepted_volume"].sum() * 100
    tree_axis = figure.add_axes((0.045, 0.43, 0.49, 0.39))
    tree_colors = [
        PROVIDER_COLORS.get(name, plt.cm.tab20(index % 20))
        for index, name in enumerate(current["provider_short"])
    ]
    labels = [
        f"{name}\n{share:.2f}%" if share >= 2 else (name if share >= 1 else "")
        for name, share in zip(current["provider_short"], current["share"], strict=True)
    ]
    squarify.plot(
        sizes=current["accepted_volume"],
        label=labels,
        color=tree_colors,
        alpha=1,
        ax=tree_axis,
        text_kwargs={"fontsize": 8, "color": "white", "weight": "bold"},
        bar_kwargs={"edgecolor": "white", "linewidth": 1},
    )
    tree_axis.set_title(
        f"Participación por mayorista ({context.period_short} {context.year}) - Corriente (%)",
        loc="left",
        pad=7,
    )
    tree_axis.set_axis_off()

    monthly_top = (
        selected.loc[selected["product"].eq("GASOLINA MOTOR CORRIENTE")]
        .groupby(["month", "provider_short"], as_index=False)["accepted_volume"]
        .sum()
    )
    line_axis = figure.add_axes((0.065, 0.08, 0.35, 0.27))
    for provider in list(current.head(5)["provider_short"]):
        series = monthly_top.loc[monthly_top["provider_short"].eq(provider)].set_index("month").reindex(
            context.months
        )
        line_axis.plot(
            context.months,
            series["accepted_volume"],
            marker="s",
            linewidth=1.1,
            markersize=4,
            color=PROVIDER_COLORS.get(provider),
            label=provider,
        )
    line_axis.set_xticks(context.months)
    line_axis.yaxis.set_major_formatter(FuncFormatter(millions))
    line_axis.set_xlabel("MES")
    line_axis.set_ylabel("Volumen aceptado (gal)")
    line_axis.legend(
        loc="center left",
        bbox_to_anchor=(1.01, 0.5),
        frameon=False,
        fontsize=7.5,
        handlelength=0.6,
        handletextpad=0.3,
    )
    style_chart(line_axis)

    product_provider = selected.groupby(["provider_short", "product"], as_index=False)["accepted_volume"].sum()
    provider_order = list(
        selected.groupby("provider_short")["accepted_volume"].sum().sort_values(ascending=False).head(18).index
    )[::-1]
    bar_axis = figure.add_axes((0.69, 0.11, 0.27, 0.69))
    y = np.arange(len(provider_order))
    for offset, product in zip([-0.22, 0, 0.22], PRODUCTS, strict=True):
        values = (
            product_provider.loc[product_provider["product"].eq(product)]
            .set_index("provider_short")["accepted_volume"]
            .reindex(provider_order, fill_value=0)
        )
        label, color = PRODUCTS[product]
        bar_axis.barh(y + offset, values, height=0.2, color=color, label=label)
    bar_axis.set_yticks(y, [textwrap.shorten(name, width=17, placeholder="…") for name in provider_order])
    bar_axis.xaxis.set_major_formatter(FuncFormatter(millions))
    bar_axis.set_xlabel("galones aceptados")
    bar_axis.legend(
        loc="lower left",
        bbox_to_anchor=(0, 1.01),
        ncols=3,
        frameon=False,
        handlelength=0.7,
        handletextpad=0.3,
    )
    style_chart(bar_axis, grid_axis="x")
    add_note(
        figure,
        "• Fuente: cubo Ordenes-Pedidos del SICOM.\n"
        "• Medida: volumen aceptado.\n"
        "• Proveedor: distribuidor mayorista.",
        (0.47, 0.015, 0.20, 0.075),
        fontsize=7.2,
    )
    return PageResult(figure)
