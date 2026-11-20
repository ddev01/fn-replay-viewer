// Unit tests for the pure match logic in wwwroot/model.js. No browser: these run under Node.
import test from "node:test";
import assert from "node:assert/strict";
import match from "../ui/fixtures/match.mjs";
import {
  emptyTimelineMessage,
  eventSide,
  filterEvents,
  keepWithHideBots,
  sortTeams,
  teamByIndex,
  youId,
} from "../../src/ReplayViewer/wwwroot/model.js";

const NOVA = "8a1f2b3c4d5e6f708192a3b4c5d6e7f8";
const ROOK = "2c3d4e5f60718293a4b5c6d7e8f90112";

/** @returns {import("../../src/ReplayViewer/wwwroot/model.js").TimelineView} */
const view = (overrides = {}) => ({
  tab: "you",
  focusTeam: null,
  teamSelected: new Set(),
  you: { kills: true, knocks: true, deaths: true },
  team: { kills: true, knocks: true, deaths: true },
  hideBots: false,
  onlyYou: false,
  ...overrides,
});

const kinds = (rows) => rows.map((e) => e.kind);

test("youId is the player flagged isYou", () => {
  assert.equal(youId(match), NOVA);
});

test("the You tab shows events that involve you, in time order", () => {
  assert.deepEqual(kinds(filterEvents(match, view())), ["knock", "finish", "revive"]);
});

test("the You tab honours the kills and knocks filters", () => {
  const noKills = view({ you: { kills: false, knocks: true, deaths: true } });
  assert.deepEqual(kinds(filterEvents(match, noKills)), ["knock", "revive"]);
  const noKnocks = view({ you: { kills: true, knocks: false, deaths: true } });
  assert.deepEqual(kinds(filterEvents(match, noKnocks)), ["finish", "revive"]);
});

test("the Game tab shows only knocks and finishes", () => {
  assert.deepEqual(kinds(filterEvents(match, view({ tab: "game" }))), ["knock", "finish"]);
});

test("the Team tab shows the selected squad's events", () => {
  const team = view({ tab: "team", focusTeam: 1, teamSelected: new Set([ROOK]) });
  assert.deepEqual(kinds(filterEvents(match, team)), ["knock", "finish", "selfElim"]);

  const noDeaths = view({
    tab: "team",
    focusTeam: 1,
    teamSelected: new Set([ROOK]),
    team: { kills: true, knocks: true, deaths: false },
  });
  assert.deepEqual(kinds(filterEvents(match, noDeaths)), ["knock"]);
});

test("the Team tab with no squad selected shows nothing instead of another squad", () => {
  assert.deepEqual(filterEvents(match, view({ tab: "team" })), []);
});

test("hiding bots drops fights against bots but keeps the rest", () => {
  const withBot = {
    ...match,
    teams: [
      ...match.teams,
      { teamIndex: 2, players: [{ ...match.teams[1].players[1], epicId: "bot1", name: "Bot" }] },
    ],
    events: [
      ...match.events,
      {
        timeSeconds: 200,
        kind: "finish",
        actorId: NOVA,
        actorName: "Nova",
        targetId: "bot1",
        targetName: "Bot",
      },
    ],
  };
  assert.equal(filterEvents(withBot, view({ tab: "game", hideBots: false })).length, 3);
  assert.equal(filterEvents(withBot, view({ tab: "game", hideBots: true })).length, 2);
});

test("the storm is hidden only when its victim is a bot", () => {
  const bot = { ...match.teams[0].players[0], isBot: true, epicId: "b" };
  const botVictim = { ...match, teams: [{ teamIndex: 0, players: [bot] }] };
  const storm = { kind: "selfElim", actorName: "Storm", targetId: "b", timeSeconds: 1 };
  assert.equal(keepWithHideBots(botVictim, storm, null), false);
  assert.equal(keepWithHideBots(match, { ...storm, targetId: NOVA }, null), true);
});

test("only-you keeps fights that include you", () => {
  assert.deepEqual(kinds(filterEvents(match, view({ tab: "game", onlyYou: true }))), ["knock", "finish"]);
});

test("eventSide says whether you dealt or took the hit", () => {
  const [knock] = match.events;
  assert.equal(eventSide(match, knock, view()), "dealt");
  assert.equal(eventSide(match, { ...knock, actorId: ROOK, targetId: NOVA }, view()), "taken");
  assert.equal(eventSide(match, match.events[2], view()), "");
});

test("teams sort by placement, or by kills with placement as the tiebreak", () => {
  const order = (teams, mode) => sortTeams(teams, mode).map((t) => t.teamIndex);
  assert.deepEqual(order(match.teams, "placement"), [0, 1]);
  const swapped = [match.teams[1], match.teams[0]];
  assert.deepEqual(order(swapped, "placement"), [0, 1]);
  assert.deepEqual(order(swapped, "kills"), [0, 1]);
});

test("an unplaced squad sorts after a placed one", () => {
  const unplaced = { ...match.teams[0], teamIndex: 9, placement: undefined };
  assert.deepEqual(
    sortTeams([unplaced, match.teams[1]], "placement").map((t) => t.teamIndex),
    [1, 9]
  );
});

test("teamByIndex does not treat a missing index as team 0", () => {
  assert.equal(teamByIndex(match, null), null);
  assert.equal(teamByIndex(match, 0)?.teamIndex, 0);
});

test("an empty timeline explains itself", () => {
  const noYou = {
    ...match,
    teams: match.teams.map((t) => ({ ...t, players: t.players.map((p) => ({ ...p, isYou: false })) })),
  };
  assert.match(emptyTimelineMessage(noYou, view()), /doesn't record which player you were/);
  assert.match(emptyTimelineMessage(match, view({ tab: "team" })), /Select a squad/);
  assert.equal(emptyTimelineMessage(match, view({ tab: "game" })), "Nothing recorded here.");
});
