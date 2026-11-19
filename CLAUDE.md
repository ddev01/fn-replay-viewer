# Replay Viewer

Windows tray app (.NET 10, WinForms shell) that reads Fortnite `.replay` files and serves a match UI
over loopback (`http://127.0.0.1:47231/`). UI is plain HTML/CSS/JS in `wwwroot`: no bundler, no build step.
Branch `develop` is the main branch. Commits are conventional: `type(scope): subject`.

## Layout
- `src/ReplayViewer.Core/`: UI-free logic (parsing, match assembly, paths, cache).
- `src/ReplayViewer/`: tray, listener, routes, updater. `wwwroot/` is the UI; `types/match.d.ts` is the hand-kept JSON contract for it.
- `tests/ReplayViewer.Tests/`: xUnit v3. `tests/ui/`: Playwright + axe against a stubbed API.
- `docs/architecture.md`: pipeline, API, cache, updater. Read it only when touching those.
- `docs/replay-data.md`: what the parser reads vs. what else is in a replay. Read it before dumping a replay again.

## Commands (cheapest first)
- One test class: `dotnet test tests/ReplayViewer.Tests -c Release --filter "FullyQualifiedName~ClassName"`
- Build (CI gate: warnings are errors): `dotnet build ReplayViewer.sln -c Release --nologo -warnaserror`
- Format (CI gate): `dotnet format ReplayViewer.sln --verify-no-changes`
- UI: `npm run lint`, `npm run typecheck`, `npm run test:ui`
- Everything CI runs: `pwsh scripts/verify.ps1` (add `-SkipFrontend` for .NET only). Slow; run once before declaring done, not per edit.
- NEVER pass `-Branch` to verify.ps1: it runs `git clean -fd` and force-checkouts, discarding local work.

## Gotchas
- NuGet restore is locked (`packages.lock.json`): adding or updating a package means regenerating the lock files.
- Coverage floor is 63% line / 61% branch. Two tests in `FortniteReplayProbeTests` skip without a real replay in the Demos folder; that's expected.
- Never write to the Demos folder: replays are opened read-only and copied to `%TEMP%`.
- Only network traffic is the GitHub update check. ("Scrape" in `MatchScraper.cs` means parsing a replay, not web scraping.)
- Don't read `artifacts/`, `test-results/`, `node_modules/`, `bin/`, `obj/` or lock files unless the task is about them.
