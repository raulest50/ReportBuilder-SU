#!/usr/bin/env bash
set -euo pipefail

project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$project_dir"

for command_name in uv quarto dotnet; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    echo "Falta '$command_name' en PATH. Consulte docs/SETUP.md." >&2
    exit 1
  fi
done

uv sync --frozen
dotnet build tools/sicom-olap-cli/src/WinSbi.Olap.Cli/WinSbi.Olap.Cli.csproj --configuration Release

if [[ ! -f .env ]]; then
  cp .env.example .env
  echo "Se creó .env. Complete sus variables SICOM antes de extraer datos."
fi

doctor_status=0
uv run su-report doctor || doctor_status=$?
if [[ "$doctor_status" -ne 0 ]]; then
  echo "El entorno quedó instalado, pero el diagnóstico tiene pendientes. Revise la tabla anterior."
fi
