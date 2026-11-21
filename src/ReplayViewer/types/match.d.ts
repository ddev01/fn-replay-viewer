// Shapes of the JSON the UI consumes.
//
// Source of truth: src/ReplayViewer.Core/Match/MatchTypes.cs (records + enums) and
// src/ReplayViewer/Http/HttpJson.cs (camelCase names, JsonStringEnumConverter with
// camelCase policy, nulls omitted).
//
// Hand-maintained, but enforced: tests/ReplayViewer.Tests/ContractTests.cs fails when a record
// and its interface here disagree on property names or optionality. Properties are optional
// where the C# type is nullable, because null properties are not written.

type EventKind = "knock" | "finish" | "selfElim" | "revive";

type FillStatus = "none" | "fill" | "unknown";

/** MatchTypes.cs: OwnerStatsSnapshot */
interface OwnerStats {
  eliminations?: number;
  damageDealt?: number;
  accuracy?: number;
  assists?: number;
  revives?: number;
  damageTaken?: number;
}

/** MatchTypes.cs: RosterPlayer */
interface RosterPlayer {
  epicId?: string;
  name: string;
  platform?: string;
  kills: number;
  isYou: boolean;
  isBot: boolean;
  disconnected: boolean;
  anonymous: boolean;
  fill: FillStatus;
  playerNumber?: number;
  placement?: number;
}

/** MatchTypes.cs: TeamRoster */
interface TeamRoster {
  teamIndex: number;
  placement?: number;
  players: RosterPlayer[];
}

/** MatchTypes.cs: MatchEvent */
interface MatchEvent {
  timeSeconds: number;
  kind: EventKind;
  actorId?: string;
  actorName?: string;
  targetId?: string;
  targetName?: string;
  weapon?: string;
  distance?: number;
  cause?: string;
  actorTeam?: number;
  targetTeam?: number;
}

/** MatchTypes.cs: MatchDocument */
interface MatchDocument {
  sourceFile: string;
  scrapedAt: string;
  playlist?: string;
  teamSize?: number;
  recordedAt?: string;
  yourPlacement?: number;
  teamPlacement?: number;
  safeZonesStartTime?: number;
  ownerStats?: OwnerStats;
  teams: TeamRoster[];
  events: MatchEvent[];
  durationSeconds?: number;
  mapName?: string;
}

/** MatchTypes.cs: DemoListItem */
interface DemoListItem {
  name: string;
  path: string;
  length: number;
  lastWriteUtc: string;
  mapName?: string;
  playlist?: string;
  recordedAt?: string;
  durationSeconds?: number;
  placement?: number;
  elims?: number;
  cached: boolean;
  totalPlayers?: number;
  unreadable: boolean;
  accountId?: string;
  accountName?: string;
}

/** MatchRoutes.cs GET /api/demos */
interface DemosResponse {
  demos: DemoListItem[];
  backfill: boolean;
}

/** MatchRoutes.cs GET /api/update */
interface UpdateInfo {
  available?: string;
  staged: boolean;
  stagedVersion?: string;
}

/** HttpJson.cs WriteErrorAsync: every non-2xx JSON body */
interface ApiError {
  error: string;
}

/** MatchRoutes.cs GET /api/config */
interface ApiConfig {
  nameFnPublicUrl?: string;
  version: string;
  dev: boolean;
}
