from pathlib import Path

from su_report.settings import Settings


def test_quarto_is_discovered_in_standard_windows_install(monkeypatch, tmp_path: Path) -> None:
    executable = tmp_path / "Quarto" / "bin" / "quarto.exe"
    executable.parent.mkdir(parents=True)
    executable.write_bytes(b"")
    monkeypatch.setattr("su_report.settings.shutil.which", lambda _name: None)
    monkeypatch.setenv("PROGRAMFILES", str(tmp_path))
    monkeypatch.delenv("LOCALAPPDATA", raising=False)

    settings = Settings(root=tmp_path, config={})

    assert settings.quarto_executable == executable
