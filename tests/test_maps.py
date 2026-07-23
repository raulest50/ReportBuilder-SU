from __future__ import annotations

import pytest

from su_report.maps import (
    MUNICIPAL_BOUNDARY_COLOR,
    MUNICIPAL_BOUNDARY_WIDTH,
    _compact_period_label,
)


@pytest.mark.parametrize(
    ("period_short", "expected"),
    [
        ("Trim. 2", "Q2"),
        ("Sem. 1", "S1"),
        ("Periodo especial", "Periodo especial"),
    ],
)
def test_compact_period_label(period_short: str, expected: str) -> None:
    assert _compact_period_label(period_short) == expected


def test_municipal_boundaries_use_thin_white_stroke() -> None:
    assert MUNICIPAL_BOUNDARY_WIDTH == 0.015
    assert MUNICIPAL_BOUNDARY_COLOR == "#ffffff"
