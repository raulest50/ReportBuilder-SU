"""Orquesta los renderers independientes y escribe los artefactos del informe."""

from __future__ import annotations

import json
from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path

import matplotlib.pyplot as plt

from su_report.charts.mayoristas_historico import (
    historical_validation_payload,
    write_historical_validation,
)
from su_report.reporting.context import PageResult, ReportContext
from su_report.reporting.data import ReportData, load_data, monthly_product
from su_report.reporting.pages.page_01_cover import render as render_page_01
from su_report.reporting.pages.page_02_national_monthly import render as render_page_02
from su_report.reporting.pages.page_03_historical_comparison import render as render_page_03
from su_report.reporting.pages.page_04_wholesalers import render as render_page_04
from su_report.reporting.pages.page_05_wholesalers_history import render as render_page_05
from su_report.reporting.pages.page_06_geography_current import render as render_page_06
from su_report.reporting.pages.page_07_geography_diesel import render as render_page_07
from su_report.reporting.pages.page_08_municipalities import render as render_page_08
from su_report.reporting.pages.page_09_departments_table import render as render_page_09
from su_report.reporting.pages.page_10_municipalities_table import render as render_page_10
from su_report.reporting.pages.page_11_eds import render as render_page_11
from su_report.reporting.pages.page_12_top_eds import render as render_page_12
from su_report.reporting.pages.page_13_conclusions import render as render_page_14
from su_report.reporting.pages.page_13_zfd import render as render_page_13
from su_report.reporting.style import PRODUCTS, configure_matplotlib

PageRenderer = Callable[[ReportContext, ReportData], PageResult]


@dataclass(frozen=True, slots=True)
class PageSpec:
    number: int
    slug: str
    renderer: PageRenderer


PAGE_SPECS: tuple[PageSpec, ...] = (
    PageSpec(1, "cover", render_page_01),
    PageSpec(2, "national-monthly", render_page_02),
    PageSpec(3, "historical-comparison", render_page_03),
    PageSpec(4, "wholesalers", render_page_04),
    PageSpec(5, "wholesalers-history", render_page_05),
    PageSpec(6, "geography-current", render_page_06),
    PageSpec(7, "geography-diesel", render_page_07),
    PageSpec(8, "municipalities", render_page_08),
    PageSpec(9, "departments-table", render_page_09),
    PageSpec(10, "municipalities-table", render_page_10),
    PageSpec(11, "eds", render_page_11),
    PageSpec(12, "top-eds", render_page_12),
    PageSpec(13, "zfd", render_page_13),
    PageSpec(14, "conclusions", render_page_14),
)


def validate_page_registry(expected_count: int) -> None:
    numbers = [spec.number for spec in PAGE_SPECS]
    expected_numbers = list(range(1, expected_count + 1))
    if numbers != expected_numbers:
        raise ValueError(
            "El registro de páginas debe ser consecutivo y coincidir con report.pages: "
            f"esperado {expected_numbers}, obtenido {numbers}."
        )
    slugs = [spec.slug for spec in PAGE_SPECS]
    if len(slugs) != len(set(slugs)):
        raise ValueError("El registro de páginas contiene identificadores duplicados.")


def _save_page(context: ReportContext, result: PageResult, spec: PageSpec) -> None:
    context.pages_dir.mkdir(parents=True, exist_ok=True)
    result.figure.savefig(
        context.pages_dir / f"page-{spec.number:02d}.svg",
        format="svg",
        facecolor="white",
        edgecolor="none",
        metadata={"Title": f"{context.title} — pagina {spec.number}"},
    )
    plt.close(result.figure)


def build_metrics(
    context: ReportContext,
    data: ReportData,
    page_metrics: dict[str, float],
) -> dict[str, object]:
    report_totals = {
        PRODUCTS[product][0].lower(): float(
            monthly_product(context, data.monthly, product)[context.year].sum()
        )
        for product in PRODUCTS
    }
    return {
        "report": {
            "year": context.year,
            "period_kind": context.period_kind,
            "period_number": context.period_number,
            "period_label": context.period_label,
            "months": list(context.months),
            "comparison_year": context.previous_year,
            "cutoff_date": context.report["cutoff_date"],
            "partial": context.partial,
        },
        "measure_contract": {
            "national_and_geographic": "volumen_despachado (Datos Abiertos)",
            "current_wholesalers_and_eds": "VOLUMEN ACEPTADO (SICOM OLAP)",
            "historical_wholesalers_page_5": (
                "VOLUMEN DESPACHADO; EDS automotrices y fluviales; tres combustibles (SICOM OLAP)"
            ),
            "top_eds_page_12": (
                "VOLUMEN DESPACHADO; EDS automotrices; tres combustibles (SICOM OLAP)"
            ),
            "zfd_page_13": (
                "VOLUMEN DESPACHADO; EDS automotrices; tres combustibles; zonas SI/NO "
                "(SICOM OLAP). EDS activas: fotografía al momento de consulta de SBI-Agentes"
            ),
        },
        "historical_market_share": historical_validation_payload(
            data.mayoristas_historico,
            context.historical_start_period,
            context.historical_end_period,
            outputs={"page_svg": "page-05.svg"},
        ),
        "national_volume_gallons": report_totals,
        "national_variation_pct": page_metrics,
        "zfd": {
            "measure": "VOLUMEN DESPACHADO",
            "buyer_scope": "ESTACION DE SERVICIO AUTOMOTRIZ",
            "active_eds_queried_at_utc": data.zfd.active_eds_queried_at_utc,
            "summary": json.loads(data.zfd.summary.to_json(orient="records")),
            "products": json.loads(data.zfd.products.to_json(orient="records")),
            "top_municipalities": json.loads(
                data.zfd.municipalities.to_json(orient="records")
            ),
        },
        "source_files": sorted(path.name for path in context.input_dir.glob("*.csv")),
        "pages": context.page_count,
    }


def build_report(config_path: Path) -> dict[str, object]:
    context = ReportContext.from_file(config_path)
    validate_page_registry(context.page_count)
    configure_matplotlib(context.font_dirs)
    data = load_data(context)
    page_metrics: dict[str, float] = {}
    for spec in PAGE_SPECS:
        result = spec.renderer(context, data)
        overlap = set(page_metrics).intersection(result.metrics)
        if overlap:
            raise ValueError(f"Métricas de página duplicadas: {', '.join(sorted(overlap))}")
        page_metrics.update(result.metrics)
        _save_page(context, result, spec)

    historical_validation = historical_validation_payload(
        data.mayoristas_historico,
        context.historical_start_period,
        context.historical_end_period,
        outputs={"page_svg": "pages/page-05.svg"},
    )
    write_historical_validation(context.historical_validation_path, historical_validation)
    metrics = build_metrics(context, data, page_metrics)
    context.metrics_path.parent.mkdir(parents=True, exist_ok=True)
    context.metrics_path.write_text(
        json.dumps(metrics, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    return metrics


def main() -> None:
    import argparse

    parser = argparse.ArgumentParser()
    parser.add_argument("--config", required=True, type=Path)
    args = parser.parse_args()
    build_report(args.config)


if __name__ == "__main__":
    main()
