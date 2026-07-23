from __future__ import annotations

from pathlib import Path

import pytest
from typer.testing import CliRunner

from su_report import cli
from su_report.models import PeriodSpec, RunPaths
from su_report.pipeline import PipelineError, ReportPipeline
from su_report.settings import Settings
from su_report.validation import ValidationResult


@pytest.mark.parametrize("resolution", ["1k", "2k", "4k"])
def test_runtime_config_selects_requested_cover_asset(resolution: str) -> None:
    root = Path(__file__).resolve().parents[1]
    settings = Settings.load(root)
    pipeline = ReportPipeline(settings=settings)
    period = PeriodSpec.parse("2026-Q2")
    paths = RunPaths(root, period)
    validation = ValidationResult(False, (), {}, {})

    runtime = pipeline._runtime_config(period, paths, validation, resolution)

    assert runtime["report"]["cover_resolution"] == resolution
    assert runtime["sources"]["cover_art"].endswith(f"cover-illustration-ai-v1-{resolution}.png")


def test_runtime_config_rejects_unknown_cover_resolution() -> None:
    root = Path(__file__).resolve().parents[1]
    pipeline = ReportPipeline(settings=Settings.load(root))
    period = PeriodSpec.parse("2026-Q2")
    paths = RunPaths(root, period)
    validation = ValidationResult(False, (), {}, {})

    with pytest.raises(PipelineError, match="Resolución de portada inválida"):
        pipeline._runtime_config(period, paths, validation, "8k")


def test_runtime_config_exposes_annual_labels_and_page_count() -> None:
    root = Path(__file__).resolve().parents[1]
    pipeline = ReportPipeline(settings=Settings.load(root))
    period = PeriodSpec.parse("2025-A")
    runtime = pipeline._runtime_config(
        period,
        RunPaths(root, period),
        ValidationResult(False, (), {}, {}),
    )

    assert runtime["report"]["period_number"] is None
    assert runtime["report"]["period_noun"] == "año"
    assert runtime["report"]["period_short"] == "Año"
    assert runtime["report"]["months"] == list(range(1, 13))
    assert runtime["report"]["pages"] == 14


def test_render_cli_defaults_to_2k(monkeypatch: pytest.MonkeyPatch) -> None:
    calls: list[tuple[str, str, str]] = []
    monkeypatch.setattr(
        cli,
        "_execute_pipeline",
        lambda operation, period, cover_resolution="2k": calls.append(
            (operation, period, cover_resolution)
        ),
    )

    result = CliRunner().invoke(cli.app, ["render", "--period", "2026-Q2"])

    assert result.exit_code == 0
    assert calls == [("render", "2026-Q2", "2k")]


def test_generate_cli_accepts_explicit_cover_resolution(monkeypatch: pytest.MonkeyPatch) -> None:
    calls: list[tuple[str, str, str]] = []
    monkeypatch.setattr(
        cli,
        "_execute_pipeline",
        lambda operation, period, cover_resolution="2k": calls.append(
            (operation, period, cover_resolution)
        ),
    )

    result = CliRunner().invoke(
        cli.app,
        ["generate", "--period", "2026-Q2", "--cover-resolution", "1k"],
    )

    assert result.exit_code == 0
    assert calls == [("generate", "2026-Q2", "1k")]


def test_cli_rejects_unknown_cover_resolution() -> None:
    result = CliRunner().invoke(
        cli.app,
        ["render", "--period", "2026-Q2", "--cover-resolution", "8k"],
    )

    assert result.exit_code == 2
    assert "Invalid value" in result.output
