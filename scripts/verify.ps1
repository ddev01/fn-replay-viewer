# Runs the same checks CI runs. Invoke from any checkout; it locates the repo root itself.
param(
    [string]$Branch = "",
    [switch]$SkipFrontend,
    [switch]$Coverage,
    [switch]$Pack
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

function Step([string]$name, [scriptblock]$body) {
    Write-Host "==> $name" -ForegroundColor Cyan
    & $body
    if ($LASTEXITCODE -ne 0) { throw "$name failed (exit $LASTEXITCODE)" }
}

if ($Branch) {
    Write-Host "==> checkout $Branch" -ForegroundColor Cyan
    git fetch origin --prune
    if ($LASTEXITCODE -ne 0) { throw "git fetch failed" }
    git checkout -B $Branch "origin/$Branch"
    if ($LASTEXITCODE -ne 0) { throw "git checkout $Branch failed" }
}

Step "restore (locked)" { dotnet restore ReplayViewer.sln --locked-mode }
Step "build" { dotnet build ReplayViewer.sln -c Release --nologo -warnaserror --no-restore }
Step "test" { dotnet test ReplayViewer.sln -c Release --nologo --no-build }
Step "format" { dotnet format ReplayViewer.sln --verify-no-changes --no-restore }

if ($Coverage) {
    Step "coverage" {
        dotnet test ReplayViewer.sln -c Release --nologo --no-build --collect:"XPlat Code Coverage" --results-directory artifacts/coverage
    }
}

$hasFrontend = -not $SkipFrontend -and (Test-Path (Join-Path $root "package.json"))
if ($hasFrontend) {
    Step "npm ci" { npm ci }
    Step "playwright browsers" { npx --no-install playwright install chromium }
    Step "lint" { npm run lint }
    Step "typecheck" { npm run typecheck }
    Step "ui tests" { npm run test:ui }
}

if ($Pack) {
    Step "pack installer" { pwsh -File (Join-Path $root "scripts/pack.ps1") }
}

Write-Host "verify: OK" -ForegroundColor Green
