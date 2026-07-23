from datetime import date

import pytest

from su_report.models import PeriodKind, PeriodSpec


@pytest.mark.parametrize(
    ("code", "kind", "months"),
    [
        ("2026-Q1", PeriodKind.QUARTER, (1, 2, 3)),
        ("2026-Q4", PeriodKind.QUARTER, (10, 11, 12)),
        ("2026-S1", PeriodKind.SEMESTER, (1, 2, 3, 4, 5, 6)),
        ("2026-S2", PeriodKind.SEMESTER, (7, 8, 9, 10, 11, 12)),
        ("2025-A", PeriodKind.ANNUAL, tuple(range(1, 13))),
    ],
)
def test_parse_period(code: str, kind: PeriodKind, months: tuple[int, ...]) -> None:
    period = PeriodSpec.parse(code)

    assert period.kind is kind
    assert period.months == months
    assert period.code == code


def test_previous_completed_quarter_crosses_year() -> None:
    assert PeriodSpec.previous_completed(date(2026, 2, 10)).code == "2025-Q4"


def test_annual_labels_paths_and_olap_arguments() -> None:
    period = PeriodSpec.parse("2025-A")

    assert period.number is None
    assert period.label == "año 2025"
    assert period.title_label == "Año 2025"
    assert period.short_label == "Año"
    assert period.olap_arguments() == ["--year", "2025", "--annual"]


@pytest.mark.parametrize("value", ["2026-Q5", "2026-S3", "2026-A1", "26-Q1", "2026-T2"])
def test_rejects_invalid_period(value: str) -> None:
    with pytest.raises(ValueError):
        PeriodSpec.parse(value)
