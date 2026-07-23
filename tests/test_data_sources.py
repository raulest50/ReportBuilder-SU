from su_report.data_sources import _quoted_months


def test_quoted_months_matches_socrata_text_format() -> None:
    assert _quoted_months((4, 5, 6)) == "'04', '05', '06'"
    assert _quoted_months((10, 11, 12)) == "'10', '11', '12'"
