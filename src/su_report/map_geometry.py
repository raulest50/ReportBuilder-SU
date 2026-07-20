from __future__ import annotations

from collections.abc import Iterable
from typing import Any

import matplotlib.pyplot as plt
import numpy as np
from matplotlib.path import Path as MplPath

try:
    from scipy import ndimage
except ImportError:  # pragma: no cover - fallback para entornos livianos.
    ndimage = None


DEPARTMENT_BOUNDARY_COLOR = "#111111"
DEPARTMENT_BOUNDARY_WIDTH = 0.62
DEPARTMENT_CONTOUR_GRID_WIDTH = 900
DEPARTMENT_CONTOUR_MIN_LENGTH = 0.75


def visible_features(
    features: list[dict[str, Any]],
    excluded_department: str,
) -> list[dict[str, Any]]:
    return [
        feature
        for feature in features
        if (feature.get("properties") or {}).get("NAME_1") != excluded_department
    ]


def iter_polygons(geometry: dict[str, Any]) -> Iterable[list[list[list[float]]]]:
    geom_type = geometry.get("type")
    coordinates = geometry.get("coordinates", [])
    if geom_type == "Polygon":
        yield coordinates
    elif geom_type == "MultiPolygon":
        yield from coordinates


def iter_polygon_rings(geometry: dict[str, Any]) -> Iterable[list[list[float]]]:
    for polygon in iter_polygons(geometry):
        yield from polygon


def geojson_bounds(features: list[dict[str, Any]]) -> tuple[float, float, float, float]:
    xs: list[float] = []
    ys: list[float] = []
    for feature in features:
        geometry = feature.get("geometry") or {}
        for ring in iter_polygon_rings(geometry):
            for x, y in ring:
                xs.append(float(x))
                ys.append(float(y))

    if not xs or not ys:
        raise ValueError("El GeoJSON no contiene coordenadas dibujables.")
    return min(xs), min(ys), max(xs), max(ys)


def ring_to_path(ring: list[list[float]]) -> MplPath | None:
    points = [(float(point[0]), float(point[1])) for point in ring if len(point) >= 2]
    if len(points) < 3:
        return None
    if points[0] != points[-1]:
        points.append(points[0])
    codes = [MplPath.MOVETO] + [MplPath.LINETO] * (len(points) - 2) + [MplPath.CLOSEPOLY]
    return MplPath(points, codes)


def ring_bounds(ring: list[list[float]]) -> tuple[float, float, float, float] | None:
    points = [(float(point[0]), float(point[1])) for point in ring if len(point) >= 2]
    if not points:
        return None
    xs = [point[0] for point in points]
    ys = [point[1] for point in points]
    return min(xs), min(ys), max(xs), max(ys)


def build_department_shapes(features: list[dict[str, Any]]) -> dict[str, list[dict[str, Any]]]:
    department_shapes: dict[str, list[dict[str, Any]]] = {}
    for feature in features:
        props = feature.get("properties") or {}
        department = props.get("NAME_1")
        if not department:
            continue
        geometry = feature.get("geometry") or {}
        for polygon in iter_polygons(geometry):
            if not polygon:
                continue
            exterior = ring_to_path(polygon[0])
            bounds = ring_bounds(polygon[0])
            if exterior is None or bounds is None:
                continue
            holes = [
                hole_path
                for ring in polygon[1:]
                if (hole_path := ring_to_path(ring)) is not None
            ]
            department_shapes.setdefault(str(department), []).append(
                {"bounds": bounds, "exterior": exterior, "holes": holes}
            )
    return department_shapes


def department_grid(
    features: list[dict[str, Any]],
) -> tuple[np.ndarray, np.ndarray, np.ndarray, list[str]]:
    min_x, min_y, max_x, max_y = geojson_bounds(features)
    width = DEPARTMENT_CONTOUR_GRID_WIDTH
    height = max(1, round(width * (max_y - min_y) / (max_x - min_x)))

    xs = np.linspace(min_x, max_x, width)
    ys = np.linspace(min_y, max_y, height)
    x_grid, y_grid = np.meshgrid(xs, ys)
    points = np.column_stack([x_grid.ravel(), y_grid.ravel()])
    dept_grid = np.full(points.shape[0], -1, dtype=np.int16)

    department_shapes = build_department_shapes(features)
    departments = sorted(department_shapes)
    for dept_id, department in enumerate(departments):
        for shape in department_shapes[department]:
            shape_min_x, shape_min_y, shape_max_x, shape_max_y = shape["bounds"]
            candidate_mask = (
                (points[:, 0] >= shape_min_x)
                & (points[:, 0] <= shape_max_x)
                & (points[:, 1] >= shape_min_y)
                & (points[:, 1] <= shape_max_y)
            )
            candidate_indices = np.flatnonzero(candidate_mask)
            if candidate_indices.size == 0:
                continue

            candidate_points = points[candidate_indices]
            inside = shape["exterior"].contains_points(candidate_points)
            for hole in shape["holes"]:
                inside &= ~hole.contains_points(candidate_points)
            dept_grid[candidate_indices[inside]] = dept_id

    return x_grid, y_grid, dept_grid.reshape(x_grid.shape), departments


def draw_department_contours(ax: plt.Axes, features: list[dict[str, Any]]) -> None:
    x_grid, y_grid, dept_grid, departments = department_grid(features)
    for dept_id, _department in enumerate(departments):
        mask = dept_grid == dept_id
        if ndimage is not None:
            mask = ndimage.binary_fill_holes(mask)
        mask = mask.astype(float)
        if mask.max() == 0:
            continue
        contour_set = ax.contour(
            x_grid,
            y_grid,
            mask,
            levels=[0.5],
            colors=DEPARTMENT_BOUNDARY_COLOR,
            linewidths=DEPARTMENT_BOUNDARY_WIDTH,
            zorder=2,
        )
        for collection in getattr(contour_set, "collections", []):
            filtered_paths = []
            for path in collection.get_paths():
                vertices = path.vertices
                if len(vertices) < 2:
                    continue
                length = np.hypot(
                    np.diff(vertices[:, 0]),
                    np.diff(vertices[:, 1]),
                ).sum()
                if length >= DEPARTMENT_CONTOUR_MIN_LENGTH:
                    filtered_paths.append(path)
            collection.set_paths(filtered_paths)
