# Publishes a self-contained Windows build and compiles the installer.
$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $root

$project = Join-Path $root "src\ReplayViewer\ReplayViewer.csproj"
$publishDir = Join-Path $root "artifacts\publish"
$installerDir = Join-Path $root "artifacts\installer"
$iss = Join-Path $root "installer\ReplayViewer.iss"

$version = (dotnet msbuild $project -getProperty:Version -nologo).Trim()
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Could not read Version from $project"
}

Write-Host "Packing Replay Viewer $version"

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDir, $installerDir -Force | Out-Null

$publishArgs = @(
    "publish", $project,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    # Publish adds SDK-derived packages the lock file cannot cover; without this the
    # publish restore rewrites packages.lock.json and breaks the next locked restore.
    "-p:RestorePackagesWithLockFile=false",
    "-o", $publishDir
)

if (-not [string]::IsNullOrWhiteSpace($env:NameFnPublicUrl)) {
    $publishArgs += "-p:NameFnPublicUrl=$($env:NameFnPublicUrl)"
}

dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed"
}

Get-ChildItem $publishDir -Recurse -Include *.pdb | Remove-Item -Force
Copy-Item (Join-Path $root "assets\app.ico") (Join-Path $publishDir "app.ico") -Force

$isccCandidates = @(
    (Join-Path $root "tools\innosetup\ISCC.exe"),
    (Join-Path ${env:LocalAppData} "Programs\Inno Setup 6\ISCC.exe"),
    (Join-Path ${env:LocalAppData} "Programs\Inno Setup 7\ISCC.exe"),
    (Join-Path ${env:ProgramFiles} "Inno Setup 7\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 7\ISCC.exe")
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "Inno Setup is required. Install it with: choco install innosetup"
}

& $iscc "/DMyAppVersion=$version" $iss
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compile failed"
}

$zipPath = Join-Path $root "artifacts\ReplayViewer-win-x64-$version.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath

$setupPath = Join-Path $installerDir "ReplayViewer-Setup-$version.exe"
Write-Host "Installer: $setupPath"
Write-Host "Zip:       $zipPath"
