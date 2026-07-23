from __future__ import annotations

import textwrap

import pandas as pd
from matplotlib.lines import Line2D
from matplotlib.patches import Rectangle

from su_report.reporting.context import PageResult, ReportContext
from su_report.reporting.data import ReportData, annual_product, geo_comparison, geo_value
from su_report.reporting.style import COLORS, add_brand, new_page


def render(context: ReportContext, data: ReportData) -> PageResult:
    figure = new_page()
    figure.add_artist(
        Rectangle(
            (0.055, 0.855),
            0.016,
            0.09,
            transform=figure.transFigure,
            facecolor="#6F6F6F",
            edgecolor="none",
        )
    )
    figure.text(0.085, 0.91, "Conclusiones", fontsize=30, color=COLORS["ink"], va="center")
    annual_current = annual_product(context, data.monthly, "GASOLINA MOTOR CORRIENTE").set_index(
        "anio_despacho"
    )
    annual_diesel = annual_product(context, data.monthly, "BIODIESEL CON MEZCLA").set_index(
        "anio_despacho"
    )
    current_variation = float(
        (
            annual_current.loc[context.year, "volumen_total"]
            / annual_current.loc[context.previous_year, "volumen_total"]
            - 1
        )
        * 100
    )
    diesel_variation = float(
        (
            annual_diesel.loc[context.year, "volumen_total"]
            / annual_diesel.loc[context.previous_year, "volumen_total"]
            - 1
        )
        * 100
    )
    current_dep = geo_comparison(
        context, data.departments, "departamento", "GASOLINA MOTOR CORRIENTE"
    )
    diesel_dep = geo_comparison(context, data.departments, "departamento", "BIODIESEL CON MEZCLA")
    top_current = current_dep.assign(
        delta=current_dep[context.year] - current_dep[context.previous_year]
    ).nlargest(3, "delta")
    top_diesel = diesel_dep.assign(
        delta=diesel_dep[context.year] - diesel_dep[context.previous_year]
    ).nlargest(4, "delta")
    negative_diesel = diesel_dep.nsmallest(5, "variation")

    def list_changes(frame: pd.DataFrame) -> str:
        parts = []
        for _, row in frame.iterrows():
            parts.append(
                f"{str(row.iloc[0]).title()} "
                f"({(row[context.year] - row[context.previous_year]) / 1_000_000:+.2f} M; "
                f"{row['variation']:+.2f}%)"
            )
        return ", ".join(parts)

    paragraphs = [
        "La tendencia de recuperación de la gasolina corriente se mantiene. "
        f"El volumen despachado nacional varió {current_variation:+.2f}% frente al "
        f"{context.period_noun} equivalente de {context.previous_year}.",
        "El ACPM continúa siendo el principal motor del aumento de combustibles líquidos, "
        f"con una variación interanual de {diesel_variation:.2f}%. El resultado es compatible "
        "con una mayor actividad de transporte de carga, logística e industria.",
        f"En gasolina corriente, los mayores aumentos absolutos se observaron en {list_changes(top_current)}. "
        f"Bogotá D.C. registró {geo_value(current_dep, 'BOGOTA D.C.', 'variation'):+.2f}%.",
        f"En ACPM, los incrementos más importantes se concentraron en {list_changes(top_diesel)}.",
        "Persisten reducciones localizadas, especialmente en "
        + ", ".join(
            f"{str(row.iloc[0]).title()} ({row['variation']:+.2f}%)"
            for _, row in negative_diesel.iterrows()
        )
        + ". Estas variaciones deben interpretarse junto con la dinámica fronteriza y la "
        "actualización operativa del periodo.",
    ]
    y = 0.82
    for number, paragraph in enumerate(paragraphs, start=1):
        wrapped = textwrap.fill(paragraph, width=122, break_long_words=False, break_on_hyphens=False)
        figure.text(
            0.06,
            y,
            f"{number}.",
            fontsize=11.2,
            color="#242424",
            va="top",
            fontweight="bold",
            gid=f"conclusion-number-{number}",
        )
        figure.text(
            0.082,
            y,
            wrapped,
            fontsize=11.2,
            color="#242424",
            va="top",
            linespacing=1.35,
            fontweight="normal",
            gid=f"conclusion-body-{number}",
        )
        y -= 0.135 if number != len(paragraphs) else 0.12
    figure.add_artist(
        Line2D(
            [0.035, 0.71],
            [0.08, 0.08],
            transform=figure.transFigure,
            color=COLORS["green"],
            linewidth=16,
            solid_capstyle="round",
        )
    )
    figure.add_artist(
        Line2D(
            [0.48, 0.64],
            [0.08, 0.08],
            transform=figure.transFigure,
            color="#7CC44E",
            linewidth=16,
            solid_capstyle="round",
        )
    )
    figure.add_artist(
        Line2D(
            [0.64, 0.72],
            [0.08, 0.08],
            transform=figure.transFigure,
            color="#999999",
            linewidth=16,
            solid_capstyle="round",
        )
    )
    add_brand(figure, context, x=0.75, y=0.035, width=0.18)
    return PageResult(
        figure,
        metrics={
            "current_variation_pct": current_variation,
            "diesel_variation_pct": diesel_variation,
        },
    )
