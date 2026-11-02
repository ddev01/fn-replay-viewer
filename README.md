# Replay Viewer

Local Windows tray app that reads Fortnite demo files from your Demos folder and shows roster and timeline.

Releases: https://github.com/ddev01/fn-replay-viewer/releases

How it works, in detail: [docs/architecture.md](docs/architecture.md)

## Install

1. Download **ReplayViewer-Setup-*.exe** from [Releases](https://github.com/ddev01/fn-replay-viewer/releases).
2. Run the installer. It does not need Administrator rights.
3. If Windows SmartScreen appears, choose **More info** then **Run anyway**. This build is unsigned; signing it (for example with Microsoft's Azure Artifact Signing, formerly Trusted Signing — a subscription of about $10/month, no hardware token) would show a publisher name, but SmartScreen still warns until a reputation builds up.
4. Leave **Launch Replay Viewer** checked on the last page.

That copies the app to `%LocalAppData%\ReplayViewer` and adds a Start Menu shortcut. The installer also offers **Start Replay Viewer when I sign in**, which you can change later from the tray icon.

Uninstall from **Settings → Apps** (or Apps & features). App data such as logs is left in `%LocalAppData%\ReplayViewer`.

The zip on the release is only for the in-app updater. You do not need to unzip it yourself.

## Use

The page opens on loopback and shows your most recent finished replay by itself; reloading keeps you on the same one. The **☰** button beside the search box lists recent replays — picking one opens it, and a replay that has been opened before comes back instantly. Right-click the tray icon for **Open**, **Check for updates**, **Start with Windows**, and **Exit**.

Updates are checked at launch and every six hours. When a newer release exists the menu item turns into **Update to x.y.z** and a notification appears; click it to download, verify, and restart into the new version. Nothing is installed without that click.

## Build

Install [.NET SDK 10.0.401](https://dotnet.microsoft.com/download) (`global.json` pins it, and restore uses the committed `packages.lock.json` files), plus [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`choco install innosetup`) to build the installer. Node is only needed for the frontend checks: if `package.json` is present, run `npm ci` first.

`scripts/verify.ps1` runs every check CI runs — locked restore, build, tests, format check, and the frontend lint/typecheck/UI tests when `package.json` is present:

```powershell
pwsh -File scripts/verify.ps1
```

To build the release artifacts:

```powershell
pwsh -File scripts/pack.ps1
```

That writes:

- `artifacts/installer/ReplayViewer-Setup-<version>.exe`
- `artifacts/ReplayViewer-win-x64-<version>.zip`

Push a matching git tag (for example `v1.0.6`) to have GitHub Actions attach both files to the release.

NameFN lookup uses `https://named.peltux.xyz` by default (same as 1.0.2). Override it by copying `Directory.Build.local.props.example` to `Directory.Build.local.props` and setting `NameFnPublicUrl`, or by setting the `NAME_FN_PUBLIC_URL` repository secret for CI. An empty override hides the NameFN button.
