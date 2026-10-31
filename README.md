# Replay Viewer

Local Windows tray app that reads Fortnite demo files from your Demos folder and shows roster and timeline.

Releases: https://github.com/ddev01/fn-replay-viewer/releases

## Install

1. Download **ReplayViewer-Setup-*.exe** from [Releases](https://github.com/ddev01/fn-replay-viewer/releases).
2. Run the installer. It does not need Administrator rights.
3. If Windows SmartScreen appears, choose **More info** then **Run anyway**. This build is unsigned (a paid code-signing cert is the only way to drop that prompt).
4. Leave **Launch Replay Viewer** checked on the last page.

That copies the app to `%LocalAppData%\ReplayViewer`, adds a Start Menu shortcut, and starts it when you sign in.

Uninstall from **Settings → Apps** (or Apps & features). App data such as logs and cached skins is left in `%LocalAppData%\ReplayViewer`.

The zip on the release is only for the in-app updater. You do not need to unzip it yourself.

## Use

The page opens on loopback. **Scrape** reads the newest finished replay. **Demos** lists recent files. Right-click the tray icon for Open, Update, and Exit. Updates are checked once at launch against GitHub Releases.

## Build

Install [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`choco install innosetup`), then:

```powershell
pwsh -File scripts/pack.ps1
```

That writes:

- `artifacts/installer/ReplayViewer-Setup-<version>.exe`
- `artifacts/ReplayViewer-win-x64-<version>.zip`

Push a matching git tag (for example `v1.0.3`) to have GitHub Actions attach both files to the release.

NameFN lookup is optional. Copy `Directory.Build.local.props.example` to `Directory.Build.local.props` and set `NameFnPublicUrl` to the public origin (no trailing slash). Rebuild so the assembly metadata is baked in. Leave it unset to hide the NameFN button. For CI, set the `NAME_FN_PUBLIC_URL` repository secret.
