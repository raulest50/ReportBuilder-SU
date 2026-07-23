from __future__ import annotations

import subprocess
import threading
import time
from collections.abc import Callable, Sequence
from dataclasses import dataclass
from pathlib import Path

from su_report.security import sanitize
from su_report.settings import Settings


class OlapError(RuntimeError):
    """Error seguro producido por el motor OLAP."""


@dataclass(frozen=True, slots=True)
class OlapResult:
    arguments: tuple[str, ...]
    return_code: int
    output: str


class OlapGateway:
    def __init__(self, settings: Settings) -> None:
        self.settings = settings
        self._process: subprocess.Popen[str] | None = None
        self._lock = threading.Lock()

    def _base_command(self) -> list[str]:
        project = self.settings.olap_project_path
        dll = project.parent / "bin" / "Release" / "net10.0" / "WinSbi.Olap.Cli.dll"
        if dll.exists():
            return ["dotnet", str(dll)]
        return ["dotnet", "run", "--project", str(project), "--"]

    def run(
        self,
        arguments: Sequence[str],
        *,
        on_output: Callable[[str], None] | None = None,
    ) -> OlapResult:
        command = [*self._base_command(), *map(str, arguments)]
        output_lines: list[str] = []
        process = subprocess.Popen(
            command,
            cwd=self.settings.root,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            encoding="utf-8",
            errors="replace",
            shell=False,
        )
        with self._lock:
            self._process = process
        try:
            if process.stdout is None:
                raise OlapError("No fue posible capturar la salida del motor OLAP.")
            for raw_line in process.stdout:
                line = sanitize(raw_line.rstrip())
                output_lines.append(line)
                if on_output is not None:
                    on_output(line)
            return_code = process.wait()
        finally:
            with self._lock:
                self._process = None

        output = "\n".join(output_lines)
        if return_code != 0:
            tail = "\n".join(output_lines[-20:])
            raise OlapError(f"El motor OLAP terminó con código {return_code}.\n{tail}")
        return OlapResult(tuple(map(str, arguments)), return_code, output)

    def _run_with_retries(
        self,
        arguments: Sequence[str],
        *,
        on_output: Callable[[str], None] | None = None,
        attempts: int = 3,
    ) -> OlapResult:
        for attempt in range(1, attempts + 1):
            try:
                return self.run(arguments, on_output=on_output)
            except OlapError:
                if attempt == attempts:
                    raise
                if on_output is not None:
                    on_output(f"Consulta OLAP interrumpida; reintentando ({attempt + 1}/{attempts}).")
                time.sleep(3 * attempt)
        raise AssertionError("El ciclo de reintentos OLAP terminó sin resultado.")

    def cancel(self) -> bool:
        with self._lock:
            process = self._process
        if process is None or process.poll() is not None:
            return False
        process.terminate()
        return True

    def diagnose(self, on_output: Callable[[str], None] | None = None) -> OlapResult:
        return self.run(["diagnose", "--source", "liqs", "--format", "json"], on_output=on_output)

    def report_mayoristas(
        self,
        period_arguments: Sequence[str],
        output_dir: Path,
        *,
        on_output: Callable[[str], None] | None = None,
    ) -> OlapResult:
        return self._run_with_retries(
            [
                "report",
                "mayoristas",
                *period_arguments,
                "--measure",
                "accepted",
                "--product",
                "all",
                "--timeout",
                "600",
                "--output-dir",
                str(output_dir),
            ],
            on_output=on_output,
        )

    def report_mayoristas_historico(
        self,
        *,
        start_year: int,
        start_month: int,
        end_year: int,
        end_month: int,
        output_dir: Path,
        on_output: Callable[[str], None] | None = None,
    ) -> OlapResult:
        return self._run_with_retries(
            [
                "report",
                "mayoristas_historico",
                "--start-year",
                str(start_year),
                "--start-month",
                str(start_month),
                "--end-year",
                str(end_year),
                "--end-month",
                str(end_month),
                "--measure",
                "dispatched",
                "--product",
                "all",
                "--buyer-scope",
                "eds_fluvial",
                "--resume",
                "--timeout",
                "600",
                "--output-dir",
                str(output_dir),
            ],
            on_output=on_output,
        )

    def report_eds_municipios(
        self,
        period_arguments: Sequence[str],
        output_dir: Path,
        *,
        on_output: Callable[[str], None] | None = None,
    ) -> OlapResult:
        return self._run_with_retries(
            [
                "report",
                "eds_municipios",
                *period_arguments,
                "--product",
                "all",
                "--timeout",
                "600",
                "--output-dir",
                str(output_dir),
            ],
            on_output=on_output,
        )

    def report_eds_top(
        self,
        period_arguments: Sequence[str],
        output_dir: Path,
        *,
        on_output: Callable[[str], None] | None = None,
    ) -> OlapResult:
        return self._run_with_retries(
            [
                "report",
                "eds_top",
                *period_arguments,
                "--measure",
                "dispatched",
                "--product",
                "all",
                "--top",
                "20",
                "--timeout",
                "600",
                "--output-dir",
                str(output_dir),
            ],
            on_output=on_output,
        )
