from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import matplotlib.pyplot as plt
import yaml  # type: ignore[import-untyped]


@dataclass(frozen=True, slots=True)
class ReportContext:
    """Configuración inmutable necesaria para componer una ejecución."""

    config: dict[str, Any]
    report: dict[str, Any]
    project_dir: Path
    input_dir: Path
    brand_logo: Path
    geography_path: Path
    cover_art: Path
    pages_dir: Path
    metrics_path: Path
    historical_validation_path: Path
    font_dirs: tuple[Path, ...]
    year: int
    previous_year: int
    period_kind: str
    period_number: int | None
    period_label: str
    period_title: str
    period_noun: str
    period_short: str
    page_count: int
    months: tuple[int, ...]
    history_start_year: int
    historical_start_period: str
    historical_end_period: str
    partial: bool

    @classmethod
    def from_file(cls, config_path: Path) -> ReportContext:
        config = yaml.safe_load(config_path.read_text(encoding="utf-8"))
        report = config["report"]
        sources = config["sources"]
        outputs = config["outputs"]
        raw_period_number = report.get("period_number")
        return cls(
            config=config,
            report=report,
            project_dir=Path(str(config["project_dir"])).resolve(),
            input_dir=Path(str(sources["input_dir"])).resolve(),
            brand_logo=Path(str(sources["brand_logo"])).resolve(),
            geography_path=Path(str(sources["geography"])).resolve(),
            cover_art=Path(str(sources["cover_art"])).resolve(),
            pages_dir=Path(str(outputs["pages_dir"])).resolve(),
            metrics_path=Path(str(outputs["metrics"])).resolve(),
            historical_validation_path=Path(str(outputs["historical_validation"])).resolve(),
            font_dirs=tuple(Path(str(path)).resolve() for path in config.get("font_dirs", [])),
            year=int(report["year"]),
            previous_year=int(report["comparison_year"]),
            period_kind=str(report["period_kind"]),
            period_number=int(raw_period_number) if raw_period_number is not None else None,
            period_label=str(report["period_label"]),
            period_title=str(report["period_title"]),
            period_noun=str(report["period_noun"]),
            period_short=str(report["period_short"]),
            page_count=int(report["pages"]),
            months=tuple(int(month) for month in report["months"]),
            history_start_year=int(report["history_start_year"]),
            historical_start_period=str(report["historical_wholesalers_start_period"]),
            historical_end_period=str(report["historical_wholesalers_end_period"]),
            partial=bool(report.get("partial", False)),
        )

    @property
    def title(self) -> str:
        return str(self.report["title"])


@dataclass(slots=True)
class PageResult:
    """Resultado en memoria de una página antes de escribir su SVG."""

    figure: plt.Figure
    metrics: dict[str, float] = field(default_factory=dict)
