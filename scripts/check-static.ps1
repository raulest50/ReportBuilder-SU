$ErrorActionPreference = "Stop"

Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    uv run ruff check .
    if ($LASTEXITCODE -ne 0) {
        throw "Ruff encontró errores."
    }
    uv run mypy src
    if ($LASTEXITCODE -ne 0) {
        throw "mypy encontró errores."
    }
}
finally {
    Pop-Location
}
