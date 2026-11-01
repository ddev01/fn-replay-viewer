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

# One verify run at a time: agents share this checkout and would collide on obj/ and on the branch switch.
$mutex = New-Object System.Threading.Mutex($false, "Global\ReplayViewerVerify")
if (-not $mutex.WaitOne([TimeSpan]::FromMinutes(40))) {
    throw "another verify run is already in progress"
}
try {

function Step([string]$name, [scriptblock]$body) {
    Write-Host "==> $name" -ForegroundColor Cyan
    & $body
    if ($LASTEXITCODE -ne 0) { throw "$name failed (exit $LASTEXITCODE)" }
}

if ($Branch) {
    Write-Host "==> checkout $Branch" -ForegroundColor Cyan
    git fetch origin --prune
    if ($LASTEXITCODE -ne 0) { throw "git fetch failed" }
    # This clone is a disposable verification target: drop stray edits and untracked
    # files so a branch that adds a file cannot abort the switch. Ignored paths
    # (bin/obj/node_modules/artifacts) are left alone.
    git clean -fdq
    git checkout -f -B $Branch "origin/$Branch"
    if ($LASTEXITCODE -ne 0) { throw "git checkout $Branch failed" }
}

Step "restore (locked)" { dotnet restore ReplayViewer.sln --locked-mode }
Step "build" { dotnet build ReplayViewer.sln -c Release --nologo -warnaserror --no-restore }
Step "test" { dotnet test ReplayViewer.sln -c Release --nologo --no-build }
Step "format" { dotnet format ReplayViewer.sln --verify-no-changes --no-restore }

if ($Coverage) {
    Step "coverage" {
        Remove-Item -Recurse -Force artifacts/coverage -ErrorAction SilentlyContinue
        dotnet test ReplayViewer.sln -c Release --nologo --no-build --collect:"XPlat Code Coverage" --results-directory artifacts/coverage
    }
    # Gate on the measured baseline (62.3% line / 63.8% branch) minus ~2 points. It is not
    # near 95% because the tray, installer and startup code is deliberately untested.
    # Each threshold is its own argument, with no leading dash: "-key" is silently ignored,
    # and "lineCoverage=60;branchCoverage=60" is read as one int and fails to parse.
    Step "coverage gate" {
        dotnet tool restore
        dotnet tool run reportgenerator "-reports:artifacts/coverage/**/coverage.cobertura.xml" "-targetdir:artifacts/coverage/report" "-reporttypes:TextSummary" "minimumCoverageThresholds:lineCoverage=60" "minimumCoverageThresholds:branchCoverage=60"
        Get-Content artifacts/coverage/report/Summary.txt
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
}
finally {
    $mutex.ReleaseMutex()
    $mutex.Dispose()
}
