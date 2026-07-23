from su_report.olap import OlapError, OlapGateway, OlapResult
from su_report.settings import Settings


def test_report_retries_after_transient_failure(monkeypatch) -> None:
    gateway = OlapGateway(Settings(root=Settings.load().root, config={}))
    calls = 0
    messages: list[str] = []

    def fake_run(arguments, *, on_output=None):
        nonlocal calls
        calls += 1
        if calls == 1:
            raise OlapError("timeout")
        return OlapResult(tuple(arguments), 0, "ok")

    monkeypatch.setattr(gateway, "run", fake_run)
    monkeypatch.setattr("su_report.olap.time.sleep", lambda _seconds: None)

    result = gateway.report_mayoristas(
        ["--year", "2026", "--quarter", "2"],
        gateway.settings.root / "output",
        on_output=messages.append,
    )

    assert result.return_code == 0
    assert calls == 2
    assert messages == ["Consulta OLAP interrumpida; reintentando (2/3)."]


def test_historical_report_uses_fixed_business_contract(monkeypatch, tmp_path) -> None:
    gateway = OlapGateway(Settings(root=Settings.load().root, config={}))
    captured: list[str] = []

    def fake_run(arguments, *, on_output=None, attempts=3):
        del on_output, attempts
        captured.extend(arguments)
        return OlapResult(tuple(arguments), 0, "ok")

    monkeypatch.setattr(gateway, "_run_with_retries", fake_run)
    result = gateway.report_mayoristas_historico(
        start_year=2011,
        start_month=1,
        end_year=2026,
        end_month=6,
        output_dir=tmp_path,
    )

    assert result.return_code == 0
    assert captured == [
        "report",
        "mayoristas_historico",
        "--start-year",
        "2011",
        "--start-month",
        "1",
        "--end-year",
        "2026",
        "--end-month",
        "6",
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
        str(tmp_path),
    ]


def test_eds_top_gateway_uses_dispatched_top_20_contract(monkeypatch, tmp_path) -> None:
    gateway = OlapGateway(Settings(root=Settings.load().root, config={}))
    captured: list[str] = []

    def fake_run(arguments, *, on_output=None, attempts=3):
        del on_output, attempts
        captured.extend(arguments)
        return OlapResult(tuple(arguments), 0, "ok")

    monkeypatch.setattr(gateway, "_run_with_retries", fake_run)
    gateway.report_eds_top(["--year", "2026", "--quarter", "2"], tmp_path)

    assert captured == [
        "report",
        "eds_top",
        "--year",
        "2026",
        "--quarter",
        "2",
        "--measure",
        "dispatched",
        "--product",
        "all",
        "--top",
        "20",
        "--timeout",
        "600",
        "--output-dir",
        str(tmp_path),
    ]


def test_eds_frontera_gateway_uses_focused_contract(monkeypatch, tmp_path) -> None:
    gateway = OlapGateway(Settings(root=Settings.load().root, config={}))
    captured: list[str] = []

    def fake_run(arguments, *, on_output=None, attempts=3):
        del on_output, attempts
        captured.extend(arguments)
        return OlapResult(tuple(arguments), 0, "ok")

    monkeypatch.setattr(gateway, "_run_with_retries", fake_run)
    gateway.report_eds_frontera(["--year", "2026", "--quarter", "2"], tmp_path)

    assert captured == [
        "report",
        "eds_frontera",
        "--year",
        "2026",
        "--quarter",
        "2",
        "--timeout",
        "600",
        "--output-dir",
        str(tmp_path),
    ]
