from __future__ import annotations

from dataclasses import replace

import matplotlib.pyplot as plt
import pytest

from su_report.reporting.builder import PAGE_SPECS
from su_report.reporting.context import ReportContext
from su_report.reporting.data import ReportData
from su_report.reporting.pages import page_01_cover, page_13_conclusions


@pytest.mark.parametrize("spec", PAGE_SPECS, ids=lambda spec: f"page-{spec.number:02d}-{spec.slug}")
def test_each_page_renders_in_memory_without_writing(
    spec,
    report_context: ReportContext,
    report_data: ReportData,
) -> None:
    before = list(report_context.pages_dir.glob("*")) if report_context.pages_dir.exists() else []
    result = spec.renderer(report_context, report_data)
    try:
        assert isinstance(result.figure, plt.Figure)
        assert result.figure.get_size_inches().tolist() == pytest.approx([13.833, 8.0])
        after = list(report_context.pages_dir.glob("*")) if report_context.pages_dir.exists() else []
        assert after == before
    finally:
        plt.close(result.figure)


def test_cover_keeps_native_text_and_hybrid_art(
    report_context: ReportContext,
    report_data: ReportData,
) -> None:
    result = page_01_cover.render(report_context, report_data)
    try:
        texts = {text.get_gid(): text for text in result.figure.texts if text.get_gid()}
        assert texts["cover-title"].get_color() == page_01_cover.COVER_COLORS["navy"]
        assert texts["cover-period"].get_text().endswith("\n2026")
        assert any(image.get_gid() == "cover-art" for axis in result.figure.axes for image in axis.images)
    finally:
        plt.close(result.figure)


def test_partial_cover_marks_draft(
    report_context: ReportContext,
    report_data: ReportData,
) -> None:
    partial = replace(report_context, partial=True)
    result = page_01_cover.render(partial, report_data)
    try:
        texts = {text.get_gid(): text for text in result.figure.texts if text.get_gid()}
        assert texts["cover-partial"].get_color() == page_01_cover.COVER_COLORS["red"]
    finally:
        plt.close(result.figure)


def test_conclusions_return_metrics_and_keep_text_weight(
    report_context: ReportContext,
    report_data: ReportData,
) -> None:
    result = page_13_conclusions.render(report_context, report_data)
    try:
        assert set(result.metrics) == {"current_variation_pct", "diesel_variation_pct"}
        texts = {text.get_gid(): text for text in result.figure.texts if text.get_gid()}
        for number in range(1, 6):
            assert texts[f"conclusion-number-{number}"].get_weight() == "bold"
            assert texts[f"conclusion-body-{number}"].get_weight() == "normal"
    finally:
        plt.close(result.figure)
