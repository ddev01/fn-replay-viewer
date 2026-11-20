# Ideas and wishlist

**Not a plan.** Nothing here is scheduled or promised. Written 20 Nov 2026 during an audit; pick from it when you want something fun or valuable to build.

How to read the tags:

- **Effort**: S (hours), M (a day or two), L (a week or more).
- **Data status**, only on ideas that depend on replay data:
  - `verified-type`: the property exists in `FortniteReplayReader` 3.1.0 (checked by reflecting over the package's public models during this audit).
  - `verified-doc`: `docs/replay-data.md` records it as parsed from a real replay.
  - `verified-code`: already flows through this repo's C# and JSON contract.
  - `guess`: plausible, but nobody has dumped a real replay to confirm it is populated. Check first.
- "Dep" means a new dependency or external tool. The project asks before adding any, and the only network traffic today is the update check, so anything that fetches images or posts data is **opt-in only**.

---

## 1. New features: data the parser exposes that the UI never shows

What the UI renders today: roster, team placement, kills, a knock/elim/revive timeline, owner-stat chips (accuracy, damage dealt/taken, assists, revives, eliminations).

| Idea | Pitch | Why it is valuable | Effort | Data status / dep |
| --- | --- | --- | --- | --- |
| **Show distance, weapon and cause on timeline rows** | "Eliminated by X, 142 m, sniper" instead of a bare arrow. | The contract already carries `distance`, `weapon`, `cause` on every event (`MatchEvent`); `app.js` never reads them, so this is the cheapest win in the list. | S | `verified-code` for distance and cause. Weapon is `guess`: the kill-feed path hard-codes `Weapon: null` (`FortniteReplayProbe.cs:402`) and the elimination path emits `GunType` as a numeric string, so a name table is needed. |
| **Death recap card** | A summary of how your run ended: killer, weapon, distance, last 10 s of events, who finished you. | It is what players actually want after a loss. | S | Built from existing events. |
| **Full owner stats panel** | Add damage to structures, materials gathered and used, distance travelled, weapon vs other damage. | `Stats` has them; the app maps only some, and `Wood/Stone/Metal` are always null (dead fields in the contract). | S | `verified-type`: `Stats.DamageToStructures, MaterialsGathered, MaterialsUsed, TotalTraveled, WeaponDamage, OtherDamage`. |
| **Storm phases overlay** | Mark each circle's start and shrink times on the timeline, not just "Storm start". | Timeline readability: see which phase a fight happened in. | S | `verified-type`: `MapData.SafeZones` (radius, start/finish shrink time, centres). `verified-doc`: parses, nothing reads it. |
| **Bus and drop info** | Bus path, when you jumped, how long you skydived, where you landed. | Drop-spot analysis is a popular topic. | M | `verified-type`: `BattleBus`, `PlayerMovement.bIsSkydiving*`, `GameData.AircraftStartTime`. Landing point is `guess` (needs `PlayerData.Locations` populated). |
| **Map view with player paths** | Draw each squad's route, knocks and the storm on the map. | The most visual feature possible; shows rotations and third-parties at a glance. | L | `verified-type`: `PlayerData.Locations`, `DeathLocation`, `MapData.WorldGrid*`. `verified-doc`: `Locations` is built from pawn movement. **Dep:** a map image per map; the parser does not ship one. Licensing needs a decision. |
| **Loot and event markers** | Llama and supply-drop locations and times; reboot-van uses. | Context for "why was everyone in that area". | M | `verified-type`: `MapData.Llamas, SupplyDrops, RebootVans` (nothing reads them, per `docs/replay-data.md`). |
| **Cosmetics per player** | Show skin, pickaxe, glider, emotes. | Fun for squads; nice for the roster. | M | `verified-type`: `PlayerData.Cosmetics`. Showing images needs an external asset API, which conflicts with the no-network rule, so a text-only version comes first. |
| **Inventory and weapon snapshots** | What a player was carrying and holding when they died. | Death context ("they had a shotgun, you had a pickaxe"). | L | `verified-type`: `Inventory`, `InventoryItem`, `WeaponData`, `PlayerData.CurrentWeapon`. Populated in `Full` mode is `guess`. |
| **Build and edit stats** | Walls placed, edits, build fights won. | Build-fight skill is what many players care about. | L | The parser has net-field exports for walls, floors, stairs, `BuildingTool` and `EditingTool` (`verified-type`). Whether they carry per-player counts is `guess`; probably needs a custom export, which `CreativeGameState.cs` shows is possible. |
| **Creative island name** | Show the island code and name in the sidebar instead of just "Creative". | Creative replays currently have no identity. | M | `Mnemonic` is in the file (`verified-doc`). Decoding it with the stock parser is an open question in `docs/replay-data.md`. |

---

## 2. Match analysis

| Idea | Pitch | Why it is valuable | Effort | Data status / dep |
| --- | --- | --- | --- | --- |
| **Fight reconstruction** | Group events into engagements (same teams, within N seconds) and show each as a card with an outcome. | Turns a flat list of 150-200 rows into 10-15 fights you can scan. | M | `verified-code`: derived from existing events. |
| **Alive-count curve** | Chart of players and teams left over time, with your death marked. | Instant read on pacing and when the lobby thinned. | S | Derived from finish/self-elim events (`verified-code`). |
| **"Who beat whom" graph** | Per squad: who eliminated them, and whom they eliminated. | Third-party and revenge stories. | M | Existing events plus `actorTeam`/`targetTeam`. |
| **Match scorecard** | Damage per elimination, accuracy, time alive, survival percentile. | A single number to compare matches. | S | `verified-type` stats; `DeathTime`. |
| **History and trends** | Average placement, K/D and win rate by playlist over time. | Players want progress over weeks, not one match. | M | The index already keeps summaries (`MatchCacheIndex`), but it is capped and rebuilt on update. Needs a durable store (JSON file or SQLite). **Dep:** possibly SQLite. |
| **"Players you have met before"** | Recognise repeat opponents across cached matches. | Fun, and useful for spotting stream-snipers. | M | `verified-code`: epic ids are in every roster. Needs persistence across matches; privacy note: local only, never sent anywhere. |
| **Compare two matches** | Side-by-side summary and timeline. | Before/after a strategy change. | M | Existing documents. |
| **Bot/human mix** | Show the share of bots in the lobby and how many of your kills were bots. | Helps judge how hard a lobby was. | S | `verified-code`: `isBot` per player; `verified-type`: `GameData.TotalBots`. |
| **Search across all matches** | "Who killed me last week" by player name. | Replay folders get long. | M | Needs the persistent store above. |

---

## 3. Sharing and export

| Idea | Pitch | Effort | Data status / dep |
| --- | --- | --- | --- |
| **Match card image** | One-click PNG: map, placement, kills, top stats. For sharing in chat. | M | Canvas rendering, no dep. |
| **Copy summary as text** | Markdown or plain text of the match to paste into Discord. | S | Existing data. |
| **Self-contained HTML export** | A single file anyone can open offline, with the match baked in. | M | Reuses `wwwroot` with the JSON inlined. |
| **CSV export** | Roster and event table. | S | `/api/match.json` exists today, so this is a small step. |
| **Discord webhook** | Post a match summary to a channel automatically. | M | **Dep:** network access, so opt-in, user-supplied URL. |

---

## 4. Replay management

| Idea | Pitch | Why it is valuable | Effort |
| --- | --- | --- | --- |
| **Archive replay files** | Copy replays you want to keep to a folder of your choice. | Fortnite deletes old replays; the app keeps summaries but not the files themselves. Read-only toward Demos, so it respects the rule. | M |
| **Favourites and notes** | Star a match, add a note. Stored in the app's own data folder, never in Demos. | Lets you find "that clutch game". | S |
| **"Show in Explorer" and "open in Fortnite" hints** | Open the file's folder; explain where the in-game replay menu is. | Closes the loop between this app and the game. | S |
| **Disk-use view** | Show how much space the replays and the app's cache use, with a clear-cache button. | Replays are 17-21 MB each. | S |

---

## 5. Live and automation

| Idea | Pitch | Effort | Dep |
| --- | --- | --- | --- |
| **Auto-notify when a match ends** | Balloon tip: "Match finished: #3, 7 elims. Click to view." | M | Needs a watcher on Demos (`FileSystemWatcher`). The app deliberately has none today for idle-cost reasons, so keep it opt-in and cheap. |
| **Auto-open on match end** | Optional: open the browser on the new match. | S | Same watcher. |
| **Second-screen on a phone** | View the match from a phone on the LAN. | M | Requires binding beyond loopback, which changes the security model. Only with an explicit toggle and a token. |
| **Global hotkey** | Open the viewer from anywhere. | S | Win32 hotkey API. |

---

## 6. UX polish

| Idea | Pitch | Effort |
| --- | --- | --- |
| **Real empty and first-run state** | A friendly "No replays yet. Play a match, then come back" screen with a retry button. | S |
| **Keyboard-first timeline** | Arrow keys move through events; Enter jumps to the player's card; `/` focuses search. | M |
| **Event filters by weapon and distance** | "Show only snipes over 100 m". | M |
| **Scrubbable time bar** | A mini-map of the match along the top of the timeline, with storm phases and your events marked. | M |
| **Light/dark/high-contrast themes** | The CSS already has a light-scheme media query; add a high-contrast mode and honour `forced-colors`. | S |
| **Localisation** | The UI is English only. | M |

---

## 7. Tooling and dev workflow

| Idea | Pitch | Why it is valuable | Effort | Dep |
| --- | --- | --- | --- | --- |
| **`replay-dump` tool** | A small project that dumps a replay's models and net-field groups to JSON, checked in. | `docs/replay-data.md` says earlier dumps were throwaway; a repeatable tool stops people re-deriving them and would settle every `guess` in this file. | S/M | None. |
| **Scrubbed fixture replay** | A tiny anonymised `.replay` committed to the repo. | Today two parser tests skip on CI and nothing exercises the real parse path there. | M | Needs a decision on privacy and licensing. |
| **Parser benchmark** | Time parse, assemble and cache for a representative replay. | Gives the performance claims in `docs/architecture.md` a regression check. | S | BenchmarkDotNet, or a stopwatch test. |
| **Changelog from commits** | Generate release notes from the conventional commits you already write. | Free documentation. | S | git-cliff (dev tool). |
| **winget / scoop package** | `winget install replay-viewer`. | Easier install than a SmartScreen-flagged download. | S/M | Package manifests. |
| **Authenticode signing** | Sign the exe and installer so SmartScreen stops warning. | Removes the biggest first-run friction. Separate from the update-signature model, which this audit leaves alone. | M | A signing service or certificate (cost). |
| **Visual regression tests** | Playwright screenshot diffs for roster, timeline and the demos panel. | The UI changes often and axe will not catch layout drift. | M | None beyond Playwright. |

---

## 8. Fun and experimental

| Idea | Pitch | Effort | Data status |
| --- | --- | --- | --- |
| **Longest shot award** | Highlight your longest-range elimination. | S | `verified-code` (distance on events). |
| **"Bus driver thanked" stat** | Did you thank the bus driver? Joke stat. | S | `verified-type`: `PlayerData.HasThankedBusDriver`. |
| **Emote log** | Who danced where, and when. | M | `verified-type`: `Cosmetics.Dances`; the in-match emote events are `guess`. |
| **Season wrapped** | A year-end summary: games, kills, best placement, favourite drop. | M | Needs the history store. |
| **Animated route replay** | A GIF or video of all squads moving across the map over time. | L | Needs map image plus `Locations`. |
| **Badges** | "First win", "10 kills", "Last squad standing". | S | Derived from existing stats. |
| **Most chaotic fight** | Rank engagements by kills per minute and number of squads. | S | Needs fight reconstruction. |

---

## Open questions behind these ideas

1. Is the map image question (licensing, shipping, fetching) worth answering? Half of the visual ideas depend on it.
2. Do you want a persistent history store? It unlocks trends, search, repeat-opponent detection and "season wrapped", but it changes "no history" from a stated limitation to a feature.
3. Which `guess` items should be settled first? A `replay-dump` tool would settle most of them in one go.
