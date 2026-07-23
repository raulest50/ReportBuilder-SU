# Configuración inicial

## Qué administra cada herramienta

- `uv` instala Python 3.12, crea `.venv` y reconstruye las dependencias desde
  `uv.lock`.
- Quarto es una aplicación independiente. Su distribución incluye el
  compilador Typst usado para ensamblar el PDF.
- .NET SDK 10 compila y ejecuta el motor XMLA incorporado en
  `tools/sicom-olap-cli`.

## Debian y Kali Linux

1. Instale `uv`, Quarto y .NET SDK 10 desde sus distribuciones oficiales.
2. Copie `.env.example` como `.env` y complete las cuatro variables SICOM.
3. Instale Segoe UI desde una instalación de Windows para la que tenga licencia
   en `.local/fonts/`, o defina `SU_REPORT_FONT_DIR` apuntando a esa carpeta.
4. Ejecute:

```bash
bash scripts/setup.sh
uv run su-report doctor
uv run su-report
```

Los archivos mínimos de Segoe UI para el diseño actual son `segoeui.ttf` y
`segoeuib.ttf`. No se publican en Git.

## Windows PowerShell

1. Instale `uv`, Quarto y .NET SDK 10.
2. Copie `.env.example` como `.env` y complete las variables.
3. Ejecute:

```powershell
.\scripts\setup.ps1
uv run su-report doctor
uv run su-report
```

La aplicación detecta automáticamente `C:\Windows\Fonts`.
También detecta Quarto en `PATH`, `C:\Program Files\Quarto\bin` y la carpeta
local de programas del usuario.

## Credenciales

Claves esperadas:

```text
USER_CUBO_SICOM
PASSWORD_CUBO_SICOM
CUBO_LIQS_URL
CUBO_AGENTS_URL
```

El orden de resolución del motor C# conserva el comportamiento original:
opciones, variables de entorno, `.env` y secretos de usuario de .NET. El
orquestador no pasa usuario o contraseña por la línea de comandos.

## Primera generación

```bash
uv run su-report generate --period 2026-Q2
```

El flujo obtiene Datos Abiertos, ejecuta los reportes C# de SICOM, normaliza los
CSV, genera mapas/gráficos, crea 13 SVG y ensambla el PDF/A-2b con Quarto/Typst.

Si una fuente no contiene todos los meses, el informe se produce como borrador
parcial y la condición queda registrada en portada y `manifest.json`.

La extracción también crea el histórico mensual de mayoristas desde enero de
2011 hasta el último mes solicitado. `render` no consulta SICOM: requiere el
CSV y el manifiesto histórico local, valida los 186 meses para `2026-Q2` y
genera la nueva página 5 vectorial antes del ensamblaje con Quarto/Typst.

## Estado de la entrega inicial

Por solicitud del propietario, la construcción inicial del repositorio se
realizó mediante análisis estático. Antes de una publicación formal deben
ejecutarse manualmente los comandos documentados en `AGENTS.md` para build,
pruebas y comparación visual.
