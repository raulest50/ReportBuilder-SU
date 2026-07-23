from __future__ import annotations

import json
import platform
import shutil
import subprocess
from collections.abc import Callable
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

import fitz
import pandas as pd
import yaml

from su_report import __version__
from su_report.data_sources import SocrataExtractor, SourceError
from su_report.models import PeriodSpec, PipelineEvent, PipelineResult, PipelineStage, RunPaths
from su_report.olap import OlapGateway
from su_report.reporting.builder import build_report
from su_report.security import sanitize, sha256_file, write_json
from su_report.settings import Settings
from su_report.validation import ValidationResult, validate_processed

EventHandler = Callable[[PipelineEvent], None]
COVER_RESOLUTIONS = ("1k", "2k", "4k")


class PipelineError(RuntimeError):
    """Fallo seguro y presentable del pipeline."""


def _tool_version(command: str | Path) -> str:
    executable = str(command) if isinstance(command, Path) and command.is_file() else shutil.which(str(command))
    if executable is None:
        return "no encontrado"
    completed = subprocess.run(
        [executable, "--version"],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        shell=False,
        check=False,
    )
    output = sanitize(completed.stdout or completed.stderr).strip().splitlines()
    return output[0] if output else f"código {completed.returncode}"


def _tool_versions(settings: Settings) -> dict[str, str]:
    return {
        "reportbuilder_su": __version__,
        "python": platform.python_version(),
        "uv": _tool_version("uv"),
        "quarto": _tool_version(settings.quarto_executable or "quarto"),
        "dotnet": _tool_version("dotnet"),
    }


def _write_frame(frame: pd.DataFrame, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    frame.to_csv(temporary, index=False)
    temporary.replace(path)


def _copy(source: Path, target: Path) -> None:
    if not source.exists():
        raise PipelineError(f"El motor OLAP no generó {source.name}.")
    target.parent.mkdir(parents=True, exist_ok=True)
    temporary = target.with_suffix(target.suffix + ".tmp")
    shutil.copy2(source, temporary)
    temporary.replace(target)


def _recent_open_data(paths: RunPaths, max_age_seconds: int = 3600) -> tuple[dict[str, Path], int] | None:
    outputs = {
        name: paths.raw_dir / "datos-abiertos" / f"{name}.csv"
        for name in ("national", "departments", "municipalities")
    }
    if not all(path.is_file() for path in outputs.values()):
        return None
    oldest_timestamp = min(path.stat().st_mtime for path in outputs.values())
    age_seconds = max(0, int(datetime.now(UTC).timestamp() - oldest_timestamp))
    if age_seconds > max_age_seconds:
        return None
    return outputs, age_seconds


class ReportPipeline:
    def __init__(self, settings: Settings | None = None, on_event: EventHandler | None = None) -> None:
        self.settings = settings or Settings.load()
        self.on_event = on_event
        self.olap = OlapGateway(self.settings)
        self._progress_start = 0.0
        self._progress_span = 1.0

    def _emit(
        self,
        stage: PipelineStage,
        message: str,
        progress: float | None = None,
        level: str = "info",
    ) -> None:
        if self.on_event is not None:
            scaled_progress = (
                self._progress_start + self._progress_span * progress
                if progress is not None
                else None
            )
            self.on_event(PipelineEvent(stage, sanitize(message), scaled_progress, level))

    def cancel(self) -> bool:
        return self.olap.cancel()

    def _preflight_fetch(self) -> None:
        if shutil.which("dotnet") is None:
            raise PipelineError("No se encontró .NET SDK en PATH. Ejecute el setup inicial.")
        if not self.settings.olap_project_path.is_file():
            raise PipelineError(f"No existe el motor OLAP incorporado: {self.settings.olap_project_path}")

    def _preflight_render(self) -> None:
        if self.settings.quarto_executable is None:
            raise PipelineError("No se encontró Quarto en PATH ni en las rutas estándar. Ejecute el setup inicial.")
        if self.settings.active_font_dir is None:
            raise PipelineError("No se encontraron segoeui.ttf y segoeuib.ttf; consulte docs/SETUP.md.")
        required_assets = (
            self.settings.logo_path,
            self.settings.geography_path,
            self.settings.quarto_template_path,
        )
        missing_assets = [path for path in required_assets if not path.is_file()]
        if missing_assets:
            raise PipelineError("Faltan activos del informe: " + ", ".join(map(str, missing_assets)))

    def fetch(self, period: PeriodSpec) -> PipelineResult:
        self._preflight_fetch()
        paths = RunPaths(self.settings.root, period)
        paths.ensure()
        self._emit(PipelineStage.PREFLIGHT, f"Preparando extracción de {period.code}", 0.02)

        missing_secrets = [key for key, present in self.settings.secret_status.items() if not present]
        if missing_secrets:
            raise PipelineError(f"Faltan variables en .env: {', '.join(missing_secrets)}")

        self._emit(PipelineStage.OPEN_DATA, "Consultando Datos Abiertos", 0.08)
        try:
            open_outputs = SocrataExtractor(self.settings).fetch(period, paths, self.settings.history_years)
        except SourceError:
            cached = _recent_open_data(paths)
            if cached is None:
                raise
            open_outputs, age_seconds = cached
            self._emit(
                PipelineStage.OPEN_DATA,
                f"Datos Abiertos no disponible; reanudando con la extracción de hace {max(1, age_seconds // 60)} min.",
                level="warning",
            )
        self._emit(PipelineStage.OPEN_DATA, "Datos Abiertos descargados", 0.20)

        mayoristas_files: list[Path] = []
        history_years = period.history_years(self.settings.history_years)
        for index, year in enumerate(history_years, start=1):
            output_dir = paths.raw_dir / "sicom" / "mayoristas" / str(year)
            self._emit(
                PipelineStage.OLAP_WHOLESALERS,
                f"Consultando mayoristas {year}",
                0.20 + 0.30 * index / len(history_years),
            )
            self.olap.report_mayoristas(
                period.olap_arguments_for_year(year),
                output_dir,
                on_output=lambda line: self._emit(PipelineStage.OLAP_WHOLESALERS, line),
            )
            mayoristas_files.append(output_dir / "mayoristas-detalle.csv")

        try:
            historical_start = pd.Period(self.settings.historical_wholesalers_start_period, freq="M")
        except ValueError as error:
            raise PipelineError("historical_wholesalers_start_period debe usar el formato YYYY-MM.") from error
        historical_end = pd.Period(year=period.year, month=period.months[-1], freq="M")
        if historical_end < historical_start:
            raise PipelineError("El periodo del informe es anterior al inicio del histórico de mayoristas.")
        historical_dir = paths.raw_dir / "sicom" / "mayoristas-historico"
        self._emit(
            PipelineStage.OLAP_WHOLESALERS,
            f"Consultando histórico de mayoristas {historical_start} a {historical_end}",
            0.53,
        )
        self.olap.report_mayoristas_historico(
            start_year=historical_start.year,
            start_month=historical_start.month,
            end_year=historical_end.year,
            end_month=historical_end.month,
            output_dir=historical_dir,
            on_output=lambda line: self._emit(PipelineStage.OLAP_WHOLESALERS, line),
        )

        self._emit(PipelineStage.OLAP_EDS, f"Consultando EDS {period.code}", 0.60)
        eds_dir = paths.raw_dir / "sicom" / "eds"
        self.olap.report_eds_municipios(
            period.olap_arguments(),
            eds_dir,
            on_output=lambda line: self._emit(PipelineStage.OLAP_EDS, line),
        )

        eds_top_dir = paths.raw_dir / "sicom" / "eds-top"
        self._emit(PipelineStage.OLAP_EDS, f"Consultando Top 20 EDS {period.code}", 0.67)
        self.olap.report_eds_top(
            period.olap_arguments(),
            eds_top_dir,
            on_output=lambda line: self._emit(PipelineStage.OLAP_EDS, line),
        )
        self._emit(PipelineStage.OLAP_EDS, "Extracción SICOM completada", 0.72)

        self._emit(PipelineStage.NORMALIZE, "Normalizando contratos de datos", 0.76)
        self._normalize(paths, open_outputs, mayoristas_files, historical_dir, eds_dir, eds_top_dir)
        validation = validate_processed(
            period,
            paths.processed_dir,
            self.settings.historical_wholesalers_start_period,
        )
        for warning in validation.warnings:
            self._emit(PipelineStage.VALIDATE, warning, level="warning")

        fetch_manifest = self._fetch_manifest(period, paths, validation)
        write_json(paths.data_dir / "fetch-manifest.json", fetch_manifest)
        self._emit(PipelineStage.VALIDATE, "Datos listos para renderizar", 1.0)
        return PipelineResult(period, paths, list(validation.warnings), fetch_manifest)

    def _normalize(
        self,
        paths: RunPaths,
        open_outputs: dict[str, Path],
        mayoristas_files: list[Path],
        historical_dir: Path,
        eds_dir: Path,
        eds_top_dir: Path,
    ) -> None:
        _copy(open_outputs["national"], paths.processed_dir / "national-monthly.csv")
        _copy(open_outputs["departments"], paths.processed_dir / "geography-departments.csv")
        _copy(open_outputs["municipalities"], paths.processed_dir / "geography-municipalities.csv")

        frames = [pd.read_csv(path) for path in mayoristas_files]
        combined = pd.concat(frames, ignore_index=True)
        volume_column = next(
            (candidate for candidate in ("VOLUMEN ACEPTADO", "VOLUMEN") if candidate in combined.columns),
            None,
        )
        if volume_column is None:
            raise PipelineError("Mayoristas no contiene la columna VOLUMEN ACEPTADO ni VOLUMEN.")
        aliases = {
            "NOMBRE": "provider",
            "MES DESPACHO": "month",
            "AÑO DESPACHO": "year",
            "PRODUCTO": "product",
            "SUBTIPO AGENTE": "buyer_subtype",
            volume_column: "accepted_volume",
        }
        missing = set(aliases) - set(combined.columns)
        if missing:
            raise PipelineError(f"Mayoristas no contiene columnas: {', '.join(sorted(missing))}")
        normalized = combined.rename(columns=aliases)[list(aliases.values())]
        normalized["month"] = pd.to_numeric(normalized["month"], errors="raise")
        normalized["year"] = pd.to_numeric(normalized["year"], errors="raise")
        normalized["accepted_volume"] = pd.to_numeric(normalized["accepted_volume"], errors="raise")
        _write_frame(normalized, paths.processed_dir / "mayoristas.csv")

        _copy(
            historical_dir / "mayoristas-historico-mensual.csv",
            paths.processed_dir / "mayoristas-historico-mensual.csv",
        )
        _copy(
            historical_dir / "mayoristas-historico-manifest.json",
            paths.processed_dir / "mayoristas-historico-manifest.json",
        )

        _copy(eds_dir / "eds-municipios-detalle.csv", paths.processed_dir / "eds-municipios.csv")
        _copy(eds_dir / "eds-municipios-eds-activas.csv", paths.processed_dir / "eds-activas.csv")
        _copy(eds_top_dir / "eds-top-volumen.csv", paths.processed_dir / "eds-top-volumen.csv")
        _copy(eds_top_dir / "eds-top-manifest.json", paths.processed_dir / "eds-top-manifest.json")

    def render(self, period: PeriodSpec, *, cover_resolution: str = "2k") -> PipelineResult:
        self._preflight_render()
        paths = RunPaths(self.settings.root, period)
        paths.ensure()
        self._emit(PipelineStage.VALIDATE, "Validando datos locales", 0.05)
        validation = validate_processed(
            period,
            paths.processed_dir,
            self.settings.historical_wholesalers_start_period,
        )
        for warning in validation.warnings:
            self._emit(PipelineStage.VALIDATE, warning, level="warning")

        runtime_config = self._runtime_config(period, paths, validation, cover_resolution)
        runtime_config_path = paths.build_dir / "report-runtime.yml"
        runtime_config_path.write_text(
            yaml.safe_dump(runtime_config, allow_unicode=True, sort_keys=False),
            encoding="utf-8",
        )

        self._emit(PipelineStage.DRAW, f"Generando {self.settings.page_count} páginas vectoriales", 0.15)
        metrics = build_report(runtime_config_path)
        self._emit(PipelineStage.DRAW, "Páginas vectoriales completadas", 0.68)

        metadata_path = paths.build_dir / "typst-metadata.json"
        write_json(
            metadata_path,
            {
                "title": self.settings.config["report"]["title"] + f" — {period.title_label}",
                "author": self.settings.config["report"]["area"],
                "pages": self.settings.page_count,
                "pages_dir": str(paths.pages_dir.relative_to(self.settings.root)).replace("\\", "/"),
            },
        )
        self._emit(PipelineStage.COMPILE, "Compilando PDF/A-2b con Quarto/Typst", 0.72)
        self._compile_pdf(paths, metadata_path)
        pdf_validation = self._validate_pdf(paths.pdf_path)

        manifest = self._final_manifest(period, paths, validation, metrics, pdf_validation)
        write_json(paths.manifest_path, manifest)
        self._emit(PipelineStage.COMPLETE, f"Informe generado: {paths.pdf_path}", 1.0)
        return PipelineResult(period, paths, list(validation.warnings), manifest)

    def generate(self, period: PeriodSpec, *, cover_resolution: str = "2k") -> PipelineResult:
        try:
            self._preflight_render()
            self._progress_start, self._progress_span = 0.0, 0.62
            self.fetch(period)
            self._progress_start, self._progress_span = 0.62, 0.38
            return self.render(period, cover_resolution=cover_resolution)
        finally:
            self._progress_start, self._progress_span = 0.0, 1.0

    def _runtime_config(
        self,
        period: PeriodSpec,
        paths: RunPaths,
        validation: ValidationResult,
        cover_resolution: str = "2k",
    ) -> dict[str, Any]:
        normalized_resolution = cover_resolution.lower()
        if normalized_resolution not in COVER_RESOLUTIONS:
            options = ", ".join(COVER_RESOLUTIONS)
            raise PipelineError(f"Resolución de portada inválida: {cover_resolution}. Opciones: {options}.")
        cover_art = (
            self.settings.root
            / "assets"
            / "cover"
            / f"cover-illustration-ai-v1-{normalized_resolution}.png"
        )
        if not cover_art.is_file():
            raise PipelineError(f"No existe el activo de portada {normalized_resolution}: {cover_art}")
        font_dirs = [str(path) for path in self.settings.font_dirs if path.exists()]
        return {
            "project_dir": str(self.settings.root),
            "report": {
                "year": period.year,
                "comparison_year": period.comparison_year,
                "period_kind": period.kind.value,
                "period_number": period.number,
                "period_label": period.label,
                "period_title": period.title_label,
                "period_noun": period.noun,
                "period_short": period.short_label,
                "pages": self.settings.page_count,
                "months": list(period.months),
                "history_start_year": period.history_years(self.settings.history_years)[0],
                "historical_wholesalers_start_period": self.settings.historical_wholesalers_start_period,
                "historical_wholesalers_end_period": f"{period.year:04d}-{period.months[-1]:02d}",
                "cutoff_date": period.cutoff_date.isoformat(),
                "partial": validation.partial,
                "title": self.settings.config["report"]["title"],
                "cover_resolution": normalized_resolution,
            },
            "sources": {
                "input_dir": str(paths.processed_dir),
                "brand_logo": str(self.settings.logo_path),
                "geography": str(self.settings.geography_path),
                "cover_art": str(cover_art),
            },
            "outputs": {
                "pages_dir": str(paths.pages_dir),
                "metrics": str(paths.metrics_path),
                "historical_validation": str(paths.build_dir / "mayoristas-historico-validation.json"),
            },
            "font_dirs": font_dirs,
        }

    def _compile_pdf(self, paths: RunPaths, metadata_path: Path) -> None:
        quarto_executable = self.settings.quarto_executable
        if quarto_executable is None:
            raise PipelineError("No se encontró Quarto para compilar el PDF.")
        command = [
            str(quarto_executable),
            "typst",
            "compile",
            str(self.settings.quarto_template_path),
            str(paths.pdf_path),
            "--root",
            str(self.settings.root),
            "--input",
            f"metadata={metadata_path.relative_to(self.settings.root).as_posix()}",
            "--pdf-standard",
            "a-2b",
        ]
        font_dir = self.settings.active_font_dir
        if font_dir is not None:
            command.extend(["--font-path", str(font_dir)])
        completed = subprocess.run(
            command,
            cwd=self.settings.root,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            shell=False,
            check=False,
        )
        if completed.returncode != 0:
            details = sanitize((completed.stderr or completed.stdout)[-4000:])
            raise PipelineError(f"Quarto/Typst no pudo generar el PDF.\n{details}")

    def _validate_pdf(self, path: Path) -> dict[str, Any]:
        if not path.exists():
            raise PipelineError("Quarto terminó sin crear el PDF esperado.")
        with fitz.open(path) as document:
            if document.page_count != self.settings.page_count:
                raise PipelineError(
                    f"El PDF contiene {document.page_count} páginas; "
                    f"se esperaban {self.settings.page_count}."
                )
            sizes = [(round(page.rect.width, 1), round(page.rect.height, 1)) for page in document]
        expected = (996.0, 576.0)
        if any(size != expected for size in sizes):
            raise PipelineError(f"El PDF tiene dimensiones inesperadas: {sorted(set(sizes))}")
        return {
            "pages": self.settings.page_count,
            "page_size_points": list(expected),
            "bytes": path.stat().st_size,
        }

    def _fetch_manifest(
        self,
        period: PeriodSpec,
        paths: RunPaths,
        validation: ValidationResult,
    ) -> dict[str, Any]:
        files = sorted(
            path
            for path in paths.processed_dir.iterdir()
            if path.is_file() and path.suffix.lower() in {".csv", ".json"}
        )
        return {
            "period": period.code,
            "extracted_at_utc": datetime.now(UTC).isoformat(),
            "partial": validation.partial,
            "warnings": list(validation.warnings),
            "months_by_source": {key: list(value) for key, value in validation.months_by_source.items()},
            "measure_contract": {
                "national_and_geographic": "volumen_despachado (Datos Abiertos)",
                "current_wholesalers_and_eds": "VOLUMEN ACEPTADO (SICOM OLAP)",
                "historical_wholesalers_page_5": (
                    "VOLUMEN DESPACHADO; EDS automotrices y fluviales; tres combustibles (SICOM OLAP)"
                ),
                "top_eds_page_12": (
                    "VOLUMEN DESPACHADO; EDS automotrices; tres combustibles (SICOM OLAP)"
                ),
            },
            "historical_market_share": validation.historical_market_share,
            "tool_versions": _tool_versions(self.settings),
            "files": {path.name: sha256_file(path) for path in files},
        }

    def _final_manifest(
        self,
        period: PeriodSpec,
        paths: RunPaths,
        validation: ValidationResult,
        metrics: dict[str, object],
        pdf_validation: dict[str, Any],
    ) -> dict[str, Any]:
        fetch_manifest_path = paths.data_dir / "fetch-manifest.json"
        current_fetch_manifest = self._fetch_manifest(period, paths, validation)
        if fetch_manifest_path.exists():
            stored_fetch_manifest = json.loads(fetch_manifest_path.read_text(encoding="utf-8"))
            fetch_manifest = {**stored_fetch_manifest, **current_fetch_manifest}
            fetch_manifest["extracted_at_utc"] = stored_fetch_manifest.get(
                "extracted_at_utc",
                current_fetch_manifest["extracted_at_utc"],
            )
        else:
            fetch_manifest = current_fetch_manifest
        historical_validation_path = paths.build_dir / "mayoristas-historico-validation.json"
        if not historical_validation_path.is_file():
            raise PipelineError(f"Falta la validación histórica renderizada: {historical_validation_path}")
        return {
            **fetch_manifest,
            "rendered_at_utc": datetime.now(UTC).isoformat(),
            "metrics": metrics,
            "historical_page_5_validation": {
                "path": str(historical_validation_path),
                "sha256": sha256_file(historical_validation_path),
            },
            "pdf": {**pdf_validation, "sha256": sha256_file(paths.pdf_path), "path": str(paths.pdf_path)},
        }
