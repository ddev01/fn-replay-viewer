# Plan: owner-only streamer linking

Contract: `contract.md` section B of the lobby-streamers project ("Viewer gate", "Stubs"). Nothing here is a secret; hosts and
tokens only ever live in the owner's local `linking.json`.

## Behaviour

- Gate: `%LOCALAPPDATA%\ReplayViewer\linking.json` = `{"baseUrl","token"}`, read once at start. `baseUrl` must be https
  (http only for loopback hosts, for tests) and the token non-empty with no whitespace. Anything else counts as absent.
  Absent => `/api/config` has `linking: false`, the routes below return 404, no `HttpClient` is created, the UI renders nothing.
- C# proxy (`Routes/LinkingRoutes.cs`, one shared `HttpClient`, 8 s timeout, no redirects, no new packages). It attaches
  `X-Viewer-Token` and forwards to the contract B endpoints:

  | Loopback | Upstream |
  |---|---|
  | `GET /api/linking/lookups?since=` | `GET /api/viewer/lookups` |
  | `POST /api/linking/replay` | `POST /api/viewer/replay` |
  | `POST /api/linking/links` | `POST /api/viewer/links` |
  | `DELETE /api/linking/links` | `DELETE /api/viewer/links` |
  | `POST /api/linking/rejections` | `POST /api/viewer/rejections` |

  The listener's Host / Origin / Sec-Fetch-Site checks already cover every `/api/` path. Writes also need a JSON content type.
  Upstream errors become `{error}`: 400 keeps the server's detail (a bad pasted URL), 401/403 -> 502 "token refused",
  429 -> 429, unreachable -> 502, timeout -> 504, anything else -> 502.
- `GameSessionId` is now parsed (`GameData.GameSessionId`) and carried on `MatchDocument.gameSessionId` so the replay call can
  use it. `MatchCache.FormatVersion` is bumped (cached matches predate the field).
- UI (`linking.js`, plain JS): sidebar icon for `none_found` / `weak_only` replays (matched by lookup `received_at` against the
  replay's time window; opening the replay replaces that guess with the server's real match). Opening a replay calls
  `POST /api/linking/replay` once. Per player: links (platform, handle, source) and F9 attempts. A pencil opens a panel with
  Twitch/TikTok search links and a paste box; submit -> `POST /api/linking/links`; delete link; "wrong guess" -> rejections.
  No client cache: every open asks again.

## Tests

C# (`LinkingTests.cs`):
- gate parsing: absent file, bad json, http non-loopback, empty token, token with spaces, https ok, loopback http ok
- routes 404 when off (and no upstream request made)
- header attached, path/method/body forwarded for each of the five routes
- cross-site, foreign Host rejected for the new routes (through `ListenerHost`)
- error mapping: 400 detail kept, 401 -> 502, 429, unreachable, timeout, non-JSON success body
- `/api/config` carries `linking`; `ContractTests` covers `gameSessionId`

JS:
- unit (`tests/unit/linking.test.mjs`): flag lookup by time window, URL parsing helpers
- UI (`tests/ui/linking.spec.mjs`, stubbed with the contract's canned responses): linking off => no linking elements and no
  `/api/linking` request; sidebar flag icons; panel opens with search links; paste success updates the row; paste error (400)
  shown; delete removes the link; axe clean.

## Docs

CLAUDE.md, README/SECURITY/info.md/architecture.md: "only network traffic is the GitHub update check" becomes "plus optional
owner-only linking when configured". `types/match.d.ts` gets `gameSessionId` and `linking`.
