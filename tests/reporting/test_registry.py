from __future__ import annotations

import pytest

from su_report.reporting import builder


def test_registry_is_consecutive_and_matches_configured_page_count() -> None:
    builder.validate_page_registry(14)
    assert [spec.number for spec in builder.PAGE_SPECS] == list(range(1, 15))
    assert len({spec.slug for spec in builder.PAGE_SPECS}) == 14


def test_registry_rejects_mismatched_page_count() -> None:
    with pytest.raises(ValueError, match="report.pages"):
        builder.validate_page_registry(13)
