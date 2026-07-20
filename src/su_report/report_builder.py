"""Genera las doce páginas vectoriales del informe configurado.

La capa de datos permanece separada de la composicion: este script lee los
insumos declarados en ``report.yml``, calcula las metricas y produce SVG. Quarto
y Typst ensamblan esos SVG en el PDF final.
"""

from __future__ import annotations

import json
import os
import textwrap
from pathlib import Path
from typing import Any

os.environ.setdefault("MPLCONFIGDIR", str(Path(__file__).resolve().parents[2] / ".matplotlib"))

import matplotlib as mpl
import matplotlib.image as mpimg
import matplotlib.pyplot as plt
import numpy as np
import pandas as pd
import squarify
import yaml
from matplotlib.lines import Line2D
from matplotlib.patches import FancyBboxPatch, Polygon, Rectangle
from matplotlib.ticker import FuncFormatter

from su_report.maps import draw_variation_map

CONFIG: dict[str, Any] = {}
REPORT: dict[str, Any] = {}
PROJECT_DIR = Path.cwd()
INPUT_DIR = Path.cwd()
BRAND_LOGO = Path()
GEOJSON_PATH = Path()
PAGES_DIR = Path.cwd()
METRICS_PATH = Path.cwd() / "metrics.json"
FONT_DIRS: list[Path] = []

YEAR = 0
PREVIOUS_YEAR = 0
PERIOD_KIND = "quarter"
PERIOD_NUMBER = 1
PERIOD_LABEL = ""
PERIOD_TITLE = ""
PERIOD_NOUN = "trimestre"
PERIOD_SHORT = "Trim. 1"
MONTHS: list[int] = []
HISTORY_START_YEAR = 0
PARTIAL = False
MONTH_LABELS = {1: "enero", 2: "febrero", 3: "marzo", 4: "abril", 5: "mayo", 6: "junio", 7: "julio", 8: "agosto", 9: "septiembre", 10: "octubre", 11: "noviembre", 12: "diciembre"}

PAGE_WIDTH = 13.833
PAGE_HEIGHT = 8.0

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


def configure(config_path: Path) -> None:
    global CONFIG, REPORT, PROJECT_DIR, INPUT_DIR, BRAND_LOGO, GEOJSON_PATH
    global PAGES_DIR, METRICS_PATH, FONT_DIRS, YEAR, PREVIOUS_YEAR
    global PERIOD_KIND, PERIOD_NUMBER, PERIOD_LABEL, PERIOD_TITLE, PERIOD_NOUN
    global PERIOD_SHORT, MONTHS, HISTORY_START_YEAR, PARTIAL

    CONFIG = yaml.safe_load(config_path.read_text(encoding="utf-8"))
    REPORT = CONFIG["report"]
    PROJECT_DIR = Path(str(CONFIG["project_dir"])).resolve()
    sources = CONFIG["sources"]
    outputs = CONFIG["outputs"]
    INPUT_DIR = Path(str(sources["input_dir"])).resolve()
    BRAND_LOGO = Path(str(sources["brand_logo"])).resolve()
    GEOJSON_PATH = Path(str(sources["geography"])).resolve()
    PAGES_DIR = Path(str(outputs["pages_dir"])).resolve()
    METRICS_PATH = Path(str(outputs["metrics"])).resolve()
    FONT_DIRS = [Path(str(path)).resolve() for path in CONFIG.get("font_dirs", [])]

    YEAR = int(REPORT["year"])
    PREVIOUS_YEAR = int(REPORT["comparison_year"])
    PERIOD_KIND = str(REPORT["period_kind"])
    PERIOD_NUMBER = int(REPORT["period_number"])
    PERIOD_LABEL = str(REPORT["period_label"])
    PERIOD_TITLE = str(REPORT["period_title"])
    PERIOD_NOUN = "trimestre" if PERIOD_KIND == "quarter" else "semestre"
    PERIOD_SHORT = ("Trim." if PERIOD_KIND == "quarter" else "Sem.") + f" {PERIOD_NUMBER}"
    MONTHS = [int(month) for month in REPORT["months"]]
    HISTORY_START_YEAR = int(REPORT["history_start_year"])
    PARTIAL = bool(REPORT.get("partial", False))


def configure_matplotlib() -> None:
    for font_dir in FONT_DIRS:
        if font_dir.exists():
            for font in font_dir.glob("segoe*.ttf"):
                try:
                    mpl.font_manager.fontManager.addfont(font)
                except RuntimeError:
                    pass
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


def load_data() -> dict[str, pd.DataFrame]:
    parte1 = pd.read_csv(INPUT_DIR / "national-monthly.csv")
    parte1["anio_despacho"] = pd.to_numeric(parte1["anio_despacho"])
    parte1["mes_despacho"] = pd.to_numeric(parte1["mes_despacho"])
    monthly = (
        parte1.loc[parte1["mes_despacho"].isin(MONTHS)]
        .groupby(["anio_despacho", "mes_despacho", "producto"], as_index=False)["volumen_total"]
        .sum()
    )

    departments = pd.read_csv(INPUT_DIR / "geography-departments.csv")
    municipalities = pd.read_csv(INPUT_DIR / "geography-municipalities.csv")
    for frame in (departments, municipalities):
        frame["volumen_total"] = pd.to_numeric(frame["volumen_total"], errors="coerce")
        frame["anio_despacho"] = pd.to_numeric(frame["anio_despacho"], errors="coerce")

    mayoristas = pd.read_csv(INPUT_DIR / "mayoristas.csv")
    required_mayoristas = ["provider", "month", "year", "product", "buyer_subtype", "accepted_volume"]
    mayoristas = mayoristas[required_mayoristas].copy()
    mayoristas["month"] = pd.to_numeric(mayoristas["month"], errors="coerce")
    mayoristas["year"] = pd.to_numeric(mayoristas["year"], errors="coerce")
    mayoristas["accepted_volume"] = pd.to_numeric(mayoristas["accepted_volume"], errors="coerce").fillna(0)
    mayoristas["provider_short"] = mayoristas["provider"].map(provider_alias)

    eds_active = pd.read_csv(INPUT_DIR / "eds-activas.csv", dtype={"CODIGO_DANE_DEPARTAMENTO": str, "CODIGO_DANE_MUNICIPIO": str})
    eds_volume = pd.read_csv(INPUT_DIR / "eds-municipios.csv", dtype={"CODIGO_DANE_DEPARTAMENTO": str, "CODIGO_DANE_MUNICIPIO": str})

    return {
        "monthly": monthly,
        "departments": departments,
        "municipalities": municipalities,
        "mayoristas": mayoristas,
        "eds_active": eds_active,
        "eds_volume": eds_volume,
    }


def provider_alias(value: str) -> str:
    upper = value.upper()
    keywords = {
        "TERPEL": "TERPEL",
        "PRIMAX": "PRIMAX",
        "CHEVRON": "CHEVRON",
        "BIOMAX": "BIOMAX",
        "PETROMIL": "PETROMIL",
        "PETROBRAS": "PETROBRAS",
        "ZEUSS": "ZEUSS",
        "DISCOM": "DISCOM",
        "PUMA": "PUMA ENERGY",
        "ECOSPETROLEO": "C.I. ECOSPETROLEO",
        "MULTIACTIVA": "COOMULPINORT",
        "AYATAWACOOP": "AYATAWACOOP",
        "PETRODECOL": "PETRODECOL",
        "PLUS MAS": "PLUS MAS ENERGY",
        "MARKETING OIL": "MARKETING OIL",
        "PROXXON": "PROXXON",
        "WAYUU": "COMBUSTIBLES WAYUU",
        "ZAPATA": "ZAPATA Y VELASQUEZ",
        "P Y B": "P Y B PETROLEOS",
    }
    for keyword, label in keywords.items():
        if keyword in upper:
            return label
    return value.split("(")[0].strip()[:24]


def new_page() -> plt.Figure:
    figure = plt.figure(figsize=(PAGE_WIDTH, PAGE_HEIGHT), dpi=120)
    return figure


def save_page(figure: plt.Figure, page_number: int) -> None:
    PAGES_DIR.mkdir(parents=True, exist_ok=True)
    figure.savefig(
        PAGES_DIR / f"page-{page_number:02d}.svg",
        format="svg",
        facecolor="white",
        edgecolor="none",
        metadata={"Title": f"{REPORT['title']} — pagina {page_number}"},
    )
    plt.close(figure)


def add_brand(figure: plt.Figure, x: float = 0.82, y: float = 0.895, width: float = 0.145) -> None:
    axis = figure.add_axes([x, y, width, 0.07])
    axis.imshow(mpimg.imread(BRAND_LOGO))
    axis.set_axis_off()


def add_header(figure: plt.Figure, title: str, *, width: float = 0.47, brand: bool = True) -> None:
    figure.text(0.04, 0.94, title, fontsize=16, fontweight="bold", color=COLORS["ink"], va="top")
    figure.add_artist(Line2D([0.04, 0.04 + width], [0.905, 0.905], transform=figure.transFigure, color=COLORS["line"], linewidth=1.2))
    if brand:
        add_brand(figure)


def add_note(figure: plt.Figure, text: str, rect: tuple[float, float, float, float], *, fontsize: float = 8.5) -> None:
    x, y, width, height = rect
    shadow = FancyBboxPatch((x + 0.008, y - 0.01), width, height, boxstyle="square,pad=0.012", transform=figure.transFigure, facecolor="#000000", alpha=0.14, edgecolor="none")
    box = FancyBboxPatch((x, y), width, height, boxstyle="square,pad=0.012", transform=figure.transFigure, facecolor=COLORS["green_light"], edgecolor="none")
    figure.add_artist(shadow)
    figure.add_artist(box)
    figure.text(x + 0.012, y + height - 0.018, text, fontsize=fontsize, color="#3A4A3D", va="top", linespacing=1.35)


def style_chart(axis: plt.Axes, *, grid_axis: str = "y") -> None:
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


def draw_table(
    axis: plt.Axes,
    frame: pd.DataFrame,
    *,
    col_widths: list[float] | None = None,
    font_size: float = 8,
    header_color: str = COLORS["ink"],
    scale_y: float = 1.18,
) -> None:
    axis.set_axis_off()
    table = axis.table(
        cellText=frame.astype(str).values,
        colLabels=list(frame.columns),
        colWidths=col_widths,
        loc="upper left",
        cellLoc="right",
        colLoc="center",
        bbox=[0, 0, 1, 1],
    )
    table.auto_set_font_size(False)
    table.set_fontsize(font_size)
    table.scale(1, scale_y)
    for (row, column), cell in table.get_celld().items():
        cell.set_linewidth(0)
        cell.PAD = 0.025
        if row == 0:
            cell.set_text_props(weight="bold", color=header_color)
            cell.set_facecolor("white")
        else:
            cell.set_facecolor(COLORS["stripe"] if row % 2 == 0 else "white")
        if column == 0:
            cell.get_text().set_ha("left")
    axis.add_line(Line2D([0, 1], [0.895, 0.895], transform=axis.transAxes, color="#2997FF", linewidth=0.8))


def monthly_product(monthly: pd.DataFrame, product: str) -> pd.DataFrame:
    selected = monthly.loc[monthly["producto"].eq(product)]
    pivot = selected.pivot_table(index="mes_despacho", columns="anio_despacho", values="volumen_total", aggfunc="sum").reindex(MONTHS)
    for year in (PREVIOUS_YEAR, YEAR):
        if year not in pivot:
            pivot[year] = np.nan
    return pivot


def comparison_table(monthly: pd.DataFrame, product: str) -> pd.DataFrame:
    pivot = monthly_product(monthly, product)
    rows: list[list[str]] = []
    for month in MONTHS:
        old = float(pivot.loc[month, PREVIOUS_YEAR])
        new = float(pivot.loc[month, YEAR])
        rows.append([MONTH_LABELS[month], fmt_millions(old), fmt_millions(new), fmt_variation(new, old)])
    old_total = float(pivot[PREVIOUS_YEAR].sum())
    new_total = float(pivot[YEAR].sum())
    rows.append(["Total", fmt_millions(old_total), fmt_millions(new_total), fmt_variation(new_total, old_total)])
    return pd.DataFrame(rows, columns=["Mes", f"{PREVIOUS_YEAR} (gal)", f"{YEAR} (gal)", "Var. relativa (%)"])


def annual_product(monthly: pd.DataFrame, product: str) -> pd.DataFrame:
    selected = monthly.loc[monthly["producto"].eq(product)].groupby("anio_despacho", as_index=False)["volumen_total"].sum()
    selected = selected.loc[selected["anio_despacho"].between(HISTORY_START_YEAR, YEAR)].sort_values("anio_despacho")
    selected["variation"] = selected["volumen_total"].pct_change().mul(100).fillna(0)
    return selected


def annual_table(monthly: pd.DataFrame, product: str) -> pd.DataFrame:
    frame = annual_product(monthly, product)
    return pd.DataFrame(
        {
            "Año": frame["anio_despacho"].astype(int),
            f"Vol. {PERIOD_SHORT} (gal)": frame["volumen_total"].map(fmt_millions),
            "Variación (%)": frame["variation"].map(lambda value: "n/d" if pd.isna(value) else f"{value:.2f}"),
        }
    )


def geo_comparison(frame: pd.DataFrame, geography: str, product: str) -> pd.DataFrame:
    selected = frame.loc[frame["producto"].eq(product)]
    pivot = selected.pivot_table(index=geography, columns="anio_despacho", values="volumen_total", aggfunc="sum", fill_value=0)
    pivot = pivot.loc[pivot.index.astype(str) != "-"].copy()
    for year in (PREVIOUS_YEAR, YEAR):
        if year not in pivot:
            pivot[year] = 0.0
    pivot["variation"] = np.where(pivot[PREVIOUS_YEAR] > 0, (pivot[YEAR] / pivot[PREVIOUS_YEAR] - 1) * 100, np.nan)
    pivot = pivot.sort_values(YEAR, ascending=False).reset_index()
    return pivot


def page_01() -> None:
    figure = new_page()
    axis = figure.add_axes([0, 0, 1, 1])
    axis.set_axis_off()
    rng = np.random.default_rng(2026)
    palette = ["#087FF2", "#36AD68", "#72D747", "#9DE27A", "#C4EDB8"]
    for index in range(16):
        center_x = rng.uniform(0.07, 0.48)
        center_y = rng.uniform(0.18, 0.78)
        width = rng.uniform(0.08, 0.22)
        height = rng.uniform(0.08, 0.28)
        vertices = np.array(
            [
                [center_x - width / 2, center_y - height / 2],
                [center_x + width / 2, center_y - height / 4],
                [center_x + rng.uniform(-0.02, 0.05), center_y + height / 2],
            ]
        )
        axis.add_patch(Polygon(vertices, closed=True, facecolor=palette[index % len(palette)], edgecolor="none", alpha=0.72))
    add_brand(figure)
    figure.text(0.76, 0.73, "Mercado de\ncombustibles\nlíquidos en\nColombia", ha="center", va="center", fontsize=32, fontweight="bold", color=COLORS["green_dark"], linespacing=1.2)
    figure.text(0.76, 0.36, PERIOD_TITLE.replace(f" de {YEAR}", f"\n{YEAR}"), ha="center", va="center", fontsize=25, color=COLORS["ink"], linespacing=1.35)
    if PARTIAL:
        figure.text(0.76, 0.27, "BORRADOR · PERIODO PARCIAL", ha="center", fontsize=10, fontweight="bold", color=COLORS["red"])
    figure.text(0.76, 0.16, "Coordinación de Regulación\ny\nAnálisis de Sector", ha="center", va="center", fontsize=13, fontweight="bold", color="#282828", linespacing=1.25)
    figure.add_artist(Line2D([0.02, 0.66], [0.075, 0.075], transform=figure.transFigure, color="#346B3B", linewidth=1.1))
    save_page(figure, 1)


def page_02(monthly: pd.DataFrame) -> None:
    figure = new_page()
    add_header(figure, f"Comportamiento de combustibles líquidos para el {PERIOD_LABEL}", width=0.61)

    axis = figure.add_axes([0.055, 0.53, 0.37, 0.27])
    for product in ("GASOLINA MOTOR CORRIENTE", "BIODIESEL CON MEZCLA"):
        pivot = monthly_product(monthly, product)
        label, color = PRODUCTS[product]
        axis.plot(MONTHS, pivot[YEAR], marker="o", linewidth=2, color=color, label=label)
    axis.set_title(f"Consumo Corriente y ACPM - {PERIOD_SHORT} {YEAR}", loc="left", pad=22)
    axis.set_xticks(MONTHS, [MONTH_LABELS[month] for month in MONTHS])
    axis.yaxis.set_major_formatter(FuncFormatter(millions))
    axis.set_ylabel("Volumen (gal)")
    axis.legend(loc="upper left", bbox_to_anchor=(0, 1.16), ncols=2, frameon=False, handlelength=0.5, handletextpad=0.3)
    style_chart(axis)

    extra_axis = figure.add_axes([0.055, 0.12, 0.37, 0.25])
    extra = monthly_product(monthly, "GASOLINA MOTOR EXTRA")
    extra_axis.plot(MONTHS, extra[YEAR], marker="o", linewidth=2, color=COLORS["gray"])
    extra_axis.set_title(f"Consumo Extra - {PERIOD_SHORT} {YEAR}", loc="left", pad=18)
    extra_axis.set_xticks(MONTHS, [MONTH_LABELS[month] for month in MONTHS])
    extra_axis.yaxis.set_major_formatter(FuncFormatter(lambda value, _: f"{value / 1_000_000:.1f}M"))
    extra_axis.set_ylabel("Volumen (gal)")
    style_chart(extra_axis)

    table_positions = [(0.455, 0.585), (0.455, 0.345), (0.455, 0.105)]
    for (product, (x, y)) in zip(PRODUCTS, table_positions, strict=True):
        label, _color = PRODUCTS[product]
        figure.text(x + 0.115, y + 0.205, f"Comparación mes a mes - {label}", ha="center", fontsize=11.5)
        table_axis = figure.add_axes([x, y, 0.23, 0.17])
        draw_table(table_axis, comparison_table(monthly, product), col_widths=[0.16, 0.27, 0.27, 0.30], font_size=7.7)

    pie_axis = figure.add_axes([0.755, 0.47, 0.19, 0.32])
    totals = [monthly_product(monthly, product)[YEAR].sum() for product in PRODUCTS]
    pie_axis.pie(totals, colors=[PRODUCTS[p][1] for p in PRODUCTS], startangle=90, counterclock=False, autopct=lambda pct: f"{pct:.2f}%" if pct > 2 else f"{pct:.2f}%", textprops={"fontsize": 8, "color": COLORS["muted"]})
    pie_axis.set_title("Participación por producto", fontsize=12, pad=18)
    handles = [Line2D([0], [0], marker="o", color="w", markerfacecolor=PRODUCTS[p][1], markersize=7, label=PRODUCTS[p][0]) for p in PRODUCTS]
    pie_axis.legend(handles=handles, loc="upper center", bbox_to_anchor=(0.5, 1.10), ncols=3, frameon=False, handlelength=0, handletextpad=0.3, columnspacing=0.8)

    note = (
        "• Alcance: Estación de Servicio Automotriz, Comercializador Industrial y\n"
        "  Estación de Servicio Fluvial, salvo indicación contraria.\n"
        "• Fuente: Datos Abiertos de combustibles líquidos a nivel nacional.\n"
        "• Medida: volumen despachado."
    )
    add_note(figure, note, (0.72, 0.10, 0.245, 0.18), fontsize=8.2)
    save_page(figure, 2)


def page_03(monthly: pd.DataFrame) -> None:
    figure = new_page()
    add_header(figure, f"Comparación del volumen despachado para el {PERIOD_NOUN} {PERIOD_NUMBER} de cada año, {HISTORY_START_YEAR}-{YEAR}", width=0.77)

    main_axis = figure.add_axes([0.07, 0.46, 0.39, 0.33])
    for product in ("GASOLINA MOTOR CORRIENTE", "BIODIESEL CON MEZCLA"):
        annual = annual_product(monthly, product)
        label, color = PRODUCTS[product]
        main_axis.plot(annual["anio_despacho"], annual["volumen_total"], marker="o", linewidth=2, color=color, label=label)
    main_axis.set_title(f"{PERIOD_TITLE.replace(f' de {YEAR}', '')} de cada año - Corriente y ACPM", fontsize=12)
    main_axis.set_xticks(range(HISTORY_START_YEAR, YEAR + 1))
    main_axis.yaxis.set_major_formatter(FuncFormatter(millions))
    main_axis.set_ylabel("gal")
    main_axis.legend(loc="upper left", bbox_to_anchor=(0, 1.06), frameon=False, ncols=2, handlelength=0.5, handletextpad=0.3)
    style_chart(main_axis)

    extra_axis = figure.add_axes([0.59, 0.56, 0.35, 0.23])
    extra = annual_product(monthly, "GASOLINA MOTOR EXTRA")
    extra_axis.plot(extra["anio_despacho"], extra["volumen_total"], marker="o", linewidth=2, color=COLORS["gray"])
    extra_axis.set_title(f"{PERIOD_TITLE.replace(f' de {YEAR}', '')} de cada año - Extra", fontsize=12)
    extra_axis.set_xticks(range(HISTORY_START_YEAR, YEAR + 1))
    extra_axis.yaxis.set_major_formatter(FuncFormatter(millions))
    extra_axis.set_ylabel("gal")
    style_chart(extra_axis)

    table_specs = [
        ("GASOLINA MOTOR CORRIENTE", 0.055, 0.11),
        ("BIODIESEL CON MEZCLA", 0.30, 0.11),
        ("GASOLINA MOTOR EXTRA", 0.65, 0.235),
    ]
    for product, x, y in table_specs:
        label, _color = PRODUCTS[product]
        figure.text(x + 0.105, y + 0.23, f"{label} - {PERIOD_SHORT} de cada año", ha="center", fontsize=11.5)
        table_axis = figure.add_axes([x, y, 0.21, 0.20])
        draw_table(table_axis, annual_table(monthly, product), col_widths=[0.18, 0.48, 0.34], font_size=7.7)

    note = (
        "• Alcance comprador: EDS automotriz, comercializador industrial y EDS fluvial.\n"
        "• Fuente: Datos Abiertos; medida: volumen despachado.\n"
        f"• La variación compara cada {PERIOD_NOUN} con el mismo periodo del año anterior."
    )
    add_note(figure, note, (0.575, 0.055, 0.39, 0.105), fontsize=7.9)
    save_page(figure, 3)


def page_04(mayoristas: pd.DataFrame) -> None:
    figure = new_page()
    add_header(figure, f"Ventas de combustibles {PERIOD_LABEL} - Mayoristas", width=0.45)
    selected = mayoristas.loc[mayoristas["year"].eq(YEAR) & mayoristas["month"].isin(MONTHS)].copy()

    current = selected.loc[selected["product"].eq("GASOLINA MOTOR CORRIENTE")].groupby("provider_short", as_index=False)["accepted_volume"].sum().sort_values("accepted_volume", ascending=False)
    current["share"] = current["accepted_volume"] / current["accepted_volume"].sum() * 100
    tree_axis = figure.add_axes([0.045, 0.43, 0.49, 0.39])
    tree_colors = [PROVIDER_COLORS.get(name, plt.cm.tab20(index % 20)) for index, name in enumerate(current["provider_short"])]
    labels = [f"{name}\n{share:.2f}%" if share >= 2 else (name if share >= 1 else "") for name, share in zip(current["provider_short"], current["share"], strict=True)]
    squarify.plot(sizes=current["accepted_volume"], label=labels, color=tree_colors, alpha=1, ax=tree_axis, text_kwargs={"fontsize": 8, "color": "white", "weight": "bold"}, bar_kwargs={"edgecolor": "white", "linewidth": 1})
    tree_axis.set_title(f"Participación por mayorista ({PERIOD_SHORT} {YEAR}) - Corriente (%)", loc="left", pad=7)
    tree_axis.set_axis_off()

    monthly_top = (
        selected.loc[selected["product"].eq("GASOLINA MOTOR CORRIENTE")]
        .groupby(["month", "provider_short"], as_index=False)["accepted_volume"]
        .sum()
    )
    top_five = list(current.head(5)["provider_short"])
    line_axis = figure.add_axes([0.065, 0.08, 0.35, 0.27])
    for provider in top_five:
        series = monthly_top.loc[monthly_top["provider_short"].eq(provider)].set_index("month").reindex(MONTHS)
        line_axis.plot(MONTHS, series["accepted_volume"], marker="s", linewidth=1.1, markersize=4, color=PROVIDER_COLORS.get(provider), label=provider)
    line_axis.set_xticks(MONTHS)
    line_axis.yaxis.set_major_formatter(FuncFormatter(millions))
    line_axis.set_xlabel("MES")
    line_axis.set_ylabel("Volumen aceptado (gal)")
    line_axis.legend(loc="center left", bbox_to_anchor=(1.01, 0.5), frameon=False, fontsize=7.5, handlelength=0.6, handletextpad=0.3)
    style_chart(line_axis)

    product_provider = selected.groupby(["provider_short", "product"], as_index=False)["accepted_volume"].sum()
    provider_order = list(selected.groupby("provider_short")["accepted_volume"].sum().sort_values(ascending=False).head(18).index)[::-1]
    bar_axis = figure.add_axes([0.69, 0.11, 0.27, 0.69])
    y = np.arange(len(provider_order))
    offsets = [-0.22, 0, 0.22]
    for offset, product in zip(offsets, PRODUCTS, strict=True):
        values = product_provider.loc[product_provider["product"].eq(product)].set_index("provider_short")["accepted_volume"].reindex(provider_order, fill_value=0)
        label, color = PRODUCTS[product]
        bar_axis.barh(y + offset, values, height=0.2, color=color, label=label)
    bar_axis.set_yticks(y, [textwrap.shorten(name, width=24, placeholder="…") for name in provider_order])
    bar_axis.xaxis.set_major_formatter(FuncFormatter(millions))
    bar_axis.set_xlabel("galones aceptados")
    bar_axis.legend(loc="lower left", bbox_to_anchor=(0, 1.01), ncols=3, frameon=False, handlelength=0.7, handletextpad=0.3)
    style_chart(bar_axis, grid_axis="x")

    add_note(figure, "• Fuente: cubo Ordenes-Pedidos del SICOM.\n• Medida: volumen aceptado.\n• Proveedor: distribuidor mayorista.", (0.48, 0.13, 0.13, 0.12), fontsize=7.8)
    save_page(figure, 4)


def page_05(mayoristas: pd.DataFrame) -> None:
    figure = new_page()
    add_header(figure, "Participación de mayoristas - Histórico", width=0.44)
    selected = mayoristas.loc[mayoristas["month"].isin(MONTHS)].copy()
    totals = selected.groupby(["year", "provider_short"], as_index=False)["accepted_volume"].sum()
    yearly_totals = totals.groupby("year")["accepted_volume"].transform("sum")
    totals["share"] = np.where(yearly_totals > 0, totals["accepted_volume"] / yearly_totals * 100, 0)
    top = list(totals.groupby("provider_short")["accepted_volume"].sum().nlargest(8).index)
    axis = figure.add_axes([0.07, 0.14, 0.87, 0.68])
    bottom = np.zeros(len(range(HISTORY_START_YEAR, YEAR + 1)))
    years = list(range(HISTORY_START_YEAR, YEAR + 1))
    for provider in top:
        values = totals.loc[totals["provider_short"].eq(provider)].set_index("year")["share"].reindex(years, fill_value=0).to_numpy()
        axis.bar(years, values, bottom=bottom, label=provider, color=PROVIDER_COLORS.get(provider))
        bottom += values
    others = np.maximum(0, 100 - bottom)
    axis.bar(years, others, bottom=bottom, label="Otros", color="#B6B6B6")
    axis.set_xticks(years)
    axis.set_ylim(0, 100)
    axis.yaxis.set_major_formatter(FuncFormatter(lambda value, _: f"{value:.0f}%"))
    axis.set_ylabel("Participación en volumen aceptado")
    axis.legend(loc="upper center", bbox_to_anchor=(0.5, -0.08), ncols=5, frameon=False, fontsize=8)
    style_chart(axis)
    add_note(figure, "• Fuente: cubo Ordenes-Pedidos del SICOM.\n• Medida histórica: volumen aceptado.\n• Alcance: distribuidor mayorista.", (0.73, 0.68, 0.22, 0.12), fontsize=7.8)
    save_page(figure, 5)


def geo_bar(axis: plt.Axes, comparison: pd.DataFrame, color: str, title: str, *, rows: int = 27) -> None:
    data = comparison.head(rows).iloc[::-1]
    shares = data[YEAR] / comparison[YEAR].sum() * 100
    bars = axis.barh(data.iloc[:, 0].astype(str), shares, color=color, height=0.74)
    axis.set_title(title, fontsize=12, pad=12)
    axis.xaxis.set_major_formatter(FuncFormatter(lambda value, _: f"{value:.0f}%"))
    style_chart(axis, grid_axis="x")
    axis.bar_label(bars, labels=[f"{value:.2f}%" for value in shares], padding=4, fontsize=7.2, color=COLORS["muted"])
    axis.margins(x=0.14)


def page_geo(page_number: int, departments: pd.DataFrame, product: str) -> None:
    figure = new_page()
    add_header(figure, "Concentración espacial del consumo", width=0.45)
    map_axis = figure.add_axes([0.07, 0.10, 0.47, 0.76])
    comparison = geo_comparison(departments, "departamento", product)
    draw_variation_map(
        map_axis,
        GEOJSON_PATH,
        comparison,
        product=product,
        base_year=PREVIOUS_YEAR,
        comparison_year=YEAR,
        period_short=PERIOD_SHORT,
    )
    label, color = PRODUCTS[product]
    bar_axis = figure.add_axes([0.69, 0.10, 0.27, 0.72])
    geo_bar(bar_axis, comparison, color, f"Participación por departamento - {label} (%)")
    save_page(figure, page_number)


def page_08(municipalities: pd.DataFrame) -> None:
    figure = new_page()
    add_header(figure, "Concentración espacial del consumo", width=0.45)
    for product, x in (("GASOLINA MOTOR CORRIENTE", 0.06), ("BIODIESEL CON MEZCLA", 0.55)):
        comparison = geo_comparison(municipalities, "municipio", product)
        label, color = PRODUCTS[product]
        axis = figure.add_axes([x, 0.08, 0.38, 0.76])
        geo_bar(axis, comparison, color, f"Participación por municipio - {label} (%)", rows=28)
    save_page(figure, 8)


def comparison_display(comparison: pd.DataFrame, geography_label: str, *, rows: int) -> pd.DataFrame:
    subset = comparison.head(rows)
    return pd.DataFrame(
        {
            geography_label: subset.iloc[:, 0].astype(str).str.upper(),
            f"{PERIOD_SHORT} {PREVIOUS_YEAR} (gal)": subset[PREVIOUS_YEAR].map(fmt_millions),
            f"{PERIOD_SHORT} {YEAR} (gal)": subset[YEAR].map(fmt_millions),
            "Var. relativa (%)": subset["variation"].map(lambda value: "—" if pd.isna(value) else f"{value:.2f}"),
        }
    )


def page_09(departments: pd.DataFrame) -> None:
    figure = new_page()
    add_header(figure, f"Comparación entre {PERIOD_NOUN}s - Departamentos", width=0.46)
    for product, x in (("GASOLINA MOTOR CORRIENTE", 0.045), ("BIODIESEL CON MEZCLA", 0.53)):
        label, _color = PRODUCTS[product]
        figure.text(x + 0.215, 0.84, label, ha="center", fontsize=13)
        comparison = geo_comparison(departments, "departamento", product)
        table_axis = figure.add_axes([x, 0.08, 0.43, 0.72])
        draw_table(table_axis, comparison_display(comparison, "Departamento", rows=29), col_widths=[0.31, 0.25, 0.25, 0.19], font_size=6.9, scale_y=1.0)
    save_page(figure, 9)


def page_10(municipalities: pd.DataFrame) -> None:
    figure = new_page()
    add_header(figure, f"Comparación entre {PERIOD_NOUN}s - Municipios", width=0.43)
    for product, x in (("GASOLINA MOTOR CORRIENTE", 0.045), ("BIODIESEL CON MEZCLA", 0.53)):
        label, _color = PRODUCTS[product]
        figure.text(x + 0.215, 0.84, label, ha="center", fontsize=13)
        comparison = geo_comparison(municipalities, "municipio", product)
        table_axis = figure.add_axes([x, 0.08, 0.43, 0.72])
        draw_table(table_axis, comparison_display(comparison, "Municipio", rows=29), col_widths=[0.31, 0.25, 0.25, 0.19], font_size=6.9, scale_y=1.0)
    save_page(figure, 10)


def eds_summary(active: pd.DataFrame, volume: pd.DataFrame) -> pd.DataFrame:
    selected = volume.loc[
        volume["ANO"].eq(YEAR)
        & volume["MES"].isin(MONTHS)
        & volume["PRODUCTO_CANONICO"].isin(["corriente", "diesel", "extra"])
    ]
    volume_by_municipality = selected.groupby("CODIGO_DANE_MUNICIPIO", as_index=False)["VOLUMEN_ACEPTADO"].sum()
    active_unique = (
        active.sort_values("EDS_AUTOMOTRIZ_ACTIVAS", ascending=False)
        .drop_duplicates("CODIGO_DANE_MUNICIPIO")
        [["CODIGO_DANE_MUNICIPIO", "DEPARTAMENTO", "MUNICIPIO", "ES_ZONA_FRONTERA", "EDS_AUTOMOTRIZ_ACTIVAS"]]
    )
    merged = active_unique.merge(volume_by_municipality, on="CODIGO_DANE_MUNICIPIO", how="left")
    merged["VOLUMEN_ACEPTADO"] = merged["VOLUMEN_ACEPTADO"].fillna(0)
    merged["GAL_MES_EDS"] = np.where(merged["EDS_AUTOMOTRIZ_ACTIVAS"] > 0, merged["VOLUMEN_ACEPTADO"] / (len(MONTHS) * merged["EDS_AUTOMOTRIZ_ACTIVAS"]), 0)
    merged["SHARE_EDS"] = merged["EDS_AUTOMOTRIZ_ACTIVAS"] / merged["EDS_AUTOMOTRIZ_ACTIVAS"].sum() * 100
    return merged.sort_values(["EDS_AUTOMOTRIZ_ACTIVAS", "VOLUMEN_ACEPTADO"], ascending=False)


def page_11(active: pd.DataFrame, volume: pd.DataFrame) -> None:
    figure = new_page()
    add_header(figure, "Estaciones de Servicio Automotriz", width=0.45)
    summary = eds_summary(active, volume).head(27)
    display = pd.DataFrame(
        {
            "Departamento": summary["DEPARTAMENTO"],
            "Municipio": summary["MUNICIPIO"],
            "EDS activas": summary["EDS_AUTOMOTRIZ_ACTIVAS"].map(lambda value: f"{int(value):,}"),
            f"Volumen {PERIOD_SHORT}\n3 productos (gal)": summary["VOLUMEN_ACEPTADO"].map(fmt_millions),
            f"Gal/mes/EDS {PERIOD_SHORT}": summary["GAL_MES_EDS"].map(lambda value: f"{value / 1000:.0f}K"),
            "Zona frontera": summary["ES_ZONA_FRONTERA"],
            "Participación EDS (%)": summary["SHARE_EDS"].map(lambda value: f"{value:.2f}%"),
        }
    )
    table_axis = figure.add_axes([0.045, 0.08, 0.92, 0.76])
    draw_table(table_axis, display, col_widths=[0.13, 0.15, 0.10, 0.18, 0.14, 0.13, 0.17], font_size=6.8, scale_y=1.0)
    save_page(figure, 11)


def wrap_paragraph(text: str, width: int = 125) -> str:
    return textwrap.fill(text, width=width, break_long_words=False, break_on_hyphens=False)


def geo_value(comparison: pd.DataFrame, name: str, column: int | str) -> float:
    return float(comparison.loc[comparison.iloc[:, 0].astype(str).str.upper().eq(name.upper()), column].iloc[0])


def page_12(monthly: pd.DataFrame, departments: pd.DataFrame) -> dict[str, float]:
    figure = new_page()
    figure.add_artist(Rectangle((0.055, 0.855), 0.016, 0.09, transform=figure.transFigure, facecolor="#6F6F6F", edgecolor="none"))
    figure.text(0.085, 0.91, "Conclusiones", fontsize=30, color=COLORS["ink"], va="center")

    annual_current = annual_product(monthly, "GASOLINA MOTOR CORRIENTE").set_index("anio_despacho")
    annual_diesel = annual_product(monthly, "BIODIESEL CON MEZCLA").set_index("anio_despacho")
    current_variation = float((annual_current.loc[YEAR, "volumen_total"] / annual_current.loc[PREVIOUS_YEAR, "volumen_total"] - 1) * 100)
    diesel_variation = float((annual_diesel.loc[YEAR, "volumen_total"] / annual_diesel.loc[PREVIOUS_YEAR, "volumen_total"] - 1) * 100)
    current_dep = geo_comparison(departments, "departamento", "GASOLINA MOTOR CORRIENTE")
    diesel_dep = geo_comparison(departments, "departamento", "BIODIESEL CON MEZCLA")

    top_current = current_dep.assign(delta=current_dep[YEAR] - current_dep[PREVIOUS_YEAR]).nlargest(3, "delta")
    top_diesel = diesel_dep.assign(delta=diesel_dep[YEAR] - diesel_dep[PREVIOUS_YEAR]).nlargest(4, "delta")
    negative_diesel = diesel_dep.nsmallest(5, "variation")

    def list_changes(frame: pd.DataFrame) -> str:
        parts = []
        for _, row in frame.iterrows():
            parts.append(f"{str(row.iloc[0]).title()} ({(row[YEAR] - row[PREVIOUS_YEAR]) / 1_000_000:+.2f} M; {row['variation']:+.2f}%)")
        return ", ".join(parts)

    paragraphs = [
        f"1. La tendencia de recuperación de la gasolina corriente se mantiene. El volumen despachado nacional varió {current_variation:+.2f}% frente al {PERIOD_NOUN} equivalente de {PREVIOUS_YEAR}.",
        f"2. El ACPM continúa siendo el principal motor del aumento de combustibles líquidos, con una variación interanual de {diesel_variation:.2f}%. El resultado es compatible con una mayor actividad de transporte de carga, logística e industria.",
        f"3. En gasolina corriente, los mayores aumentos absolutos se observaron en {list_changes(top_current)}. Bogotá D.C. registró {geo_value(current_dep, 'BOGOTA D.C.', 'variation'):+.2f}%.",
        f"4. En ACPM, los incrementos más importantes se concentraron en {list_changes(top_diesel)}.",
        "5. Persisten reducciones localizadas, especialmente en " + ", ".join(f"{str(row.iloc[0]).title()} ({row['variation']:+.2f}%)" for _, row in negative_diesel.iterrows()) + ". Estas variaciones deben interpretarse junto con la dinámica fronteriza y la actualización operativa del periodo.",
    ]

    y = 0.82
    for index, paragraph in enumerate(paragraphs):
        wrapped = wrap_paragraph(paragraph, 126)
        figure.text(0.06, y, wrapped, fontsize=11.2, color="#242424", va="top", linespacing=1.35, fontweight="bold" if index < 2 else "normal")
        y -= 0.135 if index != 4 else 0.12

    figure.add_artist(Line2D([0.035, 0.71], [0.08, 0.08], transform=figure.transFigure, color=COLORS["green"], linewidth=16, solid_capstyle="round"))
    figure.add_artist(Line2D([0.48, 0.64], [0.08, 0.08], transform=figure.transFigure, color="#7CC44E", linewidth=16, solid_capstyle="round"))
    figure.add_artist(Line2D([0.64, 0.72], [0.08, 0.08], transform=figure.transFigure, color="#999999", linewidth=16, solid_capstyle="round"))
    add_brand(figure, x=0.75, y=0.035, width=0.18)
    save_page(figure, 12)
    return {"current_variation_pct": current_variation, "diesel_variation_pct": diesel_variation}


def build_metrics(data: dict[str, pd.DataFrame], conclusion_metrics: dict[str, float]) -> dict[str, object]:
    monthly = data["monthly"]
    report_totals = {
        PRODUCTS[product][0].lower(): float(monthly_product(monthly, product)[YEAR].sum())
        for product in PRODUCTS
    }
    return {
        "report": {
            "year": YEAR,
            "period_kind": PERIOD_KIND,
            "period_number": PERIOD_NUMBER,
            "period_label": PERIOD_LABEL,
            "months": MONTHS,
            "comparison_year": PREVIOUS_YEAR,
            "cutoff_date": REPORT["cutoff_date"],
            "partial": PARTIAL,
        },
        "measure_contract": {
            "national_and_geographic": "volumen_despachado (Datos Abiertos)",
            "wholesalers_and_eds": "VOLUMEN ACEPTADO (SICOM OLAP)",
        },
        "national_volume_gallons": report_totals,
        "national_variation_pct": conclusion_metrics,
        "source_files": sorted(path.name for path in INPUT_DIR.glob("*.csv")),
        "pages": 12,
    }


def build_report(config_path: Path) -> dict[str, object]:
    configure(config_path)
    configure_matplotlib()
    data = load_data()
    page_01()
    page_02(data["monthly"])
    page_03(data["monthly"])
    page_04(data["mayoristas"])
    page_05(data["mayoristas"])
    page_geo(6, data["departments"], "GASOLINA MOTOR CORRIENTE")
    page_geo(7, data["departments"], "BIODIESEL CON MEZCLA")
    page_08(data["municipalities"])
    page_09(data["departments"])
    page_10(data["municipalities"])
    page_11(data["eds_active"], data["eds_volume"])
    conclusion_metrics = page_12(data["monthly"], data["departments"])
    METRICS_PATH.parent.mkdir(parents=True, exist_ok=True)
    metrics = build_metrics(data, conclusion_metrics)
    METRICS_PATH.write_text(json.dumps(metrics, ensure_ascii=False, indent=2), encoding="utf-8")
    return metrics


def main() -> None:
    import argparse

    parser = argparse.ArgumentParser()
    parser.add_argument("--config", required=True, type=Path)
    args = parser.parse_args()
    build_report(args.config)


if __name__ == "__main__":
    main()
