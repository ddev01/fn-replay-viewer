// @ts-check
// The one mutable object the page runs on, and the two selection helpers that change it.

/**
 * @type {{
 *   match: MatchDocument | null,
 *   tab: import("./model.js").Tab,
 *   focusTeam: number | null,
 *   focusPlayerId: string | null,
 *   teamSelected: Set<string>,
 *   sort: import("./model.js").SortMode,
 *   demosOpen: boolean,
 *   nameFnPublicUrl: string | null,
 *   demos: DemoListItem[],
 *   currentFile: string | null,
 *   demosTimer: number | undefined,
 *   statusTimer: number | undefined,
 *   backfill: boolean,
 *   loadingDemos: boolean,
 *   lastDemosRefresh: number,
 *   demosShown: number,
 *   demosSignature: string
 * }}
 */
export const state = {
  match: null,
  tab: "you",
  focusTeam: null,
  focusPlayerId: null,
  teamSelected: new Set(),
  sort: "placement",
  demosOpen: false,
  nameFnPublicUrl: null,
  demos: [],
  currentFile: null,
  demosTimer: undefined,
  statusTimer: undefined,
  backfill: false,
  loadingDemos: false,
  lastDemosRefresh: 0,
  demosShown: 0,
  demosSignature: "",
};

/**
 * @param {TeamRoster | null | undefined} team
 * @param {string | null} [playerId]
 */
export function selectSquad(team, playerId) {
  if (!team) return;
  state.focusTeam = team.teamIndex;
  state.focusPlayerId = playerId || null;
  state.teamSelected = new Set(
    team.players.filter((p) => p.epicId).map((p) => String(p.epicId).toLowerCase())
  );
}

/**
 * @param {TeamRoster | null | undefined} team
 * @param {string | null | undefined} playerId
 */
export function selectOnlyPlayer(team, playerId) {
  if (!team || !playerId) return;
  state.focusTeam = team.teamIndex;
  state.focusPlayerId = playerId;
  state.teamSelected = new Set([String(playerId).toLowerCase()]);
}
