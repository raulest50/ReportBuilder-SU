from __future__ import annotations

from pathlib import Path

from textual import work
from textual.app import App, ComposeResult
from textual.containers import Horizontal, Vertical, VerticalScroll
from textual.widgets import (
    Button,
    DataTable,
    Footer,
    Header,
    Input,
    Label,
    ProgressBar,
    RichLog,
    Select,
    Static,
    TabbedContent,
    TabPane,
)

from su_report.doctor import run_doctor
from su_report.models import PeriodSpec, PipelineEvent
from su_report.olap import OlapGateway
from su_report.pipeline import ReportPipeline
from su_report.security import sanitize
from su_report.settings import Settings


class ReportBuilderApp(App[None]):
    TITLE = "ReportBuilder-SU"
    SUB_TITLE = "Informes trimestrales y semestrales"
    CSS = """
    Screen { background: #f4f5f6; color: #20252b; }
    Header, Footer { background: #173f35; color: white; }
    TabbedContent { height: 1fr; }
    TabPane { padding: 1 2; }
    .panel { border: round #718078; padding: 1 2; margin-bottom: 1; background: white; }
    .form-row { height: auto; margin-bottom: 1; }
    .field { width: 1fr; margin-right: 1; }
    .field Label { color: #49534e; margin-bottom: 0; }
    Input, Select { width: 100%; }
    Button { margin-right: 1; }
    #generate { background: #007a58; color: white; }
    #cancel { background: #9b2f2f; color: white; }
    #progress { margin: 1 0; }
    #pipeline-log, #olap-log { height: 1fr; min-height: 12; border: solid #aeb8b2; }
    #result { color: #0b6b4c; margin-top: 1; }
    #doctor-table { height: 1fr; }
    .hint { color: #5d6862; margin-bottom: 1; }
    """
    BINDINGS = [("ctrl+c", "cancel_work", "Cancelar"), ("q", "quit", "Salir")]

    def __init__(self) -> None:
        super().__init__()
        self.settings = Settings.load()
        self.pipeline = ReportPipeline(self.settings, on_event=self._pipeline_event_from_thread)
        self.gateway = OlapGateway(self.settings)

    def compose(self) -> ComposeResult:
        default_period = PeriodSpec.previous_completed().code
        yield Header()
        with TabbedContent(initial="generate-tab"):
            with TabPane("Generar informe", id="generate-tab"):
                with Vertical(classes="panel"):
                    yield Static(
                        "Escriba el periodo y elija si desea actualizar fuentes, renderizar datos locales o ejecutar todo.",
                        classes="hint",
                    )
                    with Horizontal(classes="form-row"):
                        with Vertical(classes="field"):
                            yield Label("Periodo")
                            yield Input(default_period, placeholder="2026-Q2 o 2026-S1", id="period")
                        with Vertical(classes="field"):
                            yield Label("Operación")
                            yield Select(
                                [("Generar: actualizar + PDF", "generate"), ("Actualizar datos", "fetch"), ("Renderizar PDF local", "render")],
                                value="generate",
                                allow_blank=False,
                                id="pipeline-operation",
                            )
                    with Horizontal():
                        yield Button("Ejecutar", variant="primary", id="generate")
                        yield Button("Cancelar", variant="error", id="cancel", disabled=True)
                    yield ProgressBar(total=100, id="progress")
                    yield Label("Listo para iniciar.", id="result")
                yield RichLog(highlight=True, markup=True, wrap=True, id="pipeline-log")
            with TabPane("Herramientas OLAP", id="olap-tab"):
                with VerticalScroll(classes="panel"):
                    yield Static(
                        "Ejecuta las capacidades incorporadas del cliente C#. Las credenciales se toman de .env y nunca se muestran.",
                        classes="hint",
                    )
                    with Horizontal(classes="form-row"):
                        with Vertical(classes="field"):
                            yield Label("Operación")
                            yield Select(
                                [
                                    ("Diagnosticar conexión", "diagnose"),
                                    ("Listar catálogos", "catalogs"),
                                    ("Listar cubos", "cubes"),
                                    ("Perfilar cubo", "profile"),
                                    ("Ejecutar archivo MDX", "mdx"),
                                    ("Reporte mayoristas", "mayoristas"),
                                    ("Reporte EDS municipios", "eds_municipios"),
                                    ("Reporte EDS insights", "eds_insights"),
                                    ("Reporte EDS percentiles", "eds_percentiles"),
                                ],
                                value="diagnose",
                                allow_blank=False,
                                id="olap-operation",
                            )
                        with Vertical(classes="field"):
                            yield Label("Fuente")
                            yield Select([("SICOM líquidos", "liqs"), ("SICOM agentes", "agents")], value="liqs", allow_blank=False, id="source")
                    with Horizontal(classes="form-row"):
                        with Vertical(classes="field"):
                            yield Label("Catálogo")
                            yield Input("SBI-Ordenes-Pedidos", id="catalog")
                        with Vertical(classes="field"):
                            yield Label("Cubo")
                            yield Input("Ordenes-Pedidos", id="cube")
                    with Horizontal(classes="form-row"):
                        with Vertical(classes="field"):
                            yield Label("Periodo / final de ventana")
                            yield Input(default_period, id="olap-period")
                        with Vertical(classes="field"):
                            yield Label("Archivo MDX")
                            yield Input(placeholder="queries/consulta.mdx", id="mdx-file")
                    with Horizontal():
                        yield Button("Ejecutar OLAP", variant="primary", id="run-olap")
                        yield Button("Cancelar", variant="error", id="cancel-olap", disabled=True)
                yield RichLog(highlight=True, markup=True, wrap=True, id="olap-log")
            with TabPane("Sistema", id="system-tab"):
                yield Static("Diagnóstico local; solo indica si la configuración está presente.", classes="hint")
                yield Button("Actualizar diagnóstico", id="refresh-doctor")
                yield DataTable(id="doctor-table")
        yield Footer()

    def on_mount(self) -> None:
        progress = self.query_one("#progress", ProgressBar)
        progress.update(progress=0)
        self._refresh_doctor()

    def on_unmount(self) -> None:
        self.pipeline.cancel()
        self.gateway.cancel()

    def _refresh_doctor(self) -> None:
        table = self.query_one("#doctor-table", DataTable)
        table.clear(columns=True)
        table.add_columns("Estado", "Componente", "Detalle")
        for check in run_doctor(self.settings):
            table.add_row("OK" if check.ok else "FALTA", check.name, check.detail)

    def on_button_pressed(self, event: Button.Pressed) -> None:
        button_id = event.button.id
        if button_id == "generate":
            self.run_pipeline()
        elif button_id in {"cancel", "cancel-olap"}:
            self.action_cancel_work()
        elif button_id == "run-olap":
            self.run_olap()
        elif button_id == "refresh-doctor":
            self._refresh_doctor()

    def action_cancel_work(self) -> None:
        cancelled = self.pipeline.cancel() or self.gateway.cancel()
        message = "Se solicitó la cancelación del proceso activo." if cancelled else "No hay una consulta OLAP activa."
        self.query_one("#pipeline-log", RichLog).write(f"[yellow]{message}[/]")

    def _set_pipeline_busy(self, busy: bool) -> None:
        self.query_one("#generate", Button).disabled = busy
        self.query_one("#cancel", Button).disabled = not busy

    def _set_olap_busy(self, busy: bool) -> None:
        self.query_one("#run-olap", Button).disabled = busy
        self.query_one("#cancel-olap", Button).disabled = not busy

    def _prepare_pipeline(self) -> tuple[str, str]:
        self._set_pipeline_busy(True)
        self.query_one("#pipeline-log", RichLog).clear()
        self.query_one("#progress", ProgressBar).update(progress=0)
        return (
            self.query_one("#period", Input).value,
            str(self.query_one("#pipeline-operation", Select).value),
        )

    def _finish_pipeline(self, target: Path) -> None:
        self.query_one("#result", Label).update(f"Listo: {target}")

    def _fail_pipeline(self, message: str) -> None:
        self.query_one("#pipeline-log", RichLog).write(f"[bold red]Error:[/] {message}")
        self.query_one("#result", Label).update("La operación no pudo completarse.")

    def _append_olap(self, line: str) -> None:
        self.query_one("#olap-log", RichLog).write(line)

    def _prepare_olap(self) -> list[str]:
        self._set_olap_busy(True)
        self.query_one("#olap-log", RichLog).clear()
        return self._olap_arguments()

    def _pipeline_event_from_thread(self, event: PipelineEvent) -> None:
        self.call_from_thread(self._show_pipeline_event, event)

    def _show_pipeline_event(self, event: PipelineEvent) -> None:
        log = self.query_one("#pipeline-log", RichLog)
        color = "yellow" if event.level == "warning" else "cyan"
        log.write(f"[{color}]{event.stage.value}[/]: {event.message}")
        if event.progress is not None:
            self.query_one("#progress", ProgressBar).update(progress=event.progress * 100)

    @work(thread=True, exclusive=True, group="pipeline", exit_on_error=False)
    def run_pipeline(self) -> None:
        try:
            period_text, operation = self.call_from_thread(self._prepare_pipeline)
            period = PeriodSpec.parse(period_text)
            result = getattr(self.pipeline, operation)(period)
            target = result.paths.data_dir if operation == "fetch" else result.paths.pdf_path
            self.call_from_thread(self._finish_pipeline, target)
        except Exception as error:  # Textual debe conservarse abierto y mostrar un mensaje seguro.
            self.call_from_thread(self._fail_pipeline, sanitize(str(error)))
        finally:
            self.call_from_thread(self._set_pipeline_busy, False)

    def _olap_arguments(self) -> list[str]:
        operation = str(self.query_one("#olap-operation", Select).value)
        source = str(self.query_one("#source", Select).value)
        catalog = self.query_one("#catalog", Input).value.strip()
        cube = self.query_one("#cube", Input).value.strip()
        period_text = self.query_one("#olap-period", Input).value.strip()
        output_dir = self.settings.root / "data" / "manual-olap" / operation
        common = ["--source", source, "--timeout", "600"]
        if operation == "diagnose":
            return ["diagnose", *common, "--format", "json"]
        if operation == "catalogs":
            return ["catalogs", *common, "--format", "table"]
        if operation == "cubes":
            return ["cubes", "--catalog", catalog, *common, "--format", "table"]
        if operation == "profile":
            return ["profile", "--catalog", catalog, "--cube", cube, *common, "--format", "json"]
        if operation == "mdx":
            mdx_file = Path(self.query_one("#mdx-file", Input).value.strip())
            if not mdx_file.is_absolute():
                mdx_file = self.settings.root / mdx_file
            if not mdx_file.is_file():
                raise ValueError(f"No existe el archivo MDX: {mdx_file}")
            return ["mdx", "--catalog", catalog, "--mdx-file", str(mdx_file), *common, "--format", "table"]
        if operation == "eds_percentiles":
            period = PeriodSpec.parse(period_text)
            return [
                "report",
                operation,
                "--end-year",
                str(period.year),
                "--end-month",
                str(period.months[-1]),
                "--product",
                "all",
                "--buyer-scope",
                "eds",
                *common,
                "--output-dir",
                str(output_dir),
            ]
        period = PeriodSpec.parse(period_text)
        arguments = ["report", operation, *period.olap_arguments(), "--product", "all", *common]
        if operation == "mayoristas":
            arguments.extend(["--measure", "accepted"])
        if operation == "eds_insights":
            arguments.extend(["--top-cities", "7", "--top-eds", "20"])
        arguments.extend(["--output-dir", str(output_dir)])
        return arguments

    @work(thread=True, exclusive=True, group="olap", exit_on_error=False)
    def run_olap(self) -> None:
        try:
            arguments = self.call_from_thread(self._prepare_olap)
            self.gateway.run(arguments, on_output=lambda line: self.call_from_thread(self._append_olap, line))
            self.call_from_thread(self._append_olap, "[bold green]Operación OLAP completada.[/]")
        except Exception as error:  # Mantiene la TUI abierta ante errores externos.
            self.call_from_thread(self._append_olap, f"[bold red]Error:[/] {sanitize(str(error))}")
        finally:
            self.call_from_thread(self._set_olap_busy, False)


def run_tui() -> None:
    ReportBuilderApp().run()
