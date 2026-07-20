from __future__ import annotations

import json
import unicodedata
from collections.abc import Iterable
from pathlib import Path
from typing import Any

import matplotlib.pyplot as plt
import numpy as np
import pandas as pd
from matplotlib.collections import PatchCollection
from matplotlib.colors import LinearSegmentedColormap, TwoSlopeNorm
from matplotlib.path import Path as MplPath
from matplotlib.patches import PathPatch

from su_report.map_geometry import draw_department_contours, visible_features


EXCLUDED_DEPARTMENT = "ARCHIPIELAGO DE SAN ANDRES, SANTA CATALINA Y PROVIDENCIA"
NO_DATA_COLOR = "#f2f2f2"
SATURATION_LIMIT_PCT = 12.0
RED_GRAY_GREEN = LinearSegmentedColormap.from_list(
    "red_gray_green",
    [(0.0, "#8f1d1d"), (0.5, NO_DATA_COLOR), (1.0, "#147a38")],
)
RED_GRAY_GREEN.set_bad(NO_DATA_COLOR)


def _normalize(value: object) -> str:
    text = unicodedata.normalize("NFKD", str(value).upper())
    text = "".join(character for character in text if not unicodedata.combining(character))
    return " ".join(text.replace(",", " ").split())


def _features(path: Path) -> list[dict[str, Any]]:
    payload = json.loads(path.read_text(encoding="utf-8"))
    features = payload.get("features")
    if not isinstance(features, list):
        raise ValueError("El GeoJSON no contiene una lista de features.")
    return features


def _polygons(geometry: dict[str, Any]) -> Iterable[list[list[list[float]]]]:
    geometry_type = geometry.get("type")
    coordinates = geometry.get("coordinates", [])
    if geometry_type == "Polygon":
        yield coordinates
    elif geometry_type == "MultiPolygon":
        yield from coordinates


def _rings(geometry: dict[str, Any]) -> Iterable[list[list[float]]]:
    for polygon in _polygons(geometry):
        yield from polygon


def _patch(polygon: list[list[list[float]]]) -> PathPatch:
    vertices: list[tuple[float, float]] = []
    codes: list[int] = []
    for ring in polygon:
        if not ring:
            continue
        first = (float(ring[0][0]), float(ring[0][1]))
        vertices.append(first)
        codes.append(MplPath.MOVETO)
        for point in ring[1:]:
            vertices.append((float(point[0]), float(point[1])))
            codes.append(MplPath.LINETO)
        vertices.append(first)
        codes.append(MplPath.CLOSEPOLY)
    return PathPatch(MplPath(vertices, codes))


def _bounds(features: list[dict[str, Any]]) -> tuple[float, float, float, float]:
    points = [
        (float(point[0]), float(point[1]))
        for feature in features
        for ring in _rings(feature.get("geometry") or {})
        for point in ring
    ]
    if not points:
        raise ValueError("El GeoJSON no contiene coordenadas dibujables.")
    xs, ys = zip(*points, strict=True)
    return min(xs), min(ys), max(xs), max(ys)


def draw_variation_map(
    axis: plt.Axes,
    geojson_path: Path,
    comparison: pd.DataFrame,
    *,
    product: str,
    base_year: int,
    comparison_year: int,
    period_short: str,
) -> None:
    features = visible_features(_features(geojson_path), EXCLUDED_DEPARTMENT)
    geography_column = str(comparison.columns[0])
    variation_by_department = {
        _normalize(row[geography_column]): float(row["variation"])
        for _, row in comparison.iterrows()
        if pd.notna(row["variation"])
    }

    patches: list[PathPatch] = []
    values: list[float] = []
    for feature in features:
        properties = feature.get("properties") or {}
        department = _normalize(properties.get("NAME_1", ""))
        value = variation_by_department.get(department, np.nan)
        for polygon in _polygons(feature.get("geometry") or {}):
            patches.append(_patch(polygon))
            values.append(value)

    norm = TwoSlopeNorm(vmin=-SATURATION_LIMIT_PCT, vcenter=0, vmax=SATURATION_LIMIT_PCT)
    collection = PatchCollection(
        patches,
        cmap=RED_GRAY_GREEN,
        norm=norm,
        linewidths=0.06,
        edgecolors="#ffffff",
        zorder=1,
    )
    collection.set_array(np.asarray(values, dtype="float64"))
    axis.add_collection(collection)
    draw_department_contours(axis, features)

    min_x, min_y, max_x, max_y = _bounds(features)
    axis.set_xlim(min_x, max_x)
    axis.set_ylim(min_y, max_y)
    axis.set_aspect("equal", adjustable="box")
    axis.set_title(
        f"{product.title()}\n{period_short} {comparison_year} vs {period_short} {base_year}",
        fontsize=10,
        fontweight="bold",
    )
    axis.axis("off")
    colorbar = axis.figure.colorbar(collection, ax=axis, fraction=0.034, pad=0.01, extend="both")
    colorbar.set_label("Variación relativa (%)", fontsize=8)
    colorbar.ax.tick_params(labelsize=7)
