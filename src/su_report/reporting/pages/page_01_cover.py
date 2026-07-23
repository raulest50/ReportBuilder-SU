from __future__ import annotations

import textwrap

import matplotlib.image as mpimg
import matplotlib.pyplot as plt
from matplotlib.lines import Line2D
from matplotlib.patches import Rectangle

from su_report.reporting.context import PageResult, ReportContext
from su_report.reporting.data import ReportData
from su_report.reporting.style import add_brand, new_page

COVER_COLORS = {
    "navy": "#004975",
    "yellow": "#EBB835",
    "red": "#C0192F",
    "gray_dark": "#707070",
    "border": "#1F1F1F",
}


def _draw_cover_art(axis: plt.Axes, context: ReportContext) -> None:
    cover_art = axis.imshow(
        mpimg.imread(context.cover_art),
        extent=(0, 1, 1, 0),
        aspect="auto",
        interpolation="none",
        resample=False,
        zorder=0,
    )
    cover_art.set_gid("cover-art")


def _draw_cover_footer(figure: plt.Figure) -> None:
    segments = (
        (0.036, 0.593, COVER_COLORS["navy"]),
        (0.593, 0.742, COVER_COLORS["yellow"]),
        (0.742, 0.855, COVER_COLORS["navy"]),
        (0.855, 0.963, COVER_COLORS["red"]),
    )
    for index, (start, end, color) in enumerate(segments):
        line = Line2D(
            [start, end],
            [0.052, 0.052],
            transform=figure.transFigure,
            color=color,
            linewidth=1.4,
            solid_capstyle="butt",
            zorder=50,
        )
        if index == 0:
            line.set_gid("cover-footer")
        figure.add_artist(line)


def _draw_cover_border(figure: plt.Figure) -> None:
    margin = 0.0025
    border = Rectangle(
        (margin, margin),
        1 - 2 * margin,
        1 - 2 * margin,
        transform=figure.transFigure,
        fill=False,
        edgecolor=COVER_COLORS["border"],
        linewidth=0.8,
        zorder=100,
    )
    border.set_gid("cover-border")
    figure.add_artist(border)


def render(context: ReportContext, _data: ReportData) -> PageResult:
    figure = new_page()
    axis = figure.add_axes((0, 0, 1, 1))
    axis.set_xlim(0, 1)
    axis.set_ylim(1, 0)
    axis.set_autoscale_on(False)
    axis.set_axis_off()

    _draw_cover_art(axis, context)
    add_brand(figure, context, x=0.80, y=0.855, width=0.18, height=0.10)
    title = textwrap.fill(context.title, width=14, break_long_words=False, break_on_hyphens=False)
    title_text = figure.text(
        0.782,
        0.615,
        title,
        ha="center",
        va="center",
        fontsize=52,
        fontweight="bold",
        color=COVER_COLORS["navy"],
        linespacing=1.10,
    )
    title_text.set_gid("cover-title")
    period_name = context.period_title.removesuffix(f" de {context.year}")
    period_text = figure.text(
        0.780,
        0.335,
        f"{period_name}\n{context.year}",
        ha="center",
        va="center",
        fontsize=31,
        color=COVER_COLORS["gray_dark"],
        linespacing=1.28,
    )
    period_text.set_gid("cover-period")
    if context.partial:
        draft_text = figure.text(
            0.780,
            0.245,
            "BORRADOR · PERIODO PARCIAL",
            ha="center",
            va="center",
            fontsize=9,
            fontweight="bold",
            color=COVER_COLORS["red"],
        )
        draft_text.set_gid("cover-partial")
    area_text = figure.text(
        0.780,
        0.180,
        "Coordinación de Regulación\ny\nAnálisis de Sector",
        ha="center",
        va="center",
        fontsize=14,
        fontweight="bold",
        color=COVER_COLORS["border"],
        linespacing=1.2,
    )
    area_text.set_gid("cover-area")
    _draw_cover_footer(figure)
    _draw_cover_border(figure)
    return PageResult(figure)
