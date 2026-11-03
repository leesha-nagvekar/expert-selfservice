# Runs every test and check. Stops at the first failure. Exit code 0 = all passed.
# Run from the repo root with the virtual environment activated:  .\test.ps1
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Step($name, [scriptblock]$command) {
    Write-Host "== $name"
    & $command
    if ($LASTEXITCODE -ne 0) { Write-Host "FAILED: $name"; exit 1 }
}

Step 'pytest'       { python -m pytest }
Step 'ruff check'   { python -m ruff check . }
Step 'ruff format'  { python -m ruff format --check . }
# The skeleton step adds: dotnet test, dotnet format --verify-no-changes

Write-Host 'All tests passed'
