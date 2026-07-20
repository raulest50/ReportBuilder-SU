# ReportBuilder-SU

Aplicación autónoma para extraer, validar y convertir datos de combustibles
líquidos en informes PDF trimestrales y semestrales. La interfaz principal es
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
uv run su-report generate --period 2026-Q2
uv run su-report generate --period 2026-S1

uv run su-report olap diagnose
uv run su-report olap catalogs
uv run su-report olap cubes --catalog SBI-Ordenes-Pedidos
uv run su-report olap profile --catalog SBI-Ordenes-Pedidos --cube Ordenes-Pedidos
uv run su-report olap mdx --catalog SBI-Ordenes-Pedidos --mdx-file consulta.mdx --format csv
```

`generate` siempre actualiza las fuentes. `render` usa exclusivamente los CSV
locales existentes. Los formatos de periodo son `YYYY-Q1` a `YYYY-Q4` y
`YYYY-S1` a `YYYY-S2`.

## Resultados locales

```text
data/<periodo>/       CSV crudos y normalizados
build/<periodo>/      SVG y recursos intermedios
dist/<periodo>/       PDF, métricas y manifiesto
```

Estas carpetas están ignoradas por Git. Consulte [docs/SETUP.md](docs/SETUP.md)
para el proceso completo y las notas de portabilidad, y
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) para los contratos de datos y la
especificación de las 12 páginas.

## Publicación manual en GitHub

```bash
git init -b main
git add .
git status
git commit -m "Initial version of ReportBuilder-SU"
gh auth status
gh repo create ReportBuilder-SU --public --source=. --remote=origin --push
```
