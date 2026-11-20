# Runs the same checks CI runs. Invoke from any checkout; it locates the repo root itself.
param(
    [switch]$SkipFrontend,
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

$frontend = $null
if (-not $SkipFrontend -and (Test-Path (Join-Path $root "package.json"))) {
    $frontend = Start-Job -ArgumentList $root -ScriptBlock {
        param($root)
        Set-Location $root
        $ErrorActionPreference = "Stop"
        # npm ci wipes node_modules, so skip it while the lockfile is unchanged since the last install.
        $stamp = Join-Path $root "node_modules\.lock-hash"
        $hash = (Get-FileHash package-lock.json).Hash
        if (-not (Test-Path $stamp) -or (Get-Content $stamp -Raw).Trim() -ne $hash) {
            Write-Host "==> npm ci" -ForegroundColor Cyan
            npm ci
            if ($LASTEXITCODE -ne 0) { throw "npm ci failed (exit $LASTEXITCODE)" }
            Set-Content -Path $stamp -Value $hash
        }
        foreach ($cmd in @(
                "npx --no-install playwright install chromium",
                "npm run lint",
                "npm run typecheck",
                "npm run test:unit",
                "npm run test:ui")) {
            Write-Host "==> $cmd" -ForegroundColor Cyan
            Invoke-Expression $cmd
            if ($LASTEXITCODE -ne 0) { throw "$cmd failed (exit $LASTEXITCODE)" }
        }
    }
}

Step "restore (locked)" { dotnet restore ReplayViewer.sln --locked-mode }
Step "build" { dotnet build ReplayViewer.sln -c Release --nologo -warnaserror --no-restore }
# Tests run once, with coverage, and the gate always applies: CI enforces it, so a local run that
# skipped it could pass here and fail there.
Step "test" {
    Remove-Item -Recurse -Force artifacts/coverage -ErrorAction SilentlyContinue
    dotnet test ReplayViewer.sln -c Release --nologo --no-build --blame-hang-timeout 5m --collect:"XPlat Code Coverage" --results-directory artifacts/coverage
}
# Floor is set from a clean CI run (65.0% line / 63.2% branch), two points down.
# A local run reads higher: the two tests in FortniteReplayProbeTests need a real .replay in the
# Demos folder and skip on CI, which accounts for the difference.
# Each threshold is its own argument, with no leading dash: "-key" is silently ignored,
# and "lineCoverage=60;branchCoverage=60" is read as one int and fails to parse.
Step "coverage gate" {
    dotnet tool restore
    dotnet tool run reportgenerator "-reports:artifacts/coverage/**/coverage.cobertura.xml" "-targetdir:artifacts/coverage/report" "-reporttypes:TextSummary" "minimumCoverageThresholds:lineCoverage=63" "minimumCoverageThresholds:branchCoverage=61"
    Get-Content artifacts/coverage/report/Summary.txt
}
Step "format" { dotnet format ReplayViewer.sln --verify-no-changes --no-restore }

if ($frontend) {
    Write-Host "==> frontend (ran alongside the .NET steps)" -ForegroundColor Cyan
    Receive-Job $frontend -Wait -AutoRemoveJob
    $frontend = $null
}

if ($Pack) {
    Step "pack installer" { pwsh -File (Join-Path $root "scripts/pack.ps1") }
}

Write-Host "verify: OK" -ForegroundColor Green
}
finally {
    if ($frontend) { Remove-Job $frontend -Force }
    $mutex.ReleaseMutex()
    $mutex.Dispose()
}
