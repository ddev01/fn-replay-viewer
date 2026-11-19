# Replay data: what is read and what else is in the file

Reference for future work so nobody has to dump a replay again. Source of the "available" column:
net-field dumps of a normal match (build 43.10, 22:21) and two Creative replays (15:47, 16:17,
19 Nov 2026), made with `FortniteReplayReader` 3.1.0 in `ParseMode.Full`. The dumps were
throwaway; regenerate one by walking `ReplayReader.ReadReplay` output and the parser's
`NetFieldParser` groups if you need more.

## 1. What the parser hands us (and what we use)

| Model | Fields | Used by the app |
| --- | --- | --- |
| `PlayerData` | `EpicId`, `PlayerId`, `PlayerName`, `Platform`, `TeamIndex`, `Placement`, `Kills`, `TeamKills`, `IsBot`, `IsPartyLeader`, `IsReplayOwner`, `Cosmetics`, `Level`, `DeathTime/Cause/Location`, `Locations` | roster, team grouping, kills, placement, you |
| `FortniteReplay.Eliminations`, `KillFeed` | actor, victim, weapon, knocked/finished, time | timeline events |
| `TeamData` | `TeamIndex`, `Placement`, `PlayerIds`, `PartyOwnerId` | team placement, party owner |
| `GameData` | `CurrentPlaylist`, `TeamSize`, `UtcTimeStartedMatch`, `MaxPlayers`, `TotalBots`, `WinningTeam`, `RecorderId`, `SafeZonesStartTime`, `AircraftStartTime`, `MapInfo`, `GameSessionId` | playlist, size, date; `RecorderId` for you |
| `Stats`, `TeamStats` | eliminations, assists, revives, damage dealt/taken, accuracy, materials, distance | owner stats panel |
| `MapData` | `SafeZones`, `SupplyDrops`, `Llamas`, `RebootVans`, `BattleBusFlightPaths`, world grid | not used yet |
| `ReplayHeader`, `ReplayInfo` | branch, changelist, flags, length, timestamp, `FriendlyName` | version and length |

## 2. Who recorded it

`GameData.RecorderId` is the net guid of the recorder's player state; `RecorderResolver` maps it
through the parser's private `_actorToChannel` / `_players` to an epic id.

- **Battle Royale:** the parser reads `Athena_GameState_C.RecorderPlayerState`. Agrees with the
  labelled recorder on 50 of 50 replays.
- **Creative:** the game state class is `Playspace_GameState_C`, which the parser does not know.
  It has the same `RecorderPlayerState` field (handle 63). `CreativeGameState` registers a
  `GameState` subclass for that path, after which `RecorderId` resolves. Checked on three Creative
  replays (1, 13 and 16 players): it named one player each time, and that player was in the roster.
  Even the one-player 16:17 file resolved to that player.
- `PlayerData.IsGameSessionOwner` (`bIsGameSessionOwner`) is the island host in Creative. It is not
  the recorder.

## 3. Present in the file but not parsed into the models

Net-field groups the replay declares (paths are class names, handy for grepping a dump):

- **Player state** (`FortPlayerStateAthena`): `PlayerNamePrivate`, `PlatformUniqueNetId`,
  `SquadId`, `bInAircraft`, `bHasEverSkydivedFromBus`, `bUsingAnonymousMode`, `InitialSquadSize`,
  `SeasonLevelUIDisplay`, cosmetic loadout parts. Most of these are already mapped.
- **Game state:** `TeamCount`, `PlayersLeft`, `TeamsLeft`, `MatchStartTime`, `Mnemonic` and
  `MatchmakingLinkType` (the Creative island code), `MissionManager`, `POIManager`, `WorldManager`,
  `GameplayState`, `PawnForReplayRelevancy`.
- **Pawns** (`PlayerPawn_Athena_C`): replicated movement, skydive/parachute/emote flags,
  `CurrentWeapon`. `PlayerData.Locations` is built from these.
- **`BP_…` / `B_…` blueprint classes:** weapons and pickaxes (`B_Athena_Pickaxe_*`), supply drops
  and llamas (`AthenaSupplyDrop*`), vehicles, mutators, and in Creative the devices
  (`Device_*`, `BP_Device_*`, `CRD_*`). They carry only the few replicated properties each
  declares, such as `DeviceNameIndex`, `bIsActive`, `Role`; there is no device configuration.

## 4. Open questions

- Whether `Mnemonic` (island code) is decodable with the stock parser for showing the island name.
- Whether `MapData.SafeZones` / `SupplyDrops` are worth surfacing; they parse but nothing reads them.
