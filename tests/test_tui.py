from su_report.tui.app import ReportBuilderApp


async def test_tui_starts_and_exposes_main_tabs() -> None:
    app = ReportBuilderApp()

    async with app.run_test() as pilot:
        assert app.query_one("#generate-tab")
        assert app.query_one("#olap-tab")
        assert app.query_one("#system-tab")
        await pilot.pause()
