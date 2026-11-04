# Runs every test and check. Stops at the first failure. Exit code 0 = all passed.
# Run from anywhere:  .\test.ps1   There is no need to activate the virtual environment.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$python = Join-Path $PSScriptRoot '.venv\Scripts\python.exe'
if (-not (Test-Path $python)) {
    Write-Host 'FAILED: no .venv folder. Follow the Install section in README.md first.'
    exit 1
}

function Step($name, [scriptblock]$command) {
    Write-Host "== $name"
    & $command
    if ($LASTEXITCODE -ne 0) { Write-Host "FAILED: $name"; exit 1 }
}

Step 'pytest'        { & $python -m pytest }
Step 'ruff check'    { & $python -m ruff check . }
Step 'ruff format'   { & $python -m ruff format --check . }
Step 'dotnet test'   { dotnet test }
Step 'dotnet format' { dotnet format --verify-no-changes }

Write-Host 'All tests passed'
