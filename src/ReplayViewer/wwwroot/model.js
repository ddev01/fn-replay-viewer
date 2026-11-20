// @ts-check
// The match logic: which teams, players and events to show. Pure functions of a MatchDocument and a
// TimelineView, with no DOM and no page state, so they run (and are tested) under plain Node.
import { idsEqual } from "./format.js";

/** @typedef {"game" | "you" | "team"} Tab */
/** @typedef {"placement" | "kills"} SortMode */
/** @typedef {{ kills: boolean, knocks: boolean, deaths: boolean }} KindFilter */
/**
 * What the timeline filters need. The caller reads it from the page and the page state.
 * @typedef {{
 *   tab: Tab,
 *   focusTeam: number | null,
 *   teamSelected: Set<string>,
 *   you: KindFilter,
 *   team: KindFilter,
 *   hideBots: boolean,
 *   onlyYou: boolean
 * }} TimelineView
 */

/**
 * @param {MatchDocument} match
 * @returns {string | null}
 */
export function youId(match) {
  for (const team of match.teams) {
    for (const p of team.players) {
      if (p.isYou) return p.epicId ?? null;
    }
  }
  return null;
}

/**
 * @param {MatchDocument} match
 * @param {string | null | undefined} id
 * @returns {{ team: TeamRoster, player: RosterPlayer } | null}
 */
export function findPlayer(match, id) {
  for (const team of match.teams) {
    for (const p of team.players) {
      if (idsEqual(p.epicId, id)) return { team, player: p };
    }
  }
  return null;
}

/**
 * @param {MatchDocument} match
 * @returns {TeamRoster | null}
 */
export function yourTeam(match) {
  return match.teams.find((t) => t.players.some((p) => p.isYou)) ?? null;
}

/**
 * @param {MatchDocument} match
 * @param {number | null | undefined} index
 * @returns {TeamRoster | null}
 */
export function teamByIndex(match, index) {
  // Number(null) is 0, so without this a missing index would resolve to team 0.
  if (index == null) return null;
  return match.teams.find((t) => t.teamIndex === Number(index)) ?? null;
}

/**
 * @param {TeamRoster} team
 * @returns {number}
 */
export function teamKills(team) {
  return (team.players || []).reduce((sum, p) => sum + (p.kills || 0), 0);
}

/**
 * @param {TeamRoster[]} teams
 * @param {SortMode} mode
 * @returns {TeamRoster[]}
 */
export function sortTeams(teams, mode) {
  const copy = teams.slice();
  if (mode === "kills") {
    copy.sort((a, b) => teamKills(b) - teamKills(a) || (a.placement ?? 9999) - (b.placement ?? 9999));
    return copy;
  }
  copy.sort((a, b) => {
    const ap = a.placement;
    const bp = b.placement;
    if (ap != null && bp != null && ap !== bp) return ap - bp;
    if (ap != null && bp == null) return -1;
    if (ap == null && bp != null) return 1;
    return teamKills(b) - teamKills(a);
  });
  return copy;
}

/**
 * @param {MatchDocument} match
 * @returns {MatchEvent[]}
 */
export function youEvents(match) {
  const you = youId(match);
  return match.events.filter((e) => idsEqual(e.actorId, you) || idsEqual(e.targetId, you));
}

/**
 * @param {MatchDocument} match
 * @returns {MatchEvent | undefined}
 */
export function latestYouDeath(match) {
  const you = youId(match);
  return youEvents(match)
    .filter((e) => (e.kind === "finish" || e.kind === "selfElim") && idsEqual(e.targetId, you))
    .sort((a, b) => b.timeSeconds - a.timeSeconds)[0];
}

/**
 * @param {string | null | undefined} label
 * @returns {boolean}
 */
export function isEnvironmentName(label) {
  return label === "Storm" || label === "Fall" || label === "Environment";
}

/**
 * @param {MatchDocument} match
 * @param {MatchEvent} e
 * @param {string | null} you
 * @returns {boolean}
 */
export function keepWithHideBots(match, e, you) {
  const actorBot = findPlayer(match, e.actorId)?.player?.isBot === true;
  const targetBot = findPlayer(match, e.targetId)?.player?.isBot === true;
  if (idsEqual(e.actorId, you)) return !targetBot;
  if (idsEqual(e.targetId, you)) return !actorBot;
  if (!e.actorId && isEnvironmentName(e.actorName)) return !targetBot;
  return !actorBot && !targetBot;
}

/**
 * @param {MatchDocument} match
 * @param {TimelineView} view
 * @returns {MatchEvent[]}
 */
export function filterEvents(match, view) {
  const you = youId(match);
  /** @type {MatchEvent[]} */
  let rows;
  if (view.tab === "game") {
    rows = match.events.filter((e) => e.kind === "knock" || e.kind === "finish");
  } else if (view.tab === "you") {
    rows = youEvents(match).filter((e) => {
      if (e.kind === "finish" && idsEqual(e.actorId, you)) return view.you.kills;
      if (e.kind === "knock" && (idsEqual(e.actorId, you) || idsEqual(e.targetId, you))) {
        return view.you.knocks;
      }
      if ((e.kind === "finish" || e.kind === "selfElim") && idsEqual(e.targetId, you)) {
        return view.you.deaths;
      }
      return e.kind === "revive";
    });
  } else {
    const teamIndex = view.focusTeam;
    rows = match.events
      .filter((e) => e.actorTeam === teamIndex || e.targetTeam === teamIndex)
      .filter((e) => {
        if (e.kind === "knock") return view.team.knocks;
        if (e.kind === "finish" && e.actorTeam === teamIndex) return view.team.kills;
        if (e.kind === "finish" || e.kind === "selfElim") return view.team.deaths;
        return true;
      })
      .filter((e) => {
        const actorOn =
          e.actorTeam === teamIndex && view.teamSelected.has(String(e.actorId || "").toLowerCase());
        const targetOn =
          e.targetTeam === teamIndex && view.teamSelected.has(String(e.targetId || "").toLowerCase());
        return actorOn || targetOn;
      });
  }

  if (view.hideBots) {
    rows = rows.filter((e) => keepWithHideBots(match, e, you));
  }
  if (view.onlyYou) {
    rows = rows.filter((e) => idsEqual(e.actorId, you) || idsEqual(e.targetId, you));
  }
  return rows.slice().sort((a, b) => a.timeSeconds - b.timeSeconds);
}

/**
 * Whether an event hurt or helped the player (or squad) the current tab is about.
 * @param {MatchDocument} match
 * @param {MatchEvent} e
 * @param {TimelineView} view
 * @returns {"taken" | "dealt" | ""}
 */
export function eventSide(match, e, view) {
  if (e.kind !== "knock" && e.kind !== "finish" && e.kind !== "selfElim") {
    return "";
  }
  if (view.tab === "you") {
    const you = youId(match);
    const dealt = idsEqual(e.actorId, you);
    const taken = idsEqual(e.targetId, you);
    if (taken && !dealt) return "taken";
    if (dealt && !taken) return "dealt";
    if (taken) return "taken";
    return "";
  }
  if (view.tab === "team") {
    const team = Number(view.focusTeam);
    const actorOn = e.actorTeam === team && view.teamSelected.has(String(e.actorId || "").toLowerCase());
    const targetOn = e.targetTeam === team && view.teamSelected.has(String(e.targetId || "").toLowerCase());
    if (targetOn && !actorOn) return "taken";
    if (actorOn && !targetOn) return "dealt";
    if (targetOn) return "taken";
  }
  return "";
}

/**
 * @param {MatchDocument} match
 * @param {TimelineView} view
 * @returns {string}
 */
export function emptyTimelineMessage(match, view) {
  if (view.tab === "you" && youId(match) == null) {
    return "This replay doesn't record which player you were, so there is no personal timeline.";
  }

  if (view.tab === "team" && view.focusTeam == null) {
    return "Select a squad to see its timeline.";
  }

  return "Nothing recorded here.";
}
