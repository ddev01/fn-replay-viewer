# Replay Viewer — project info

A local Windows tray app that reads Fortnite `.replay` demo files from your Demos folder and
shows the match: roster, teams, placement, and a timeline of every knock, elimination, revive
and self-elimination. Built as a single small WinForms shell that serves a hand-written web UI
over loopback.

- **Repo:** https://github.com/ddev01/fn-replay-viewer
- **Releases:** https://github.com/ddev01/fn-replay-viewer/releases
- **Version:** 1.0.12 (`Directory.Build.props`)
- **Status:** hobby project, unsigned builds, latest release only supported
- **Deep dive:** [docs/architecture.md](docs/architecture.md)

---

## What it does

- Watches `%LocalAppData%\FortniteGame\Saved\Demos` for `.replay` files.
- Picks the newest stable replay automatically, or one you choose from the ☰ list.
- Parses it, assembles the match (players, teams, placement, kills, fill/bot flags), and serves
  it to a browser tab on `http://127.0.0.1:47231/`.
- Caches each assembled match as Brotli JSON so reopening is instant and still works after
  Fortnite deletes the replay file.
- Checks for its own updates at launch and every six hours; installs a verified update on the
  next start (or on demand).
- Lives entirely in the tray: **Open**, **Check for updates**, **Start with Windows**, **Exit**.

It never writes to the Demos folder. Replays are opened read-only and copied to `%TEMP%` before
parsing. The only network traffic is the GitHub update check.

---

## How it works at a glance

```
Demos folder
  → copy to %TEMP% (FileShare.ReadWrite|Delete)
  → FortniteReplayReader (ParseMode.Full, then Minimal; 60 s request timeout)
  → MatchAssembler (teams, placement, kills, isYou, fill)
  → MatchDocument JSON over loopback
  → wwwroot UI in a browser tab
  ↗ cached as Brotli (~6 KB) under %LocalAppData%\ReplayViewer\cache
```

One process, one tray icon, one loopback `HttpListener`, no background polling when idle.
A named mutex (`Local\ReplayViewer.SingleInstance`) makes a second launch open the browser at the
running instance instead of starting a copy.

CLI entry points:

| Flag | Effect |
|---|---|
| `--install` | Copy the current payload over `%LocalAppData%\ReplayViewer` (used by updates) |
| `--uninstall` | Remove startup registration and Start Menu shortcut |

---

## Tech stack

| Layer | Choice |
|---|---|
| Runtime | .NET 10, `net10.0-windows`, self-contained `win-x64` single-file publish |
| Desktop shell | WinForms (tray icon, timers, context menu) |
| HTTP | `System.Net.HttpListener` on `127.0.0.1:47231`, one `Task` per request |
| Replay parsing | [`FortniteReplayReader`](https://www.nuget.org/packages/FortniteReplayReader) 3.1.0 |
| UI | Plain HTML/CSS/JS in `wwwroot` — no bundler, no build step |
| Tests | xUnit v3, Playwright + axe |
| Frontend checks | ESLint, Stylelint, Prettier, `tsc` |
| Installer | Inno Setup 6 (`installer/ReplayViewer.iss`) |
| CI | GitHub Actions, CodeQL, Dependabot |

---

## Repository layout

| Path | Contents |
|---|---|
| `src/ReplayViewer.Core/` | UI-free logic: parsing probe, match assembly, paths, cache index, payload sync |
| `src/ReplayViewer/` | Windows app: tray, listener, routes, updater, installer layout, logging |
| `src/ReplayViewer/wwwroot/` | The UI (`index.html`, `app.js`, `app.css`, SVG icons) |
| `src/ReplayViewer/types/match.d.ts` | Hand-maintained JSON contract for the UI |
| `tests/ReplayViewer.Tests/` | xUnit v3 — core logic plus the real loopback HTTP layer |
| `tests/ui/` | Playwright smoke and accessibility tests with a stubbed API |
| `installer/ReplayViewer.iss` | Inno Setup script |
| `scripts/verify.ps1` | Every check CI runs, in one command |
| `scripts/pack.ps1` | Publish, build installer, zip updater payload |
| `scripts/sign-release.sh` | Offline signing and publishing (maintainer only) |
| `.github/workflows/` | `ci.yml`, `codeql.yml`, `release.yml` |
| `docs/architecture.md` | Detailed design, API, cache, updater and performance notes |

---

## HTTP API (loopback only)

| Method | Path | Purpose |
|---|---|---|
| GET | `/` | The UI |
| GET | `/*.js`, `*.css`, `*.svg`, `*.ico` | Static files from `wwwroot` |
| GET | `/api/config` | `{ nameFnPublicUrl }` for the optional NameFN lookup |
| GET | `/api/demos` | Replays on disk plus cached matches whose file is gone |
| POST | `/api/scrape` | `{ "path": "..." }` or `{}` for the newest replay |
| GET | `/api/match` | Last match served by this process, or 404 |
| GET | `/api/match.json` | Same, as a download |
| GET | `/api/update` | `{ available, staged, stagedVersion }` |
| POST | `/api/update/apply` | Install a staged update and restart |

Notable status codes: `409` scrape already running or replay still being written, `415` body is
not `application/json`, `422` replay unreadable/live, `504` parse exceeded 60 s.

Every response sets `X-Content-Type-Options: nosniff` and a strict CSP
(`default-src 'self'`); static files are `Cache-Control: no-store`.

---

## Where data lives

| Item | Location |
|---|---|
| Replay source | `%LocalAppData%\FortniteGame\Saved\Demos` |
| Install | `%LocalAppData%\ReplayViewer` (no Admin rights) |
| Logs | `%LocalAppData%\ReplayViewer\logs` |
| Match cache | `%LocalAppData%\ReplayViewer\cache\<key>.json.br` (max 200 entries) |
| Temp parse copy | `%TEMP%\ReplayViewer\<name>-<guid>.replay` |
| Staged update | `%TEMP%\ReplayViewerPending` |

The cache key covers format version, app version, parser version, full path, size and last-write
ticks, so it invalidates itself on change. Any cache failure is a miss, never an error.

---

## Build and verify

Prerequisites: [.NET SDK 10.0.401](https://dotnet.microsoft.com/download) (pinned in
`global.json`), [Inno Setup 6](https://jrsoftware.org/isinfo.php) for the installer, and Node
only for frontend checks (`npm ci`).

```powershell
pwsh -File scripts/verify.ps1     # locked restore, build, test, format, lint, typecheck, UI tests
pwsh -File scripts/pack.ps1       # release artifacts
```

`verify.ps1` flags: `-Pack`, `-SkipFrontend`. The coverage gate always runs.
Pack writes:

- `artifacts/installer/ReplayViewer-Setup-<version>.exe`
- `artifacts/ReplayViewer-win-x64-<version>.zip`

Released builds are self-contained single-file, with `EnableCompressionInSingleFile=false`
(measured: smaller installer, lower memory, faster start than compressed).

---

## Release flow

1. Bump `Version` in `Directory.Build.props`, commit to `develop`.
2. Push a matching `v*` tag. CI runs tests, packs, attests provenance, uploads artifacts.
3. The maintainer runs `scripts/sign-release.sh vX.Y.Z` offline: it downloads the artifacts,
   signs them with the ECDSA P-256 release key, verifies the signatures, and publishes an
   immutable release with the installer, zip, and detached signatures.

CI cannot publish on its own — it has no signing key. The app refuses any update lacking a valid
detached signature, so a compromised GitHub account cannot push a malicious build.

---

## Testing

| Suite | What it covers |
|---|---|
| `ReplayViewer.Tests` | Parsing, match assembly, event fixup, paths, cache, routes over a real listener, update parsing, installer payload sync |
| `tests/ui` (Playwright) | Rendering, icon masking under the real CSP, empty states, no serious axe violations |

Coverage gate in CI: 63 % line / 61 % branch. Core alone is ~96 %. Tray, installer and startup
code are deliberately untested (they would mutate the Task Scheduler and registry). Two tests
need a real replay in the Demos folder and skip on CI.

---

## Configuration

The optional NameFN lookup button uses `https://named.peltux.xyz` by default.

- Local override: copy `Directory.Build.local.props.example` to
  `Directory.Build.local.props` and set `NameFnPublicUrl`.
- CI override: set the `NAME_FN_PUBLIC_URL` repository secret.
- An empty value hides the NameFN button.

---

## Security posture and known limits

- Listener is bound to `127.0.0.1` with a fixed route set; static paths are confined to
  `wwwroot` and replay paths must resolve inside the Demos folder.
- **No authentication on loopback** — any local process can read the current match. Accepted for
  a single-user desktop app; documented in `SECURITY.md`.
- Replay files are untrusted input parsed in-process. The 60 s timeout ends the request, not the
  parse; a crafted file can hold one core busy.
- Builds are **unsigned**: SmartScreen warns on first install.
- Some replays never set `IsReplayOwner`; those open fine but the You/Team tabs cannot identify
  you.
- One match in memory at a time; no history beyond the per-replay cache.
- Fortnite format drift can break parsing until `FortniteReplayReader` updates.
- Auto-update requires a click by design.

Report vulnerabilities privately via GitHub Security Advisories — see [SECURITY.md](SECURITY.md).

---

## Conventions

- CRLF, 4-space indent, Allman braces (`.editorconfig` drives `dotnet format`).
- Warnings are errors; `AnalysisMode=Recommended`; nullable enabled.
- Restore is reproducible: `global.json` + `packages.lock.json` + `--locked-mode`.
- Commits go to `develop`; releases are `v*` tags matching `Directory.Build.props`.
