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
except for the update check, plus optional owner-only linking when configured
(a local `linking.json` with a server URL and token; without it the feature is off and sends nothing). Interesting areas, in plain language:

- **Replay file parser.** `.replay` files from the Demos folder are untrusted input from disk,
  parsed by `FortniteReplayReader` in a **child process** (`ReplayParseProcess`,
  `src/ReplayViewer.Core/FortniteReplayProbe.cs`). The child runs at below-normal priority with a
  2 GB heap limit, is killed after 60 seconds, ends itself after 2 seconds without parser
  progress, and exits if the app does. Files over 512 MB are refused. A crafted file that
  crashes or hangs the *app*, escapes the parser into the host, or defeats those limits is in
  scope.
- **Loopback HTTP server.** The UI and JSON API are served on `http://127.0.0.1:47231/`
  (`src/ReplayViewer.Core/AppPaths.cs`, `src/ReplayViewer/Routes/MatchRoutes.cs`). It has **no
  authentication**, so any process on the machine can call it. Browser pages cannot: requests
  whose `Host` is not `127.0.0.1` or `localhost` on the listening port are refused (421, which
  stops DNS rebinding), and `/api/*` calls carrying a cross-site `Sec-Fetch-Site` or a foreign
  `Origin` are refused (403). Request handling that goes beyond reading replays under the Demos
  folder, a way around those two checks, or responses that leak data to another local process,
  is in scope.
- **Self-updater.** It polls
  `https://api.github.com/repos/ddev01/fn-replay-viewer/releases/latest`, downloads the release zip
  and its detached ECDSA P-256 signature, and verifies the signature against a key embedded in the
  app. An update without a valid signature is refused (fail closed). The GitHub `digest` is also
  checked when present, and the extracted exe's version must match the release tag
  (`src/ReplayViewer/Hosting/UpdateChecker.cs`). The update is staged silently and installs on
  the next launch or when the user asks. Ways to install something other than a release signed by
  the maintainer, or to downgrade, are in scope.
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
