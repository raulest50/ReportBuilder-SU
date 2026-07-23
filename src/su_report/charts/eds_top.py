from __future__ import annotations

import re
from dataclasses import dataclass

import matplotlib.pyplot as plt
import numpy as np
import pandas as pd
from matplotlib.ticker import FuncFormatter, MaxNLocator

BAR_COLOR = "#006B35"
REQUIRED_COLUMNS = (
    "RANK_EDS",
    "CODIGO_SICOM_COMPRADOR",
    "NOMBRE_COMERCIAL_COMPRADOR",
    "MUNICIPIO",
    "DEPARTAMENTO",
    "VOLUMEN_DESPACHADO",
    "MEDIDA_RANKING",
    "VOLUMEN_RANKING",
    "PARTICIPACION_NACIONAL_PCT",
)


class EdsTopChartError(ValueError):
    """Raised when the Top EDS input violates its visual contract."""


@dataclass(frozen=True)
class EdsTopChartData:
    frame: pd.DataFrame


def strip_trailing_identifier(value: object) -> str:
    return re.sub(r"\s*\(\d+\)\s*$", "", str(value).strip()).strip()


def _title_case(value: object) -> str:
    text = re.sub(r"\s+", " ", strip_trailing_identifier(value))
    return text.title().replace("Eds ", "EDS ")


def _format_percent(value: float) -> str:
    return f"{value:.2f}".replace(".", ",") + "%"


def format_colombian_thousands(value: float, _position: float | None = None) -> str:
    return f"{int(round(value)):,}".replace(",", ".")


def prepare_eds_top(frame: pd.DataFrame, *, expected_top: int = 20) -> EdsTopChartData:
    missing = sorted(set(REQUIRED_COLUMNS) - set(frame.columns))
    if missing:
        raise EdsTopChartError("Faltan columnas Top EDS: " + ", ".join(missing) + ".")
    if len(frame) != expected_top:
        raise EdsTopChartError(f"Se esperaban {expected_top} EDS y se recibieron {len(frame)}.")

    data = frame.loc[:, REQUIRED_COLUMNS].copy()
    for column in (
        "CODIGO_SICOM_COMPRADOR",
        "NOMBRE_COMERCIAL_COMPRADOR",
        "MUNICIPIO",
        "DEPARTAMENTO",
    ):
        if data[column].isna().any() or data[column].astype(str).str.strip().eq("").any():
            raise EdsTopChartError(f"Top EDS contiene valores vacíos en {column}.")

    numeric_columns = (
        "RANK_EDS",
        "VOLUMEN_DESPACHADO",
        "VOLUMEN_RANKING",
        "PARTICIPACION_NACIONAL_PCT",
    )
    for column in numeric_columns:
        data[column] = pd.to_numeric(data[column], errors="coerce")
    numeric = data.loc[:, numeric_columns]
    if numeric.isna().any().any() or not np.isfinite(numeric.to_numpy(dtype=float)).all():
        raise EdsTopChartError("Top EDS contiene valores numéricos nulos o no finitos.")
    if (numeric < 0).any().any():
        raise EdsTopChartError("Top EDS contiene valores negativos.")
    if data["RANK_EDS"].tolist() != list(range(1, expected_top + 1)):
        raise EdsTopChartError("El ranking Top EDS debe ser consecutivo y estar ordenado.")
    if not data["MEDIDA_RANKING"].astype(str).str.lower().eq("dispatched").all():
        raise EdsTopChartError("La gráfica del informe exige MEDIDA_RANKING=dispatched.")
    if not np.allclose(
        data["VOLUMEN_RANKING"].to_numpy(dtype=float),
        data["VOLUMEN_DESPACHADO"].to_numpy(dtype=float),
        rtol=0.0,
        atol=1e-6,
    ):
        raise EdsTopChartError("VOLUMEN_RANKING no coincide con VOLUMEN_DESPACHADO.")
    if data["PARTICIPACION_NACIONAL_PCT"].gt(100).any():
        raise EdsTopChartError("La participación nacional debe estar entre 0 y 100.")

    data["station_label"] = data["NOMBRE_COMERCIAL_COMPRADOR"].map(strip_trailing_identifier)
    data["location_label"] = [
        f"{_title_case(municipality)} ({_title_case(department)})"
        for municipality, department in zip(data["MUNICIPIO"], data["DEPARTAMENTO"], strict=True)
    ]
    data["annotation"] = data["PARTICIPACION_NACIONAL_PCT"].map(
        lambda value: f"{_format_percent(float(value))} del volumen nacional"
    )
    return EdsTopChartData(data)


def draw_eds_top(axis: plt.Axes, data: EdsTopChartData) -> None:
    frame = data.frame
    positions = np.arange(len(frame))
    volumes = frame["VOLUMEN_DESPACHADO"].to_numpy(dtype=float)
    axis.barh(
        positions,
        volumes,
        height=0.72,
        color=BAR_COLOR,
        edgecolor="white",
        linewidth=0.35,
        zorder=3,
    )
    axis.set_yticks(
        positions,
        [
            f"{station}\n{location}"
            for station, location in zip(frame["station_label"], frame["location_label"], strict=True)
        ],
    )
    axis.invert_yaxis()
    axis.tick_params(axis="y", labelsize=5.8, length=0, pad=5, colors="#3F3F3F")
    axis.tick_params(axis="x", labelsize=7, length=0, colors="#4D4D4D")
    axis.xaxis.set_major_locator(MaxNLocator(nbins=5, min_n_ticks=4))
    axis.xaxis.set_major_formatter(FuncFormatter(format_colombian_thousands))
    axis.set_xlabel("Galones despachados en el periodo", fontsize=8, color="#303030")
    axis.grid(axis="x", color="#D7D7D7", linewidth=0.55, alpha=0.85, zorder=0)
    axis.set_axisbelow(True)

    maximum = float(volumes.max())
    axis.set_xlim(0, maximum * 1.30)
    offset = maximum * 0.012
    for position, volume, annotation in zip(positions, volumes, frame["annotation"], strict=True):
        axis.text(
            volume + offset,
            float(position),
            annotation,
            va="center",
            ha="left",
            fontsize=5.7,
            color="#4B4B4B",
        )
    for spine in axis.spines.values():
        spine.set_visible(True)
        spine.set_color("#333333")
        spine.set_linewidth(0.6)
