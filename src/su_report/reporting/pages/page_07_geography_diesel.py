from __future__ import annotations

from su_report.reporting.context import PageResult, ReportContext
from su_report.reporting.data import ReportData
from su_report.reporting.pages.page_06_geography_current import render_product


def render(context: ReportContext, data: ReportData) -> PageResult:
    return render_product(context, data, "BIODIESEL CON MEZCLA")
