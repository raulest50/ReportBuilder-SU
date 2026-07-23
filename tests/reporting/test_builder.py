from __future__ import annotations

import json
from dataclasses import replace
from pathlib import Path

from su_report.reporting import builder
from su_report.reporting.context import ReportContext
from su_report.reporting.data import ReportData


def test_builder_writes_registered_pages_metrics_and_validation(
    monkeypatch,
    tmp_path: Path,
    report_context: ReportContext,
    report_data: ReportData,
) -> None:
    context = replace(
        report_context,
        pages_dir=tmp_path / "pages",
        metrics_path=tmp_path / "metrics.json",
        historical_validation_path=tmp_path / "historical-validation.json",
    )
    monkeypatch.setattr(builder.ReportContext, "from_file", lambda _path: context)
    monkeypatch.setattr(builder, "load_data", lambda _context: report_data)

    metrics = builder.build_report(tmp_path / "unused.yml")

    assert sorted(path.name for path in context.pages_dir.glob("*.svg")) == [
        f"page-{number:02d}.svg" for number in range(1, 14)
    ]
    assert metrics["pages"] == 13
    assert context.metrics_path.is_file()
    assert context.historical_validation_path.is_file()
    stored = json.loads(context.metrics_path.read_text(encoding="utf-8"))
    assert stored["national_variation_pct"] == metrics["national_variation_pct"]
