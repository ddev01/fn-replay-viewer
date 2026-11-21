# Replay Viewer

Windows tray app (.NET 10, WinForms shell) that reads Fortnite `.replay` files and serves a match UI
over loopback (`http://127.0.0.1:47231/`). UI is plain HTML/CSS/JS in `wwwroot`: no bundler, no build step.
Branch `develop` is the main branch. Commits are conventional: `type(scope): subject`.

## Layout
- `src/ReplayViewer.Core/`: UI-free logic (parsing, match assembly, paths, cache).
- `src/ReplayViewer/`: tray, listener, routes, updater. `wwwroot/` is the UI; `types/match.d.ts` is the hand-kept JSON contract for it.
- `tests/ReplayViewer.Tests/`: xUnit v3. `tests/ui/`: Playwright + axe against a stubbed API. `tests/unit/`: Node tests for the pure UI modules.
- `docs/architecture.md`: pipeline, API, cache, updater. Read it only when touching those.
- `docs/replay-data.md`: what the parser reads vs. what else is in a replay. Read it before dumping a replay again.

## Commands (cheapest first)
- One test class: `dotnet test tests/ReplayViewer.Tests -c Release --filter "FullyQualifiedName~ClassName"`
- Build (CI gate: warnings are errors): `dotnet build ReplayViewer.sln -c Release --nologo -warnaserror`
- Format (CI gate): `dotnet format ReplayViewer.sln --verify-no-changes`
- UI: `npm run lint`, `npm run typecheck`, `npm run test:unit` (pure logic in `wwwroot/model.js`), `npm run test:ui`
- Everything CI runs: `pwsh scripts/verify.ps1` (add `-SkipFrontend` for .NET only). Slow; run once before declaring done, not per edit.

## Dev install rule
After any change to app code (`src/`, `wwwroot`), once checks pass, run `pwsh scripts/install-dev.ps1` so the user can see it. It stops the running tray app, installs the working tree over `%LOCALAPPDATA%\ReplayViewer` (cache and logs kept), relaunches, and checks the new build answers. The build is tagged `1.0.x-dev.<timestamp>`: shown in the tray tooltip, a DEV pill on the page, and `/api/config` (`dev: true`). Dev builds never auto-update, so a release can't replace one unasked. Tell the user the tag you installed. Skip for docs-only or test-only changes.

## Gotchas
- NuGet restore is locked (`packages.lock.json`): adding or updating a package means regenerating the lock files.
- Coverage floor is 63% line / 61% branch. Two tests in `FortniteReplayProbeTests` skip without a real replay in the Demos folder; that's expected.
- Never write to the Demos folder: replays are opened read-only with shared access and parsed in place by a child process. The one exception is `MatchScraper.DeleteDemos`, only for names the user picked and confirmed in the sidebar.
- Only network traffic is the GitHub update check. ("Scrape" in `MatchScraper.cs` means parsing a replay, not web scraping.)
- Don't read `artifacts/`, `test-results/`, `node_modules/`, `bin/`, `obj/` or lock files unless the task is about them.
