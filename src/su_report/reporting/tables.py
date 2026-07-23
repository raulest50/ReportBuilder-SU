from __future__ import annotations

from typing import Literal

import matplotlib.pyplot as plt
import pandas as pd
from matplotlib.lines import Line2D
from matplotlib.table import Table

from su_report.reporting.style import COLORS


def _create_table(
    axis: plt.Axes,
    frame: pd.DataFrame,
    *,
    col_widths: list[float] | None = None,
    font_size: float = 8,
    scale_y: float = 1.18,
    padding: float = 0.025,
) -> Table:
    axis.set_axis_off()
    table = axis.table(
        cellText=frame.astype(str).values,
        colLabels=list(frame.columns),
        colWidths=col_widths,
        loc="upper left",
        cellLoc="right",
        colLoc="center",
        bbox=[0, 0, 1, 1],  # type: ignore[arg-type]
    )
    table.auto_set_font_size(False)
    table.set_fontsize(font_size)
    table.scale(1, scale_y)
    for (row, column), cell in table.get_celld().items():
        cell.set_linewidth(0)
        cell.PAD = padding
        if column == 0:
            cell.get_text().set_ha("left")  # type: ignore[attr-defined]
        elif row > 0:
            cell.get_text().set_ha("right")  # type: ignore[attr-defined]
    return table


def draw_table(
    axis: plt.Axes,
    frame: pd.DataFrame,
    *,
    col_widths: list[float] | None = None,
    font_size: float = 8,
    header_font_size: float | None = None,
    header_color: str = COLORS["ink"],
    header_rule: Literal["fixed", "cell-bottom"] = "fixed",
    scale_y: float = 1.18,
) -> Table:
    if header_rule not in ("fixed", "cell-bottom"):
        raise ValueError(f"Modo de línea de encabezado inválido: {header_rule}")
    table = _create_table(
        axis,
        frame,
        col_widths=col_widths,
        font_size=font_size,
        scale_y=scale_y,
    )
    for (row, _column), cell in table.get_celld().items():
        if row == 0:
            cell.set_text_props(
                weight="bold",
                color=header_color,
                fontsize=header_font_size if header_font_size is not None else font_size,
            )
            cell.set_facecolor("white")
        else:
            cell.set_facecolor(COLORS["stripe"] if row % 2 == 0 else "white")
    if header_rule == "fixed":
        axis.add_line(Line2D([0, 1], [0.895, 0.895], transform=axis.transAxes, color="#2997FF", linewidth=0.8))
    else:
        axis.figure.canvas.draw()
        header_bottom = table.get_celld()[(0, 0)].get_y()
        axis.add_line(
            Line2D(
                [0, 1],
                [header_bottom, header_bottom],
                transform=axis.transAxes,
                color="#2997FF",
                linewidth=0.8,
            )
        )
    return table


def draw_monthly_comparison_table(
    axis: plt.Axes,
    frame: pd.DataFrame,
    *,
    col_widths: list[float] | None = None,
    font_size: float = 7.7,
) -> Table:
    table = _create_table(
        axis,
        frame,
        col_widths=col_widths,
        font_size=font_size,
        scale_y=1.0,
        padding=0.018,
    )
    total_row = len(frame)
    for (row, _column), cell in table.get_celld().items():
        cell.set_facecolor("white")
        cell.set_edgecolor("#2997FF")
        if row == 0:
            cell.set_text_props(weight="bold", color=COLORS["ink"])
            cell.visible_edges = "B"
            cell.set_linewidth(0.9)
        elif row == total_row:
            cell.set_text_props(weight="bold", color=COLORS["green"])
            cell.visible_edges = "T"
            cell.set_linewidth(0.9)
        else:
            cell.set_text_props(weight="normal", color=COLORS["ink"])
            cell.set_linewidth(0)
            if row % 2 == 0:
                cell.set_facecolor(COLORS["stripe"])
    return table
