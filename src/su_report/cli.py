from __future__ import annotations

from collections.abc import Sequence
from pathlib import Path
from typing import Annotated

import typer
from rich.console import Console
from rich.table import Table

from su_report.doctor import doctor_succeeded, run_doctor
from su_report.models import PeriodSpec, PipelineEvent
from su_report.olap import OlapError, OlapGateway
from su_report.pipeline import ReportPipeline
from su_report.security import sanitize
from su_report.settings import Settings


console = Console()
app = typer.Typer(
    name="su-report",
    help="Generador trimestral y semestral de informes SU.",
    invoke_without_command=True,
    no_args_is_help=False,
    pretty_exceptions_show_locals=False,
)
olap_app = typer.Typer(help="Acceso directo al motor SICOM OLAP incorporado.")
olap_report_app = typer.Typer(help="Reportes reutilizables del motor SICOM OLAP.")
app.add_typer(olap_app, name="olap")
olap_app.add_typer(olap_report_app, name="report")

PeriodOption = Annotated[
    str,
    typer.Option("--period", "-p", help="Periodo YYYY-Q1..Q4 o YYYY-S1..S2."),
]
SourceOption = Annotated[str, typer.Option("--source", help="Fuente secreta: liqs o agents.")]
FormatOption = Annotated[str, typer.Option("--format", "-f", help="Formato: table, json o csv.")]
OutputOption = Annotated[Path | None, typer.Option("--output", "-o", help="Archivo de salida opcional.")]
DEFAULT_PERIOD = PeriodSpec.previous_completed().code
DEFAULT_MAYORISTAS_DIR = Path("data/manual-olap/mayoristas")
DEFAULT_EDS_MUNICIPIOS_DIR = Path("data/manual-olap/eds-municipios")
DEFAULT_EDS_INSIGHTS_DIR = Path("data/manual-olap/eds-insights")
DEFAULT_EDS_PERCENTILES_DIR = Path("data/manual-olap/eds-percentiles")


def _period(value: str) -> PeriodSpec:
    try:
        return PeriodSpec.parse(value)
    except ValueError as error:
        raise typer.BadParameter(str(error), param_hint="--period") from error


def _event(event: PipelineEvent) -> None:
    styles = {"warning": "yellow", "error": "bold red", "info": "cyan"}
    console.print(f"[{styles.get(event.level, 'white')}]{event.stage.value}:[/] {event.message}")


def _execute_pipeline(operation: str, period_value: str) -> None:
    period = _period(period_value)
    pipeline = ReportPipeline(on_event=_event)
    try:
        result = getattr(pipeline, operation)(period)
    except (RuntimeError, ValueError, OSError) as error:
        console.print(f"[bold red]Error:[/] {sanitize(str(error))}")
        raise typer.Exit(1) from error
    console.print(f"[bold green]Listo:[/] {result.paths.pdf_path if operation != 'fetch' else result.paths.data_dir}")
    if result.warnings:
        console.print(f"[yellow]{len(result.warnings)} advertencia(s); consulte el manifiesto.[/]")


def _olap_run(arguments: Sequence[str]) -> None:
    gateway = OlapGateway(Settings.load())
    try:
        result = gateway.run(arguments, on_output=console.print)
    except (OlapError, OSError) as error:
        console.print(f"[bold red]Error OLAP:[/] {error}")
        raise typer.Exit(1) from error
    if not result.output:
        console.print("[green]Operación OLAP completada.[/]")


def _output_arguments(output: Path | None) -> list[str]:
    return ["--output", str(output)] if output is not None else []


@app.callback()
def root(ctx: typer.Context) -> None:
    """Abre la TUI cuando no se especifica un subcomando."""
    if ctx.invoked_subcommand is None:
        from su_report.tui.app import run_tui

        run_tui()


@app.command()
def tui() -> None:
    """Abre explícitamente la interfaz de terminal."""
    from su_report.tui.app import run_tui

    run_tui()


@app.command()
def doctor() -> None:
    """Comprueba herramientas, activos y presencia de configuración secreta."""
    checks = run_doctor(Settings.load())
    table = Table(title="Diagnóstico de ReportBuilder-SU", show_lines=False)
    table.add_column("Estado", justify="center")
    table.add_column("Componente", style="bold")
    table.add_column("Detalle")
    for check in checks:
        table.add_row("[green]OK[/]" if check.ok else "[red]FALTA[/]", check.name, check.detail)
    console.print(table)
    if not doctor_succeeded(checks):
        raise typer.Exit(1)


@app.command()
def fetch(period: PeriodOption = DEFAULT_PERIOD) -> None:
    """Actualiza Datos Abiertos y SICOM, y normaliza los CSV."""
    _execute_pipeline("fetch", period)


@app.command()
def render(period: PeriodOption = DEFAULT_PERIOD) -> None:
    """Genera el PDF usando únicamente los CSV locales existentes."""
    _execute_pipeline("render", period)


@app.command()
def generate(period: PeriodOption = DEFAULT_PERIOD) -> None:
    """Actualiza todas las fuentes y genera el PDF final."""
    _execute_pipeline("generate", period)


@olap_app.command()
def diagnose(source: SourceOption = "liqs", format_: FormatOption = "json") -> None:
    """Prueba la conexión y el descubrimiento XMLA."""
    _olap_run(["diagnose", "--source", source, "--format", format_])


@olap_app.command()
def catalogs(
    source: SourceOption = "liqs",
    format_: FormatOption = "table",
    output: OutputOption = None,
) -> None:
    """Lista los catálogos XMLA."""
    _olap_run(["catalogs", "--source", source, "--format", format_, *_output_arguments(output)])


@olap_app.command()
def cubes(
    catalog: Annotated[str, typer.Option("--catalog", help="Catálogo Analysis Services.")],
    source: SourceOption = "liqs",
    format_: FormatOption = "table",
    output: OutputOption = None,
) -> None:
    """Lista los cubos de un catálogo."""
    _olap_run(
        ["cubes", "--source", source, "--catalog", catalog, "--format", format_, *_output_arguments(output)]
    )


@olap_app.command()
def profile(
    catalog: Annotated[str, typer.Option("--catalog", help="Catálogo Analysis Services.")],
    cube: Annotated[str, typer.Option("--cube", help="Nombre del cubo.")],
    source: SourceOption = "liqs",
    format_: FormatOption = "json",
    output: OutputOption = None,
) -> None:
    """Exporta el perfil de dimensiones, medidas y campos de un cubo."""
    _olap_run(
        [
            "profile",
            "--source",
            source,
            "--catalog",
            catalog,
            "--cube",
            cube,
            "--format",
            format_,
            *_output_arguments(output),
        ]
    )


@olap_app.command()
def mdx(
    catalog: Annotated[str, typer.Option("--catalog", help="Catálogo Analysis Services.")],
    mdx_file: Annotated[Path, typer.Option("--mdx-file", exists=True, dir_okay=False, help="Consulta MDX local.")],
    source: SourceOption = "liqs",
    format_: FormatOption = "csv",
    output: OutputOption = None,
    timeout: Annotated[int, typer.Option("--timeout", min=1)] = 600,
) -> None:
    """Ejecuta una consulta MDX desde archivo."""
    _olap_run(
        [
            "mdx",
            "--source",
            source,
            "--catalog",
            catalog,
            "--mdx-file",
            str(mdx_file.resolve()),
            "--format",
            format_,
            "--timeout",
            str(timeout),
            *_output_arguments(output),
        ]
    )


def _report_arguments(
    report_name: str,
    period_value: str,
    output_dir: Path,
    source: str,
    product: str,
) -> list[str]:
    period = _period(period_value)
    return [
        "report",
        report_name,
        *period.olap_arguments(),
        "--product",
        product,
        "--source",
        source,
        "--timeout",
        "600",
        "--output-dir",
        str(output_dir),
    ]


@olap_report_app.command("mayoristas")
def report_mayoristas(
    period: PeriodOption = DEFAULT_PERIOD,
    output_dir: Annotated[Path, typer.Option("--output-dir")] = DEFAULT_MAYORISTAS_DIR,
    source: SourceOption = "liqs",
    product: Annotated[str, typer.Option("--product")] = "all",
    measure: Annotated[str, typer.Option("--measure")] = "accepted",
) -> None:
    """Genera participación de mayoristas con volumen aceptado por defecto."""
    arguments = _report_arguments("mayoristas", period, output_dir, source, product)
    arguments[2:2] = ["--measure", measure]
    _olap_run(arguments)


@olap_report_app.command("eds-municipios")
def report_eds_municipios(
    period: PeriodOption = DEFAULT_PERIOD,
    output_dir: Annotated[Path, typer.Option("--output-dir")] = DEFAULT_EDS_MUNICIPIOS_DIR,
    source: SourceOption = "liqs",
    product: Annotated[str, typer.Option("--product")] = "all",
) -> None:
    """Genera volúmenes aceptados de EDS por municipio."""
    _olap_run(_report_arguments("eds_municipios", period, output_dir, source, product))


@olap_report_app.command("eds-insights")
def report_eds_insights(
    period: PeriodOption = DEFAULT_PERIOD,
    output_dir: Annotated[Path, typer.Option("--output-dir")] = DEFAULT_EDS_INSIGHTS_DIR,
    source: SourceOption = "liqs",
    product: Annotated[str, typer.Option("--product")] = "all",
    top_cities: Annotated[int, typer.Option("--top-cities", min=1)] = 7,
    top_eds: Annotated[int, typer.Option("--top-eds", min=1)] = 20,
) -> None:
    """Genera el paquete analítico de EDS."""
    arguments = _report_arguments("eds_insights", period, output_dir, source, product)
    arguments.extend(["--top-cities", str(top_cities), "--top-eds", str(top_eds)])
    _olap_run(arguments)


@olap_report_app.command("eds-percentiles")
def report_eds_percentiles(
    end_year: Annotated[int, typer.Option("--end-year", min=2000, max=2100)],
    end_month: Annotated[int, typer.Option("--end-month", min=1, max=12)],
    output_dir: Annotated[Path, typer.Option("--output-dir")] = DEFAULT_EDS_PERCENTILES_DIR,
    source: SourceOption = "liqs",
    product: Annotated[str, typer.Option("--product")] = "all",
    buyer_scope: Annotated[str, typer.Option("--buyer-scope")] = "eds",
) -> None:
    """Genera percentiles de galones/mes para la ventana móvil de 12 meses."""
    _olap_run(
        [
            "report",
            "eds_percentiles",
            "--end-year",
            str(end_year),
            "--end-month",
            str(end_month),
            "--product",
            product,
            "--buyer-scope",
            buyer_scope,
            "--source",
            source,
            "--timeout",
            "600",
            "--output-dir",
            str(output_dir),
        ]
    )


def main() -> None:
    app()


if __name__ == "__main__":
    main()
