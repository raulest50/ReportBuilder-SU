# ReportBuilder-SU

Aplicación autónoma para extraer, validar y convertir datos de combustibles
líquidos en informes PDF trimestrales, semestrales y anuales. La interfaz principal es
una TUI; la misma funcionalidad está disponible como CLI para automatización.

## Inicio rápido

Requisitos del sistema: `uv`, Quarto y .NET SDK 10.

```bash
cp .env.example .env
bash scripts/setup.sh
uv run su-report
```

En Windows PowerShell:

```powershell
Copy-Item .env.example .env
.\scripts\setup.ps1
uv run su-report
```

Complete `.env` localmente antes de consultar SICOM. La aplicación nunca debe
mostrar ni registrar sus valores.

## Operación por comandos

```bash
uv run su-report doctor
uv run su-report fetch --period 2026-Q2
uv run su-report render --period 2026-Q2
uv run su-report render --period 2026-Q2 --cover-resolution 1k
uv run su-report render --period 2026-Q2 --cover-resolution 4k
uv run su-report generate --period 2026-Q2
uv run su-report generate --period 2026-S1
uv run su-report generate --period 2025-A

uv run su-report olap diagnose
uv run su-report olap catalogs
uv run su-report olap cubes --catalog SBI-Ordenes-Pedidos
uv run su-report olap profile --catalog SBI-Ordenes-Pedidos --cube Ordenes-Pedidos
uv run su-report olap mdx --catalog SBI-Ordenes-Pedidos --mdx-file consulta.mdx --format csv
```

`generate` siempre actualiza las fuentes. `render` usa exclusivamente los CSV
locales existentes. Los formatos de periodo son `YYYY-Q1` a `YYYY-Q4`,
`YYYY-S1` a `YYYY-S2` y `YYYY-A`. Un periodo anual exige enero-diciembre
completos. `render` y `generate` aceptan
`--cover-resolution {1k|2k|4k}`; si se omite, la portada utiliza `2k`.

## Histórico mensual de mayoristas

El histórico mensual se extrae por años con el CLI incorporado. El pipeline
`fetch` conserva un snapshot por periodo, `render` valida el CSV local y la
página 5 del PDF se genera como una gráfica vectorial de áreas apiladas. La
página 4 mantiene su contrato de volumen aceptado; la página 5 usa volumen
despachado a EDS automotrices y fluviales.

```powershell
dotnet run --project .\tools\sicom-olap-cli\src\WinSbi.Olap.Cli\WinSbi.Olap.Cli.csproj -- `
  report mayoristas_historico --start-year 2011 --start-month 1 `
  --end-year 2026 --end-month 6 --measure dispatched --product all `
  --buyer-scope eds_fluvial --resume --timeout 600 `
  --output-dir data\2026-Q2\standalone\mayoristas-historico

.\.venv\Scripts\python.exe .\scripts\build_mayoristas_historico.py `
  --input data\2026-Q2\standalone\mayoristas-historico\mayoristas-historico-mensual.csv `
  --output-dir build\2026-Q2\mayoristas-historico `
  --start-period 2011-01 --end-period 2026-06
```

Estos comandos continúan disponibles para generar y verificar la figura de
forma independiente. En el flujo normal basta ejecutar `su-report generate`:
la extracción histórica, la página 5 y el PDF quedan integrados automáticamente.

## Top 20 EDS por volumen despachado

La página 12 usa un reporte OLAP enfocado e independiente de `eds_insights`.
El ranking y el total nacional se calculan con la misma medida, productos y
meses del periodo. Para ejecutarlo de forma aislada:

```powershell
uv run su-report olap report eds-top --period 2026-Q2 `
  --measure dispatched --product all --top 20 `
  --output-dir data\manual-olap\eds-top
```

La salida comprende `eds-top-volumen.csv` y `eds-top-manifest.json`. En el
flujo normal, `fetch` los genera y valida automáticamente.

## Resultados locales

```text
data/<periodo>/       CSV crudos y normalizados
build/<periodo>/      SVG y recursos intermedios
dist/<periodo>/       PDF, métricas y manifiesto
```

Estas carpetas están ignoradas por Git. Consulte [docs/SETUP.md](docs/SETUP.md)
para el proceso completo y las notas de portabilidad, y
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) para los contratos de datos y la
especificación de las 13 páginas.

La composición visual está dividida en un módulo Python por página bajo
`src/su_report/reporting/pages`. El registro ordenado y la escritura de SVG se
centralizan en `src/su_report/reporting/builder.py`; consulte la arquitectura
antes de incorporar una página nueva.

## Publicación manual en GitHub

```bash
git init -b main
git add .
git status
git commit -m "Initial version of ReportBuilder-SU"
gh auth status
gh repo create ReportBuilder-SU --public --source=. --remote=origin --push
```
