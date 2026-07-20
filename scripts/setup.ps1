$ErrorActionPreference = "Stop"

Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    foreach ($CommandName in @("uv", "quarto", "dotnet")) {
        if (-not (Get-Command $CommandName -ErrorAction SilentlyContinue)) {
            throw "Falta '$CommandName' en PATH. Consulte docs/SETUP.md."
        }
    }

    uv sync --frozen
    if ($LASTEXITCODE -ne 0) {
        throw "uv sync no pudo reconstruir el entorno."
    }
    dotnet build tools/sicom-olap-cli/src/WinSbi.Olap.Cli/WinSbi.Olap.Cli.csproj --configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build no pudo compilar el motor OLAP."
    }

    if (-not (Test-Path .env)) {
        Copy-Item .env.example .env
        Write-Host "Se creó .env. Complete sus variables SICOM antes de extraer datos."
    }

    uv run su-report doctor
    $DoctorStatus = $LASTEXITCODE
    if ($DoctorStatus -ne 0) {
        Write-Warning "El entorno quedó instalado, pero el diagnóstico tiene pendientes. Revise la tabla anterior."
    }
}
finally {
    Pop-Location
}
