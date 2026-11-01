# Security policy

Replay Viewer is a local-only hobby app for Windows. It is maintained in spare time, and there is
**no bug bounty**.

## Supported versions

Only the latest release is supported. Fixes ship in a new release; older versions get nothing back.

## Reporting a vulnerability

Use **GitHub Security Advisories** on this repository (the **Security** tab → *Report a
vulnerability*). That report is private between you and the maintainer.

Do **not** open a public issue for a security problem, and do not attach replay files you would
rather not share publicly — a minimal or synthetic file is usually enough to show the problem.

## Scope

The app runs as the signed-in user, with no Administrator rights, and never leaves the machine
except for the update check. Interesting areas, in plain language:

- **Replay file parser.** `.replay` files from the Demos folder are untrusted input from disk,
  parsed in-process by `FortniteReplayReader` (`src/ReplayViewer.Core/FortniteReplayProbe.cs`, with
  a 60-second parse timeout). A crafted file that crashes, hangs past the timeout, or escapes the
  parser into the host process is in scope.
- **Loopback HTTP server.** The UI and JSON API are served on `http://127.0.0.1:47231/`
  (`src/ReplayViewer.Core/AppPaths.cs`, `src/ReplayViewer/Routes/MatchRoutes.cs`). It has **no
  authentication and no origin check**, so any process on the machine — including a browser page
  that can reach loopback — can call it. Request handling that goes beyond reading replays under
  the Demos folder, or responses that leak data to another local process, is in scope.
- **Self-updater.** It polls
  `https://api.github.com/repos/ddev01/fn-replay-viewer/releases/latest`, downloads the release zip,
  and checks it against the SHA-256 in the asset's `digest` field
  (`src/ReplayViewer/Hosting/UpdateChecker.cs`). Nothing installs without the user clicking the
  update menu item. Note the known limit: when GitHub omits a `digest`, or reports an algorithm
  other than SHA-256, verification is skipped and the zip is extracted unverified — the update
  trusts that the release on GitHub is genuine. Ways to make that verification stronger, or to
  install something other than the verified release asset, are in scope.
- **Installer payload sync.** `src/ReplayViewer.Core/InstallPayload.cs` copies a payload over
  `%LocalAppData%\ReplayViewer` and deletes files the payload no longer contains (skipping `unins*`
  and the preserved user-data directories). Deleting or overwriting something outside that folder,
  or destroying user data that should survive an update, is in scope.

## Out of scope

- **SmartScreen warnings on unsigned builds.** The binaries are not code-signed; "Windows warned me"
  is expected behaviour, not a vulnerability.
- **Anything that needs Administrator rights.** The installer runs with
  `PrivilegesRequired=lowest`, so a problem that already requires elevated access is not an
  escalation of this app's privileges.
- **Anything that needs your own replay files to be public.** Do not send real replays; describe the
  file structure instead.
