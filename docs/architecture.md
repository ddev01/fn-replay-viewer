# Replay Viewer — how it works

A local Windows tray app that reads Fortnite `.replay` files from your Demos folder and shows
the match: who was in it, which team placed where, and a timeline of every knock and elimination.

It never talks to the internet except to check for its own updates, and it never writes to your
Demos folder — replays are opened read-only and copied to temp before parsing.

---

## 1. Process model

One process, one window handle (the tray icon), one loopback HTTP listener.

```
┌──────────────────────────── ReplayViewer.exe (tray, no main window) ───────────────────────────┐
│                                                                                                │
│  WinForms message loop (Application.Run)                                                       │
│    ├── NotifyIcon  ── right click → context menu: Open · Check for updates · Start with        │
│    │                  Windows · Exit                                                           │
│    ├── WinForms Timer ── one tick at 1 s (first update check), then every 6 hours              │
│    └── ContextMenuStrip                                                                        │
│                                                                                                │
│  HttpListener on http://127.0.0.1:47231/                                                       │
│    └── accept loop (async) → one Task per request → MatchRoutes                                │
│                                                                                                │
│  No FileSystemWatcher, no polling thread, no background workers when idle.                     │
└────────────────────────────────────────────────────────────────────────────────────────────────┘
                                                        ▲
                                                        │ HTTP (loopback only)
                                        ┌───────────────┴────────────────┐
                                        │  Browser tab, opened at launch │
                                        │  wwwroot: index.html/app.js/css│
                                        └────────────────────────────────┘
```

**Single instance.** `Program.Main` takes a named mutex `Local\ReplayViewer.SingleInstance`.
A second launch does not start a second copy — it opens the browser at the running instance and
exits. This also means the installer's `AppMutex` can close the app cleanly during an update.

**Why a browser UI?** It keeps the app one small WinForms shell with no layout code, and the UI
is plain HTML/CSS/JS that ships as content files.

---

## 2. Repository layout

| Path | What lives there |
|---|---|
| `src/ReplayViewer.Core/` | Everything that does not touch WinForms: replay parsing, match assembly, paths, version, install payload sync. Fully unit-tested. |
| `src/ReplayViewer/` | The Windows app: `Program`, tray, `HttpListener`, routes, updater, installer layout, logging. |
| `src/ReplayViewer/wwwroot/` | The UI. Hand-written, no bundler, no build step. |
| `src/ReplayViewer/types/match.d.ts` | The JSON contract, hand-maintained from the C# records. Outside `wwwroot` so it is never shipped. |
| `tests/ReplayViewer.Tests/` | xUnit v3. Core logic plus the app's HTTP layer (driven through a real `HttpListener`). |
| `tests/ui/` | Playwright + axe smoke tests against `wwwroot` with a stubbed API. |
| `installer/ReplayViewer.iss` | Inno Setup script. |
| `scripts/pack.ps1` | Publish + installer + zip. |
| `scripts/verify.ps1` | The same checks CI runs, one command. |
| `.github/workflows/` | `ci.yml`, `codeql.yml`, `release.yml`. |

---

## 3. From a `.replay` file to the screen

```
Demos folder
    │  1. pick the replay (newest stable, or the one you clicked)
    ▼
%TEMP%\ReplayViewer\<name>-<guid>.replay
    │  2. copy with FileShare.ReadWrite|Delete  (the game still has it open)
    ▼
FortniteReplayReader
    │  3. parse: ParseMode.Full, then Minimal; 60 s timeout
    ▼
ParsedReplaySnapshot
    │  4. group players into teams, resolve placement, count kills, tag fill
    ▼
MatchDocument  ──►  JSON over loopback  ──►  render
    │
    └─ 5. written to the cache (Brotli, ~6 KB) so reopening is instant
```

### 3.1 Choosing the replay

`LatestStableReplay.Pick` lists `*.replay`, filters to files that exist, orders by
`LastWriteTimeUtc` then name, and checks the newest is stable: it waits up to 5 s and requires
the size to stop changing for 3 s. If it is still growing, the caller gets a 409.

A replay you pick from the menu is resolved by `ReplayPaths.ResolveInside`, which requires the
path to be inside the Demos folder and to end in `.replay`.

### 3.2 Copying

`ReplayFileAccess.CopyWithReadWriteShare` copies to `%TEMP%` opening the source with
`FileShare.ReadWrite | FileShare.Delete`, because Fortnite keeps the file open while recording.
The copy is deleted in a `finally`; a failed delete is logged and ignored.

### 3.3 Parsing

`FortniteReplayProbe` runs the parse on a worker task with a 60 s timeout. It tries `ParseMode.Full`
first and falls back to `Minimal`, keeping the first result that has both players and events.

> The timeout ends the *request*, not the parse: the worker keeps running until the process exits.
> `FortniteReplayReader` has no cancellation path. Documented in `SECURITY.md`.

### 3.4 Assembling the match

`MatchAssembler.Build` turns the snapshot into what the UI consumes:

- players are grouped by `TeamIndex` (`null` becomes team `-1`);
- team placement comes from the replay's team data, falling back to the best player placement;
- **kills** come from the parser when it provides them, otherwise they are counted from
  `finish` events where the player is the actor;
- **isYou** is the player flagged `IsReplayOwner`, matched by epic id;
- **fill** is `fill` when a team has more parties than the team size allows, `unknown` when the
  party data is missing;
- names fall back to `Anonymous N` / `Anonymous` / `Unknown`.

`EventFixup.Clean` orders events by time and drops near-duplicates within 1.5 s (the same knock is
sometimes recorded twice).

### 3.5 Event kinds

| Kind | Meaning |
|---|---|
| `knock` | Downed another player |
| `finish` | Eliminated another player |
| `selfElim` | Died to the storm, fall, bleed-out or their own action |
| `revive` | Revived a teammate |

---

## 4. HTTP API

Everything is on `http://127.0.0.1:47231/`, loopback only, no authentication (see Security).

| Method | Path | Purpose |
|---|---|---|
| GET | `/` | The UI (`index.html`) |
| GET | `/*.js`, `*.css`, `*.svg`, `*.ico` | Static files from `wwwroot` |
| GET | `/api/config` | `{ nameFnPublicUrl }` — drives the NameFN button |
| GET | `/api/demos` | 20 most recent replays: name, path, size, last write |
| POST | `/api/scrape` | Body `{ "path": "..." }` (or `{}` for the newest). Returns the match. |
| GET | `/api/match` | The last match served by this process, or 404 |
| GET | `/api/match.json` | Same, as a download (`Content-Disposition: attachment`) |
| GET | `/api/update` | `{ available, staged, stagedVersion }` — drives the update banner |
| POST | `/api/update/apply` | Installs a staged update and restarts |

Status codes the UI relies on:

| Code | When |
|---|---|
| 400 | Invalid JSON body |
| 404 | No match yet, or no such file |
| 409 | A scrape is already running, or the newest replay is still being written |
| 413 | Body over 64 KB |
| 415 | `POST /api/scrape` without `application/json` |
| 422 | Replay is still live, or could not be read |
| 500 | Unexpected failure (logged with a stack trace) |
| 504 | Parse exceeded the 60 s timeout |

Every response carries `X-Content-Type-Options: nosniff` and
`Content-Security-Policy: default-src 'self'; img-src 'self' data:; object-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'`.
Static files are served with `Cache-Control: no-store`.

---

## 5. The UI

`wwwroot` is served as-is: `index.html`, `app.js` (~830 lines), `app.css`, and SVG icons. There is
no bundler and no build step, so what you edit is what ships.

- **On load** it reads `/api/match` and renders it; if that 404s it scrapes the newest replay.
  Reloading therefore stays on the same replay and does not re-parse.
- **The ☰ button** lists recent replays. Picking one scrapes it.
- **Squads** show `#4` (or `#?` when unplaced) and a red elim count; clicking one opens its
  timeline.
- **Tabs**: Game (every knock/finish), You (your events), Team (the selected squad).
  An empty list explains itself rather than showing a blank panel.
- **Search / Sort / Show bots** filter the roster client-side.

Because the page runs under `style-src 'self'`, icon URLs live in CSS classes
(`.icon-kill`, `.icon-external`, …) — an inline `style="--icon:…"` is blocked and renders a solid
block. The Playwright suite asserts the computed mask and the absence of CSP violations.

---

## 6. Cache

Parsing a 17-21 MB replay takes 0.5-2 s; the assembled match is ~49 KB of JSON, which Brotli takes
to ~6 KB.

- Location: `%LocalAppData%\ReplayViewer\cache\<key>.json.br`
- Key: SHA-256 of `format version | app version | parser version | full path | size | last-write ticks`
- 200 entries max, oldest pruned on write
- Any failure (missing, corrupt, locked) is a **miss**, never an error
- Invalidated automatically when the file changes, the app updates, or the parser updates

The cache is disposable: the installer prunes it on update, and a cold start simply re-parses.

---

## 7. Updater

1. **Check** at launch and every 6 hours: `GET api.github.com/repos/ddev01/fn-replay-viewer/releases/latest`.
2. If the tag is newer than the running assembly version, the tray item becomes **Update to x.y.z**
   and a balloon tip appears.
3. **Stage, silently.** The `.zip` asset and its detached `.sig` are downloaded, the signature is
   verified against a key embedded in the app, the GitHub digest is checked too, and the payload is
   extracted to `%TEMP%\ReplayViewerPending`. Nothing is installed and nothing restarts.
4. **Apply on the next launch.** `Program.Main` sees the staged payload, runs
   `ReplayViewer.exe --install` from it, and the installer relaunches the new version. Updating
   therefore never interrupts a session.
5. **Apply now** from the web banner (`POST /api/update/apply`) or the tray menu, which runs the
   same chain immediately.

### Signature

The release key is ECDSA P-256 and never leaves the maintainer's machine
(`~/.config/replay-viewer/release-key.pem`). `scripts/sign-release.sh` signs each artifact and
publishes the release; the public half is a constant in `ReleaseSignature.cs`.

A missing signature **fails closed**: an update without one is refused. That is the point — the
GitHub digest only proves the file matches what GitHub stores, so a compromised account could
publish a matching pair. The signature proves the maintainer produced it.

## 8. Install and uninstall

| | |
|---|---|
| Location | `%LocalAppData%\ReplayViewer` |
| Privileges | none — `PrivilegesRequired=lowest` |
| Start Menu | `Replay Viewer.lnk` |
| Autostart | optional; HKCU `Run` value or a scheduled logon task (a legacy task is migrated) |
| Data kept | `logs/`, `cache/` |
| Uninstall | Settings → Apps, or `ReplayViewer.exe --uninstall` |

---

## 9. Packaging and release

`scripts/pack.ps1` publishes and compiles the installer:

```
dotnet publish -c Release -r win-x64 --self-contained true
  -p:PublishSingleFile=true
  -p:IncludeNativeLibrariesForSelfExtract=true
  -p:EnableCompressionInSingleFile=false     ← measured; see Performance
```

then Inno Setup (LZMA2) produces `ReplayViewer-Setup-<version>.exe`, and the same payload is zipped
for the in-app updater.

**Compression is off on purpose.** With it on, the runtime decompresses the assemblies into memory
at startup: measured 152 MB working set versus 92 MB, and 546-644 ms to start versus 538 ms. The
installer is *smaller* without it (35.5 MB vs 46.8 MB) because LZMA2 compresses an uncompressed exe
far better than a pre-compressed one.

---

## 10. CI/CD

| Workflow | Trigger | What it does |
|---|---|---|
| `ci.yml` | every push, PRs to `develop` | locked restore → build `-warnaserror` → test → coverage gate → format → lint → typecheck → UI tests |
| `codeql.yml` | push/PR to `develop`, weekly | CodeQL C# on Windows, scoped to the shipped projects |
| `release.yml` | `v*` tag | test → install Inno (pinned) → pack → attest build provenance → upload the artifacts to the run |

All actions are pinned to commit SHAs and updated by Dependabot. Releases are **immutable** and
carry a Sigstore attestation (`gh attestation verify ReplayViewer-Setup-x.y.z.exe --repo …`).

The release job refuses to build when the tag does not match `Version` in `Directory.Build.props`.

**Publishing is a separate, manual step**: `scripts/sign-release.sh v1.2.3` downloads the run's
artifacts, signs them with the offline key, verifies the signatures, and creates the immutable
release with all four files. CI cannot publish a release on its own, by design — it has no key.

---

## 11. Security posture

- The listener binds `127.0.0.1` only and serves a fixed set of routes.
- Static files are confined to `wwwroot`: the path is resolved and must start with the root **plus
  a separator**, and any path containing `..` is refused.
- Replay paths must resolve inside the Demos folder and end in `.replay`.
- Request bodies are capped at 64 KB and must declare `application/json`.
- No authentication on the loopback API: any local process can read the current match. Accepted for
  a single-user desktop app; documented in `SECURITY.md`.
- The updater requires a detached ECDSA signature from the offline release key and fails closed
  without one; the GitHub digest is checked as well.

---

## 12. Performance (measured on a 16-core machine)

| State | CPU | Memory |
|---|---|---|
| Idle in the tray | **0.008 %** (0 ms per minute) | **83 MB** working set, ~13 MB private |
| Cached replay open | ~0 ms | — |
| Cold parse | ~1.2 cores for 0.5-2.5 s | peak ~395 MB, back to ~92 MB afterwards |
| After a parse | 0.000 % | heap trimmed: `GC.Collect` + LOH compaction |

No disk writes, no network and no thread activity while idle; the only scheduled work is the
6-hour update check.

---

## 13. Tests

| Suite | Count | Notes |
|---|---|---|
| `ReplayViewer.Tests` | 191 | Core logic, routes (real `HttpListener` on a free port), cache, updater parsing, installer payload sync |
| `tests/ui` (Playwright) | 10 | Rendering, icon masking under the real CSP, empty states, no serious axe violations |

Coverage gate: 63 % line / 61 % branch, measured in CI. Core alone sits at ~96 %; the tray,
installer and startup code is deliberately untested because exercising it means mutating the Task
Scheduler and the registry.

Two tests need a real replay in the Demos folder and skip on CI.

---

## 14. Known limitations

- **Unsigned.** SmartScreen warns on first install; that is expected.
- **No owner recorded.** Some replays never set `IsReplayOwner` (typically ones Fortnite did not
  finalise). They open fine, but the You/Team tabs explain that the player cannot be identified.
- **The parse timeout does not stop the parse** — the thread runs until the process exits.
- **No history.** One match in memory at a time; the cache is per-replay, not a database.
- **Fortnite format drift** is inherited from `FortniteReplayReader`; a game update can break
  parsing until that package updates.
- **Auto-update requires a click.** By design.

---

## 15. Working on it

```powershell
# everything CI runs
pwsh -File scripts/verify.ps1

# release artifacts
pwsh -File scripts/pack.ps1
```

Requires the .NET SDK pinned in `global.json`, Inno Setup 6 for the installer, and Node only for the
frontend checks (`npm ci`). `global.json` and `packages.lock.json` keep restore reproducible;
`.editorconfig` (CRLF, 4 spaces, Allman) drives `dotnet format`.

Commits go to `develop`; a release is a `v*` tag whose version matches `Directory.Build.props`.
