from __future__ import annotations

import matplotlib.pyplot as plt
import pandas as pd
from matplotlib.patches import FancyBboxPatch

from su_report.reporting.context import PageResult, ReportContext
from su_report.reporting.data import ReportData
from su_report.reporting.style import COLORS, PRODUCTS, add_header, new_page, style_chart
from su_report.reporting.tables import draw_table


def _decimal_comma(value: float, decimals: int = 2) -> str:
    formatted = f"{value:,.{decimals}f}"
    return formatted.replace(",", "\ue000").replace(".", ",").replace("\ue000", ".")


def _millions(value: float, *, signed: bool = False) -> str:
    sign = "+" if signed and value > 0 else ""
    return f"{sign}{_decimal_comma(value / 1_000_000)} M"


def _variation(value: float) -> str:
    if pd.isna(value):
        return "n/d"
    sign = "+" if value > 0 else ""
    return f"{sign}{_decimal_comma(value)}%"


def _integer(value: float) -> str:
    return f"{int(round(value)):,}".replace(",", ".")


def _period_code(context: ReportContext) -> str:
    if context.period_kind == "quarter" and context.period_number is not None:
        return f"Q{context.period_number} {context.year}"
    if context.period_kind == "semester" and context.period_number is not None:
        return f"S{context.period_number} {context.year}"
    if context.period_kind == "annual":
        return str(context.year)
    return f"{context.period_short} {context.year}"


def _summary_display(context: ReportContext, summary: pd.DataFrame) -> pd.DataFrame:
    labels = {"SI": "Zona frontera", "NO": "No frontera", "TOTAL NACIONAL": "Nacional"}
    return pd.DataFrame(
        {
            "Grupo": summary["ZONA_FRONTERA"].map(labels),
            f"{context.period_short} {context.previous_year}": summary[
                "VOLUMEN_DESPACHADO_ANIO_ANTERIOR"
            ].map(_millions),
            f"{context.period_short} {context.year}": summary["VOLUMEN_DESPACHADO"].map(
                _millions
            ),
            "Cambio absoluto": summary["CAMBIO_ABSOLUTO_DESPACHADO"].map(
                lambda value: _millions(value, signed=True)
            ),
            "Variación": summary["VAR_INTERANUAL_DESPACHADO_PCT"].map(_variation),
            "EDS activas*": summary["EDS_ACTIVAS"].map(_integer),
            "Participación volumen": summary["PARTICIPACION_VOLUMEN_NACIONAL_PCT"].map(
                lambda value: f"{_decimal_comma(value)}%"
            ),
            "Gal/mes/EDS": summary["GAL_MES_EDS_DESPACHADO"].map(
                lambda value: f"{_decimal_comma(value / 1_000, 1)} mil"
            ),
        }
    )


def _draw_summary_table(
    figure: plt.Figure,
    context: ReportContext,
    summary: pd.DataFrame,
) -> None:
    figure.text(
        0.045,
        0.858,
        "Fotografía analítica del periodo",
        fontsize=10.8,
        color=COLORS["ink"],
        fontweight="bold",
        va="top",
    )
    figure.text(
        0.265,
        0.813,
        "Evolución del volumen",
        fontsize=7.3,
        color=COLORS["muted"],
        ha="center",
    )
    figure.text(
        0.762,
        0.813,
        "Estructura operativa",
        fontsize=7.3,
        color=COLORS["muted"],
        ha="center",
    )
    axis = figure.add_axes((0.045, 0.615, 0.91, 0.18))
    table = draw_table(
        axis,
        _summary_display(context, summary),
        col_widths=[0.14, 0.115, 0.115, 0.125, 0.09, 0.10, 0.165, 0.15],
        font_size=7.7,
        header_font_size=7.5,
        header_rule="cell-bottom",
        scale_y=1.0,
    )
    total_row = len(summary)
    for (row, column), cell in table.get_celld().items():
        if row == total_row:
            cell.set_text_props(weight="bold", color=COLORS["ink"])
        if row > 0 and column in (3, 4):
            raw = float(summary.iloc[row - 1][
                "CAMBIO_ABSOLUTO_DESPACHADO" if column == 3 else "VAR_INTERANUAL_DESPACHADO_PCT"
            ])
            cell.get_text().set_color(COLORS["green_dark"] if raw >= 0 else COLORS["red"])


def _draw_change_bridge(figure: plt.Figure, summary: pd.DataFrame) -> None:
    indexed = summary.set_index("ZONA_FRONTERA")
    frontier_change = float(indexed.loc["SI", "CAMBIO_ABSOLUTO_DESPACHADO"])
    other_change = float(indexed.loc["NO", "CAMBIO_ABSOLUTO_DESPACHADO"])
    national_change = float(indexed.loc["TOTAL NACIONAL", "CAMBIO_ABSOLUTO_DESPACHADO"])
    other_variation = float(indexed.loc["NO", "VAR_INTERANUAL_DESPACHADO_PCT"])
    national_variation = float(indexed.loc["TOTAL NACIONAL", "VAR_INTERANUAL_DESPACHADO_PCT"])
    difference_pp = other_variation - national_variation
    operator = "-" if frontier_change < 0 else "+"
    direction = "redujo" if difference_pp >= 0 else "elevó"

    box = FancyBboxPatch(
        (0.045, 0.505),
        0.91,
        0.075,
        boxstyle="round,pad=0.008,rounding_size=0.008",
        transform=figure.transFigure,
        facecolor="#EDF6E8",
        edgecolor="#BDD9AD",
        linewidth=0.8,
    )
    figure.add_artist(box)
    figure.text(
        0.07,
        0.548,
        f"No frontera {_millions(other_change, signed=True)}  {operator}  Zona frontera "
        f"{_millions(abs(frontier_change))}  =  Nacional {_millions(national_change, signed=True)}",
        fontsize=11.0,
        color=COLORS["ink"],
        fontweight="bold",
        va="center",
    )
    figure.text(
        0.07,
        0.518,
        f"La dinámica fronteriza {direction} en {_decimal_comma(abs(difference_pp))} pp la variación "
        "nacional frente al resultado no fronterizo.",
        fontsize=7.8,
        color=COLORS["muted"],
        va="center",
    )


def _draw_municipalities(figure: plt.Figure, municipalities: pd.DataFrame) -> None:
    ordered = municipalities.sort_values("RANK_MUNICIPIO", ascending=True).copy()
    labels = (
        ordered["MUNICIPIO"].astype(str).str.title()
        + " - "
        + ordered["DEPARTAMENTO"].astype(str).str.title()
    )
    volumes = pd.to_numeric(ordered["VOLUMEN_DESPACHADO"])
    shares = pd.to_numeric(ordered["PARTICIPACION_VOLUMEN_FRONTERA_PCT"])
    figure.text(
        0.055,
        0.455,
        "Municipios ZFD de mayor volumen despachado",
        fontsize=10.5,
        color=COLORS["ink"],
        fontweight="bold",
        va="top",
    )
    axis = figure.add_axes((0.195, 0.145, 0.37, 0.265))
    bars = axis.barh(range(len(ordered)), volumes, color=COLORS["green_dark"], height=0.55)
    axis.set_yticks(range(len(ordered)), labels, fontsize=7.8)
    axis.invert_yaxis()
    axis.set_xlim(0, float(volumes.max()) * 1.42)
    axis.set_xticks([])
    style_chart(axis, grid_axis="x")
    for bar, volume, share in zip(bars, volumes, shares, strict=True):
        axis.text(
            bar.get_width() + float(volumes.max()) * 0.025,
            bar.get_y() + bar.get_height() / 2,
            f"{_millions(float(volume))} | {_decimal_comma(float(share))}%",
            va="center",
            fontsize=7.4,
            color=COLORS["ink"],
        )


def _draw_product_mix(figure: plt.Figure, products: pd.DataFrame, municipalities: pd.DataFrame) -> None:
    frontier = products.loc[products["ZONA_FRONTERA"].astype(str).str.upper().eq("SI")].copy()
    order = ["corriente", "diesel", "extra"]
    frontier["order"] = frontier["PRODUCTO_CANONICO"].map({name: index for index, name in enumerate(order)})
    frontier = frontier.sort_values("order")
    shares = {
        str(row["PRODUCTO_CANONICO"]): float(row["PARTICIPACION_PRODUCTO_EN_ZONA_PCT"])
        for _, row in frontier.iterrows()
    }
    figure.text(
        0.625,
        0.455,
        "Mezcla de producto exclusivamente en ZFD",
        fontsize=10.5,
        color=COLORS["ink"],
        fontweight="bold",
        va="top",
    )
    axis = figure.add_axes((0.625, 0.285, 0.32, 0.10))
    left = 0.0
    for canonical in order:
        label, color = next(
            value for key, value in PRODUCTS.items() if value[0].lower() == ("acpm" if canonical == "diesel" else canonical)
        )
        share = shares[canonical]
        axis.barh([0], [share], left=left, color=color, height=0.42)
        if share >= 6:
            axis.text(
                left + share / 2,
                0,
                f"{label}\n{_decimal_comma(share)}%",
                ha="center",
                va="center",
                fontsize=7.3,
                color="white",
                fontweight="bold",
            )
        else:
            axis.annotate(
                f"{label} {_decimal_comma(share)}%",
                xy=(left + share / 2, 0.21),
                xytext=(left + share / 2, 0.62),
                ha="center",
                va="bottom",
                fontsize=7.0,
                color=COLORS["muted"],
                arrowprops={"arrowstyle": "-", "color": COLORS["gray"], "linewidth": 0.7},
            )
        left += share
    axis.set_xlim(0, 100)
    axis.set_ylim(-0.6, 0.95)
    axis.set_axis_off()

    concentration = float(
        pd.to_numeric(municipalities["PARTICIPACION_VOLUMEN_FRONTERA_PCT"]).sum()
    )
    figure.text(
        0.625,
        0.22,
        f"Los cinco municipios concentran {_decimal_comma(concentration)}% del volumen despachado en ZFD.",
        fontsize=8.0,
        color=COLORS["ink"],
        va="top",
    )
    figure.text(
        0.625,
        0.18,
        "La mezcla fronteriza tiene mayor peso del ACPM y una participación marginal de gasolina extra.",
        fontsize=7.7,
        color=COLORS["muted"],
        va="top",
        wrap=True,
    )


def render(context: ReportContext, data: ReportData) -> PageResult:
    figure = new_page()
    add_header(
        figure,
        context,
        f"Zonas de frontera frente al resto del país — {_period_code(context)}",
        width=0.64,
    )
    summary = data.zfd.summary.copy()
    products = data.zfd.products.copy()
    municipalities = data.zfd.municipalities.copy()
    _draw_summary_table(figure, context, summary)
    _draw_change_bridge(figure, summary)
    _draw_municipalities(figure, municipalities)
    _draw_product_mix(figure, products, municipalities)

    queried_at = pd.to_datetime(data.zfd.active_eds_queried_at_utc, utc=True).strftime("%Y-%m-%d")
    figure.text(
        0.045,
        0.046,
        "Fuente: SICOM, cubos Ordenes-Pedidos y SBI-Agentes. Volumen despachado; EDS automotrices; "
        "gasolina corriente, gasolina extra y biodiésel con mezcla.",
        fontsize=6.8,
        color=COLORS["muted"],
        va="bottom",
    )
    figure.text(
        0.045,
        0.025,
        f"* EDS activas corresponde al estado observado en la consulta del {queried_at}; no es un "
        "inventario histórico al cierre del periodo.",
        fontsize=6.8,
        color=COLORS["muted"],
        va="bottom",
    )
    return PageResult(figure)
