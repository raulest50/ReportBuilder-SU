# Instrucciones para agentes

## Propósito

`ReportBuilder-SU` genera informes PDF trimestrales, semestrales y anuales de 13 páginas.
Es un proyecto independiente y multiplataforma. No debe depender de rutas del
workspace que lo originó.

## Arquitectura

- `src/su_report`: TUI Textual, CLI Typer, orquestación, transformaciones y
  renderizado.
- `tools/sicom-olap-cli`: copia integrada del motor C# XMLA. Mantener sus
  rutinas de negocio salvo que el contrato SICOM exija un cambio. Consulte
  `tools/sicom-olap-cli/INTEGRATION.md` antes de sincronizarla.
- `templates`: ensamblaje Typst ejecutado por Quarto.
- `assets`: logo y geografía necesarios para el informe.
- `data`, `build` y `dist`: estado local ignorado por Git.

La TUI y la CLI deben llamar los mismos servicios. No introducir cálculos de
negocio dentro de widgets o comandos.

## Setup y comandos

```bash
bash scripts/setup.sh              # Debian/Kali
.\scripts\setup.ps1                # Windows PowerShell
uv run su-report doctor
uv run su-report                   # TUI
uv run su-report generate --period 2026-Q2
uv run su-report generate --period 2026-S1
```

Validación completa, cuando sea autorizada:

```bash
uv run ruff check .
uv run mypy src
uv run pytest
dotnet test tools/sicom-olap-cli/tests/WinSbi.Olap.Tests/WinSbi.Olap.Tests.csproj
```

## Reglas de negocio SICOM

- Catálogo: `SBI-Ordenes-Pedidos`.
- Cubo: `Ordenes-Pedidos`.
- Mayoristas/EDS: `[Measures].[VOLUMEN ACEPTADO]`.
- Nacional/geografía: `volumen_despachado` de Datos Abiertos.
- Mayorista significa exactamente proveedor tipo `DISTRIBUIDOR MAYORISTA`.
- No usar `TOPCOUNT(19)` como definición empresarial.
- Compradores: EDS automotriz, EDS fluvial y comercializador industrial.
- Productos: gasolina corriente, gasolina extra y biodiésel con mezcla.
- `BIODIESEL CON MEZCLA` representa ACPM; no incorporar diésel marino.
- Nunca mezclar o sumar volumen aceptado y despachado.
- Los meses se derivan del periodo; nunca escribir rangos manualmente.

## Seguridad

- No imprimir credenciales, tokens, contraseñas ni encabezados Authorization.
- `.env`, CSV, fuentes propietarias, logs y resultados deben permanecer fuera
  de Git.
- Invocar C# con listas de argumentos, no con `shell=True`.
- Sanitizar errores antes de presentarlos en TUI, consola o manifiestos.

## Contratos y calidad

- Periodos aceptados: `YYYY-Q1..Q4`, `YYYY-S1..S2` y `YYYY-A`.
- Un periodo incompleto genera un PDF marcado como parcial, no un fallo.
- Salida esperada: 13 páginas, 13.833 × 8 pulgadas y PDF/A-2b.
- El manifiesto debe registrar fuentes, meses encontrados, advertencias, hashes
  y versiones de herramientas, pero nunca secretos.
- Antes de cambiar el diseño, comparar contra el PDF de referencia de 2026-Q2.

## Estado conocido

La primera entrega fue solicitada sin builds, consultas live, renderizados ni
pruebas. No afirmar que esos pasos pasaron hasta ejecutarlos explícitamente.
