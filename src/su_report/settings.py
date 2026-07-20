from __future__ import annotations

import os
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import yaml
from dotenv import load_dotenv


SECRET_KEYS = (
    "USER_CUBO_SICOM",
    "PASSWORD_CUBO_SICOM",
    "CUBO_LIQS_URL",
    "CUBO_AGENTS_URL",
)


def project_root() -> Path:
    configured = os.environ.get("SU_REPORT_ROOT")
    if configured:
        return Path(configured).expanduser().resolve()
    return Path(__file__).resolve().parents[2]


@dataclass(frozen=True, slots=True)
class Settings:
    root: Path
    config: dict[str, Any]

    @classmethod
    def load(cls, root: Path | None = None) -> Settings:
        resolved_root = (root or project_root()).resolve()
        load_dotenv(resolved_root / ".env", override=False)
        config_path = resolved_root / "config" / "report.yml"
        config = yaml.safe_load(config_path.read_text(encoding="utf-8"))
        return cls(root=resolved_root, config=config)

    def path(self, *parts: str) -> Path:
        return self.root.joinpath(*parts)

    @property
    def logo_path(self) -> Path:
        return self.path(self.config["assets"]["logo"])

    @property
    def geography_path(self) -> Path:
        return self.path(self.config["assets"]["geography"])

    @property
    def olap_project_path(self) -> Path:
        return self.path(self.config["paths"]["olap_project"])

    @property
    def quarto_template_path(self) -> Path:
        return self.path("templates", "report.typ")

    @property
    def history_years(self) -> int:
        return int(self.config["report"]["history_years"])

    @property
    def secret_status(self) -> dict[str, bool]:
        return {key: bool(os.environ.get(key, "").strip()) for key in SECRET_KEYS}

    @property
    def font_dirs(self) -> tuple[Path, ...]:
        candidates: list[Path] = []
        configured = os.environ.get("SU_REPORT_FONT_DIR")
        if configured:
            candidates.append(Path(configured).expanduser())
        candidates.append(self.path(".local", "fonts"))
        windir = os.environ.get("WINDIR")
        if windir:
            candidates.append(Path(windir) / "Fonts")
        candidates.append(Path("/mnt/c/Windows/Fonts"))
        unique: list[Path] = []
        for candidate in candidates:
            resolved = candidate.resolve()
            if resolved not in unique:
                unique.append(resolved)
        return tuple(unique)

    @property
    def active_font_dir(self) -> Path | None:
        for directory in self.font_dirs:
            if (directory / "segoeui.ttf").exists() and (directory / "segoeuib.ttf").exists():
                return directory
        return None

