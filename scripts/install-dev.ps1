# Builds the working tree and installs it over the local Replay Viewer, tagged as a dev build
# (1.0.x-dev.<timestamp>) so it is never mistaken for a release. The cache and logs are kept.
# Stops the running app first, installs, relaunches it, and checks the new build answers.
$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $root

$project = Join-Path $root "src\ReplayViewer\ReplayViewer.csproj"
$outDir = Join-Path $root "artifacts\dev-publish"
$base = (dotnet msbuild $project -getProperty:Version -nologo).Trim()
$tag = "$base-dev.$(Get-Date -Format 'yyyyMMddHHmm')"

if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

# Publish restores an unlocked graph that rewrites packages.lock.json; put the locks back.
$locks = @{}
Get-ChildItem -Path $root -Recurse -Filter packages.lock.json -File |
    ForEach-Object { $locks[$_.FullName] = [System.IO.File]::ReadAllText($_.FullName) }

dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=false -p:DebugType=None -p:DebugSymbols=false `
    "-p:InformationalVersion=$tag" -o $outDir --nologo -v:q
$publishExit = $LASTEXITCODE
foreach ($path in $locks.Keys) { [System.IO.File]::WriteAllText($path, $locks[$path]) }
if ($publishExit -ne 0) { throw "dotnet publish failed" }

Copy-Item (Join-Path $root "assets\app.ico") (Join-Path $outDir "app.ico") -Force

# The parse child processes run from the same exe, so stop every copy before replacing it.
Get-Process ReplayViewer -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

# --install copies the payload over the install directory and --relaunch starts the installed exe.
$payload = Join-Path $outDir "ReplayViewer.exe"
# Not -Wait: that would also wait for the relaunched app, which never exits.
$install = Start-Process $payload -ArgumentList "--install", "--relaunch" -PassThru
$install.WaitForExit()
if ($install.ExitCode -ne 0) { throw "install exited with $($install.ExitCode); see the log in %LOCALAPPDATA%\ReplayViewer\logs" }

for ($i = 0; $i -lt 20; $i++) {
    try {
        $config = Invoke-RestMethod "http://127.0.0.1:47231/api/config" -TimeoutSec 2
        if ($config.version -eq $tag) {
            Write-Host "Installed $tag (dev=$($config.dev))"
            return
        }
    } catch { }
    Start-Sleep -Milliseconds 500
}
throw "The installed app did not report $tag"
