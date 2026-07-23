from __future__ import annotations

import os
from contextlib import suppress
from pathlib import Path
from typing import Literal

os.environ.setdefault("MPLCONFIGDIR", str(Path(__file__).resolve().parents[3] / ".matplotlib"))

import matplotlib as mpl
import matplotlib.image as mpimg
import matplotlib.pyplot as plt
import pandas as pd
from matplotlib.lines import Line2D
from matplotlib.patches import FancyBboxPatch

from su_report.reporting.context import ReportContext

PAGE_WIDTH = 13.833
PAGE_HEIGHT = 8.0

MONTH_LABELS = {
    1: "enero",
    2: "febrero",
    3: "marzo",
    4: "abril",
    5: "mayo",
    6: "junio",
    7: "julio",
    8: "agosto",
    9: "septiembre",
    10: "octubre",
    11: "noviembre",
    12: "diciembre",
}

COLORS = {
    "ink": "#2F3A40",
    "muted": "#6C6C6C",
    "line": "#6D6D6D",
    "green": "#35B65A",
    "green_dark": "#006B35",
    "green_light": "#C6E2B5",
    "blue": "#0B70C4",
    "gray": "#7D7D7D",
    "stripe": "#ECECEC",
    "grid": "#D7D7D7",
    "red": "#F3262C",
    "orange": "#ED7137",
    "purple": "#7B50C7",
}

PRODUCTS = {
    "GASOLINA MOTOR CORRIENTE": ("Corriente", COLORS["green"]),
    "BIODIESEL CON MEZCLA": ("ACPM", COLORS["blue"]),
    "GASOLINA MOTOR EXTRA": ("Extra", COLORS["gray"]),
}


def configure_matplotlib(font_dirs: tuple[Path, ...]) -> None:
    for font_dir in font_dirs:
        if font_dir.exists():
            for font in font_dir.glob("segoe*.ttf"):
                with suppress(RuntimeError):
                    mpl.font_manager.fontManager.addfont(font)
    mpl.rcParams.update(
        {
            "font.family": "Segoe UI",
            "font.size": 9,
            "axes.titlesize": 12,
            "axes.labelsize": 9,
            "xtick.labelsize": 8,
            "ytick.labelsize": 8,
            "svg.fonttype": "none",
            "figure.facecolor": "white",
            "axes.facecolor": "white",
        }
    )


def new_page() -> plt.Figure:
    return plt.figure(figsize=(PAGE_WIDTH, PAGE_HEIGHT), dpi=120)


def add_brand(
    figure: plt.Figure,
    context: ReportContext,
    x: float = 0.82,
    y: float = 0.895,
    width: float = 0.145,
    height: float = 0.07,
) -> None:
    axis = figure.add_axes((x, y, width, height))
    axis.imshow(mpimg.imread(context.brand_logo))
    axis.set_axis_off()


def add_header(
    figure: plt.Figure,
    context: ReportContext,
    title: str,
    *,
    width: float = 0.47,
    brand: bool = True,
) -> None:
    figure.text(0.04, 0.94, title, fontsize=16, fontweight="bold", color=COLORS["ink"], va="top")
    figure.add_artist(
        Line2D(
            [0.04, 0.04 + width],
            [0.905, 0.905],
            transform=figure.transFigure,
            color=COLORS["line"],
            linewidth=1.2,
        )
    )
    if brand:
        add_brand(figure, context)


def add_note(
    figure: plt.Figure,
    text: str,
    rect: tuple[float, float, float, float],
    *,
    fontsize: float = 8.5,
) -> None:
    x, y, width, height = rect
    shadow = FancyBboxPatch(
        (x + 0.008, y - 0.01),
        width,
        height,
        boxstyle="square,pad=0.012",
        transform=figure.transFigure,
        facecolor="#000000",
        alpha=0.14,
        edgecolor="none",
    )
    box = FancyBboxPatch(
        (x, y),
        width,
        height,
        boxstyle="square,pad=0.012",
        transform=figure.transFigure,
        facecolor=COLORS["green_light"],
        edgecolor="none",
    )
    figure.add_artist(shadow)
    figure.add_artist(box)
    figure.text(
        x + 0.012,
        y + height - 0.018,
        text,
        fontsize=fontsize,
        color="#3A4A3D",
        va="top",
        linespacing=1.35,
    )


def style_chart(axis: plt.Axes, *, grid_axis: Literal["both", "x", "y"] = "y") -> None:
    for spine in axis.spines.values():
        spine.set_visible(False)
    axis.tick_params(axis="both", length=0, colors="#5F5F5F")
    axis.grid(axis=grid_axis, linestyle=(0, (1, 4)), linewidth=0.7, color=COLORS["grid"])
    axis.set_axisbelow(True)


def millions(value: float, _position: int | None = None) -> str:
    return f"{value / 1_000_000:.0f}M"


def fmt_millions(value: float) -> str:
    if pd.isna(value):
        return "n/d"
    return f"{value / 1_000_000:.2f}M"


def fmt_variation(new: float, old: float) -> str:
    if pd.isna(new) or pd.isna(old) or old == 0:
        return "n/d"
    return f"{(new / old - 1) * 100:.2f}"
