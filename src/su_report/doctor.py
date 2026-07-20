from __future__ import annotations

import shutil
import subprocess
from dataclasses import dataclass
from pathlib import Path

from su_report.settings import Settings


@dataclass(frozen=True, slots=True)
class Check:
    name: str
    ok: bool
    detail: str
    required: bool = True


def _command(name: str) -> Path | None:
    value = shutil.which(name)
    return Path(value) if value else None


def _version(executable: Path | None) -> str | None:
    if executable is None:
        return None
    completed = subprocess.run(
        [str(executable), "--version"],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        shell=False,
        check=False,
    )
    lines = (completed.stdout or completed.stderr).strip().splitlines()
    return lines[0] if completed.returncode == 0 and lines else None


def run_doctor(settings: Settings) -> list[Check]:
    uv = _command("uv")
    quarto = _command("quarto")
    dotnet = _command("dotnet")
    uv_version = _version(uv)
    quarto_version = _version(quarto)
    dotnet_version = _version(dotnet)
    checks = [
        Check("uv", uv_version is not None, uv_version or "No encontrado en PATH"),
        Check("uv.lock", settings.path("uv.lock").exists(), str(settings.path("uv.lock"))),
        Check("Quarto", quarto_version is not None, quarto_version or "No encontrado en PATH"),
        Check(
            ".NET SDK 10",
            dotnet_version is not None and dotnet_version.split(".", 1)[0] == "10",
            dotnet_version or "No encontrado en PATH",
        ),
        Check("Motor OLAP C#", settings.olap_project_path.exists(), str(settings.olap_project_path)),
        Check("Logo", settings.logo_path.exists(), str(settings.logo_path)),
        Check("Geografía", settings.geography_path.exists(), str(settings.geography_path)),
        Check(
            "Segoe UI",
            settings.active_font_dir is not None,
            str(settings.active_font_dir) if settings.active_font_dir else "Instale segoeui.ttf y segoeuib.ttf en .local/fonts",
        ),
    ]
    for key, present in settings.secret_status.items():
        checks.append(Check(key, present, "Configurada" if present else "Falta en .env"))
    return checks


def doctor_succeeded(checks: list[Check]) -> bool:
    return all(check.ok for check in checks if check.required)
