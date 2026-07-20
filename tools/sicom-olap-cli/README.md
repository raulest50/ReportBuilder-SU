# sicom-olap-cli incorporado

Cliente .NET multiplataforma para SICOM OLAP mediante XMLA sobre `msmdpump.dll`.
Esta copia forma parte de `ReportBuilder-SU`; conserva las rutinas del cliente
operacional original y puede ejecutarse directamente en Windows o Linux.

This project intentionally does not reference or modify `sbi-icli`, `sbi-cloning`, or `sbi-custom-cli`.

## Build

```powershell
dotnet build .\WinSbi.Olap.csproj
dotnet build .\src\WinSbi.Olap.Cli\WinSbi.Olap.Cli.csproj
dotnet test .\tests\WinSbi.Olap.Tests\WinSbi.Olap.Tests.csproj
```

## Secrets

Resolution order is:

1. CLI options
2. Environment variables
3. `.env` encontrado desde el directorio de trabajo o sus ancestros
4. .NET user-secrets

Expected keys:

- `USER_CUBO_SICOM`
- `PASSWORD_CUBO_SICOM`
- `CUBO_LIQS_URL`
- `CUBO_AGENTS_URL`

The CLI never prints passwords or Authorization headers.

## Commands

Desde `tools/sicom-olap-cli`, `dotnet run` inicia el asistente interactivo.
Las salidas manuales predeterminadas quedan en `results-interactive-cli/SBI-Ordenes-Pedidos/<report>/<period>/`.

```powershell
dotnet run

dotnet run --project src/WinSbi.Olap.Cli/WinSbi.Olap.Cli.csproj -- diagnose --source liqs
dotnet run --project src/WinSbi.Olap.Cli/WinSbi.Olap.Cli.csproj -- catalogs --source liqs --format table
dotnet run --project src/WinSbi.Olap.Cli/WinSbi.Olap.Cli.csproj -- cubes --source liqs --catalog SBI-Ordenes-Pedidos --format json
dotnet run --project src/WinSbi.Olap.Cli/WinSbi.Olap.Cli.csproj -- profile --source liqs --catalog SBI-Ordenes-Pedidos --cube Ordenes-Pedidos --format json --output reports/SBI-Ordenes-Pedidos/ordenes-pedidos-profile.json
dotnet run --project src/WinSbi.Olap.Cli/WinSbi.Olap.Cli.csproj -- mdx --source liqs --catalog SBI-Ordenes-Pedidos --timeout 600 --format csv --output reports/SBI-Ordenes-Pedidos/ordenes-pedidos-market-share-2025-2026.csv --mdx-file queries/SBI-Ordenes-Pedidos/market-share-accepted-2025-2026.mdx
```

## Reports

The short report command uses built-in MDX templates for recurrent Power BI CSV generation:

```powershell
dotnet run -- report mayoristas --year 2026 --quarter 2
dotnet run -- report mayoristas --year 2026 --semester 1
dotnet run -- report mayoristas --year 2026 --months 4,5,6 --measure both --product corriente
dotnet run -- report eds_municipios --year 2026 --quarter 2
dotnet run -- report eds_municipios --year 2026 --semester 1
dotnet run -- report eds_municipios --year 2026 --months 4,5,6 --product diesel
dotnet run -- report eds_insights --year 2026 --quarter 2
dotnet run -- report eds_insights --year 2026 --semester 1
dotnet run -- report eds_insights --year 2026 --months 4,5,6 --product all --top-cities 7 --top-eds 20
dotnet run -- report eds_percentiles --end-year 2026 --end-month 6
dotnet run -- report eds_percentiles --end-year 2026 --end-month 6 --buyer-scope eds_fluvial
```

`report mayoristas` defaults:

- catalog: `SBI-Ordenes-Pedidos`
- provider type: `DISTRIBUIDOR MAYORISTA`
- buyer subtypes: service station automotive, service station fluvial, industrial marketer
- products: current gasoline, extra gasoline, biodiesel blend
- measure: accepted volume

Options:

- `--year <yyyy>`
- `--quarter 1|2|3|4`
- `--semester 1|2`
- `--months <csv>`
- `--measure accepted|dispatched|both`
- `--product all|corriente|extra|diesel`
- `--output-dir <path>`

Generated files:

- `mayoristas-detalle.csv`
- `mayoristas-resumen.csv`
- `mayoristas-productos.csv` when `--product all`

`report eds_municipios` generates monthly accepted-volume CSVs for automotive service stations by municipality.

Defaults:

- catalog: `SBI-Ordenes-Pedidos`
- buyer subtype scope: automotive service station by default
- products: current gasoline, extra gasoline, biodiesel blend
- measure: accepted volume
- location fields: buyer department, buyer municipality, buyer border-zone flag
- active EDS count: separate census-style CSV from `SBI-Agentes`, filtered to active automotive service stations

Options:

- `--year <yyyy>`
- `--quarter 1|2|3|4`
- `--semester 1|2`
- `--months <csv>`
- `--product all|corriente|extra|diesel`
- `--output-dir <path>`

Generated files:

- `eds-municipios-detalle.csv`
- `eds-municipios-promedio.csv`
- `eds-municipios-eds-activas.csv`

Use `eds-municipios-eds-activas.csv` as a separate Power BI table and relate it to the volume CSVs by `CODIGO_DANE_MUNICIPIO`.

`report eds_insights` generates several related CSVs for automotive service station analysis. It combines active-station counts from `SBI-Agentes` with accepted and dispatched volumes from `SBI-Ordenes-Pedidos`.

Defaults:

- order catalog: `SBI-Ordenes-Pedidos`
- agents catalog: `SBI-Agentes`
- buyer subtype: automotive service station
- products: current gasoline, extra gasoline, biodiesel blend
- ranking measure: accepted volume
- auxiliary volume measure: dispatched volume
- top cities: 7
- top EDS: 20

Options:

- `--year <yyyy>`
- `--quarter 1|2|3|4`
- `--semester 1|2`
- `--months <csv>`
- `--product all|corriente|extra|diesel`
- `--top-cities <n>`
- `--top-eds <n>`
- `--output-dir <path>`

Generated files:

- `eds-banderas-nacional.csv`
- `eds-ciudades-top.csv`
- `eds-banderas-ciudades-top.csv`
- `eds-top20-volumen.csv`
- `eds-frontera-resumen.csv`
- `eds-frontera-productos.csv`

`VOLUMEN_ACEPTADO` and `VOLUMEN_DESPACHADO` are exported as separate columns. They are not summed together.

`report eds_percentiles` generates a rolling 12-month automotive service station percentile analysis from `SBI-Ordenes-Pedidos`.

Defaults:

- buyer subtype: automotive service station
- products: current gasoline, extra gasoline, biodiesel blend
- percentile ranking metric: accepted gal/month total
- auxiliary contrast metric: dispatched gal/month total
- rolling window: 12 closed months ending in `--end-year` / `--end-month`

Options:

- `--end-year <yyyy>`
- `--end-month <1-12>`
- `--product all|corriente|extra|diesel`
- `--buyer-scope eds|eds_fluvial|eds_fluvial_industrial`
- `--output-dir <path>`

Generated files:

- `eds-percentiles-base-12m.csv`
- `eds-percentiles-estaciones.csv`
- `eds-percentiles-resumen.csv`

The base CSV keeps accepted and dispatched volume separate at the `buyer x month x product x buyer subtype` grain. Deciles are calculated from `GAL_MES_ACEPTADO_TOTAL`; dispatched values are included only as comparison columns.

## Folder conventions

- Reusable MDX queries live under `queries/<catalog>/`.
- Generated CSV/JSON/XML/log outputs live under `reports/<catalog>/`.
- Debug XML/log artifacts live under `reports/<catalog>/debug/`.

Common options:

- `--source liqs|agents`
- `--url <endpoint>`
- `--user <user>`
- `--password <password>`
- `--auth basic|challenge`
- `--timeout <seconds>`
- `--format table|json|csv`
- `--output <path>`
- `--xmla-request-output <path>`
- `--xmla-response-output <path>`
