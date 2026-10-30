# Replay Viewer

Local Windows tray app that reads Fortnite demo files from your Demos folder and shows roster, timeline, and optional skin icons.

Releases: https://github.com/ddev01/fn-replay-viewer/releases

## Install

1. Download the latest zip from Releases.
2. Unzip and run `ReplayViewer.exe`.
3. If Windows SmartScreen appears, choose **More info** then **Run anyway**. This build is unsigned (a paid code-signing cert is the only way to drop that prompt).
4. The app copies itself to `%LocalAppData%\ReplayViewer`, adds a Start Menu shortcut, and starts at sign-in.

`--install` does the copy and shortcut. `--uninstall` removes the logon task and Start Menu shortcut (it leaves app data).

## Use

The page opens on loopback. **Scrape** reads the newest finished replay. **Demos** lists recent files. Right-click the tray icon for Open, Update, and Exit. Updates are checked once at launch against GitHub Releases.

## Build

```bash
dotnet publish src/ReplayViewer/ReplayViewer.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish
```

Zip `artifacts/publish` and attach it to a GitHub release whose tag matches the version in `Directory.Build.props` (for example `v1.0.1`).

NameFN lookup is optional. Copy `Directory.Build.local.props.example` to `Directory.Build.local.props` and set `NameFnPublicUrl` to the public origin (no trailing slash). Rebuild so the assembly metadata is baked in. Leave it unset to hide the NameFN button.
