from __future__ import annotations

import calendar
import re
from dataclasses import dataclass, field
from datetime import date, datetime, timezone
from enum import StrEnum
from pathlib import Path
from typing import Any


class PeriodKind(StrEnum):
    QUARTER = "quarter"
    SEMESTER = "semester"
    ANNUAL = "annual"


_PERIOD_PATTERN = re.compile(r"^(?P<year>20\d{2})-(?:(?P<kind>[QS])(?P<number>[1-4])|(?P<annual>A))$", re.IGNORECASE)
_ORDINALS = {1: "primer", 2: "segundo", 3: "tercer", 4: "cuarto"}


@dataclass(frozen=True, slots=True)
class PeriodSpec:
    year: int
    kind: PeriodKind
    number: int | None

    def __post_init__(self) -> None:
        if not 2000 <= self.year <= 2100:
            raise ValueError("El año debe estar entre 2000 y 2100.")
        if self.kind is PeriodKind.ANNUAL:
            if self.number is not None:
                raise ValueError("Un periodo anual no admite número.")
            return
        if self.number is None:
            raise ValueError("Los periodos trimestrales y semestrales requieren número.")
        maximum = 4 if self.kind is PeriodKind.QUARTER else 2
        if not 1 <= self.number <= maximum:
            noun = "trimestre" if self.kind is PeriodKind.QUARTER else "semestre"
            raise ValueError(f"El {noun} debe estar entre 1 y {maximum}.")

    @classmethod
    def parse(cls, value: str) -> PeriodSpec:
        match = _PERIOD_PATTERN.fullmatch(value.strip())
        if match is None:
            raise ValueError("Periodo inválido. Use YYYY-Q1..Q4, YYYY-S1..S2 o YYYY-A.")
        if match.group("annual"):
            return cls(year=int(match.group("year")), kind=PeriodKind.ANNUAL, number=None)
        kind_letter = match.group("kind").upper()
        kind = PeriodKind.QUARTER if kind_letter == "Q" else PeriodKind.SEMESTER
        number = int(match.group("number"))
        if kind is PeriodKind.SEMESTER and number > 2:
            raise ValueError("Un semestre debe ser S1 o S2.")
        return cls(year=int(match.group("year")), kind=kind, number=number)

    @classmethod
    def previous_completed(cls, today: date | None = None) -> PeriodSpec:
        current = today or date.today()
        current_quarter = ((current.month - 1) // 3) + 1
        if current_quarter == 1:
            return cls(current.year - 1, PeriodKind.QUARTER, 4)
        return cls(current.year, PeriodKind.QUARTER, current_quarter - 1)

    @property
    def code(self) -> str:
        if self.kind is PeriodKind.ANNUAL:
            return f"{self.year}-A"
        letter = "Q" if self.kind is PeriodKind.QUARTER else "S"
        return f"{self.year}-{letter}{self.number}"

    @property
    def months(self) -> tuple[int, ...]:
        if self.kind is PeriodKind.ANNUAL:
            return tuple(range(1, 13))
        assert self.number is not None
        size = 3 if self.kind is PeriodKind.QUARTER else 6
        start = (self.number - 1) * size + 1
        return tuple(range(start, start + size))

    @property
    def comparison_year(self) -> int:
        return self.year - 1

    @property
    def noun(self) -> str:
        if self.kind is PeriodKind.ANNUAL:
            return "año"
        return "trimestre" if self.kind is PeriodKind.QUARTER else "semestre"

    @property
    def label(self) -> str:
        if self.kind is PeriodKind.ANNUAL:
            return f"año {self.year}"
        assert self.number is not None
        return f"{_ORDINALS[self.number]} {self.noun} de {self.year}"

    @property
    def title_label(self) -> str:
        return self.label.capitalize()

    @property
    def short_label(self) -> str:
        if self.kind is PeriodKind.ANNUAL:
            return "Año"
        assert self.number is not None
        prefix = "Trim." if self.kind is PeriodKind.QUARTER else "Sem."
        return f"{prefix} {self.number}"

    @property
    def cutoff_date(self) -> date:
        month = self.months[-1]
        return date(self.year, month, calendar.monthrange(self.year, month)[1])

    def history_years(self, count: int) -> tuple[int, ...]:
        return tuple(range(self.year - count + 1, self.year + 1))

    def olap_arguments(self) -> list[str]:
        if self.kind is PeriodKind.ANNUAL:
            return ["--year", str(self.year), "--annual"]
        assert self.number is not None
        option = "--quarter" if self.kind is PeriodKind.QUARTER else "--semester"
        return ["--year", str(self.year), option, str(self.number)]

    def olap_arguments_for_year(self, year: int) -> list[str]:
        args = self.olap_arguments()
        args[1] = str(year)
        return args


@dataclass(frozen=True, slots=True)
class RunPaths:
    root: Path
    period: PeriodSpec

    @property
    def data_dir(self) -> Path:
        return self.root / "data" / self.period.code

    @property
    def raw_dir(self) -> Path:
        return self.data_dir / "raw"

    @property
    def processed_dir(self) -> Path:
        return self.data_dir / "processed"

    @property
    def build_dir(self) -> Path:
        return self.root / "build" / self.period.code

    @property
    def pages_dir(self) -> Path:
        return self.build_dir / "pages"

    @property
    def dist_dir(self) -> Path:
        return self.root / "dist" / self.period.code

    @property
    def pdf_path(self) -> Path:
        return self.dist_dir / f"Informe_SU_{self.period.code.replace('-', '_')}.pdf"

    @property
    def metrics_path(self) -> Path:
        return self.dist_dir / "metrics.json"

    @property
    def manifest_path(self) -> Path:
        return self.dist_dir / "manifest.json"

    def ensure(self) -> None:
        for path in (self.raw_dir, self.processed_dir, self.pages_dir, self.dist_dir):
            path.mkdir(parents=True, exist_ok=True)


class PipelineStage(StrEnum):
    PREFLIGHT = "preflight"
    OPEN_DATA = "datos_abiertos"
    OLAP_WHOLESALERS = "sicom_mayoristas"
    OLAP_EDS = "sicom_eds"
    NORMALIZE = "normalizacion"
    VALIDATE = "validacion"
    DRAW = "graficos"
    COMPILE = "pdf"
    COMPLETE = "completado"


@dataclass(frozen=True, slots=True)
class PipelineEvent:
    stage: PipelineStage
    message: str
    progress: float | None = None
    level: str = "info"
    timestamp: datetime = field(default_factory=lambda: datetime.now(timezone.utc))


@dataclass(slots=True)
class PipelineResult:
    period: PeriodSpec
    paths: RunPaths
    warnings: list[str] = field(default_factory=list)
    metadata: dict[str, Any] = field(default_factory=dict)
