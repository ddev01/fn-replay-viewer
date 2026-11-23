import test from "node:test";
import assert from "node:assert/strict";
import {
  accountChoices,
  activeFilterCount,
  classifyPlaylist,
  emptyFilters,
  filterDemos,
  playerHits,
} from "../../src/ReplayViewer/wwwroot/demo-filter.js";

const demo = (/** @type {object} */ over) => ({
  name: "a.replay",
  path: "a",
  length: 1,
  lastWriteUtc: "2026-10-31T12:00:00",
  cached: true,
  unreadable: false,
  ...over,
});

test("classifyPlaylist reads size, ranked and build from the id", () => {
  assert.deepEqual(classifyPlaylist("Playlist_Habanero_NoBuild_PiperBoot_Duos"), {
    size: "duos",
    ranked: "ranked",
    build: "zerobuild",
  });
  assert.deepEqual(classifyPlaylist("Playlist_DefaultSquad"), {
    size: "squads",
    ranked: "unranked",
    build: "build",
  });
  assert.equal(classifyPlaylist("Playlist_PlaygroundV2").build, null);
  assert.equal(classifyPlaylist(undefined).size, null);
});

test("filterDemos narrows by size, map, date and kills, and drops unknowns", () => {
  const demos = [
    demo({ name: "1", playlist: "Playlist_DefaultDuo", mapName: "Reload - Surf City", elims: 5 }),
    demo({
      name: "2",
      playlist: "Playlist_DefaultSolo",
      mapName: "Creative",
      elims: 12,
      lastWriteUtc: "2026-09-01T12:00:00",
    }),
    demo({ name: "3" }),
  ];
  const names = (/** @type {ReturnType<typeof emptyFilters>} */ f) =>
    filterDemos(demos, f).map((d) => d.name);

  assert.deepEqual(names(emptyFilters()), ["1", "2", "3"]);
  const duos = emptyFilters();
  duos.sizes.add("duos");
  assert.deepEqual(names(duos), ["1"]);
  const maps = emptyFilters();
  maps.maps.add("Creative");
  assert.deepEqual(names(maps), ["2"]);
  const recent = emptyFilters();
  recent.from = "2026-10-01";
  assert.deepEqual(names(recent), ["1", "3"]);
  const kills = emptyFilters();
  kills.minKills = "10";
  assert.deepEqual(names(kills), ["2"]);
  assert.equal(activeFilterCount(kills), 1);
});

test("a switched-off account hides its replays and any whose account is not known yet", () => {
  const demos = [
    demo({ name: "main", accountId: "m", accountName: "Main" }),
    demo({ name: "alt", accountId: "a", accountName: "Alt" }),
    demo({ name: "unparsed" }),
  ];
  const filters = emptyFilters();
  assert.deepEqual(
    filterDemos(demos, filters).map((d) => d.name),
    ["main", "alt", "unparsed"]
  );
  filters.hiddenAccounts.add("a");
  assert.deepEqual(
    filterDemos(demos, filters).map((d) => d.name),
    ["main"]
  );
  assert.deepEqual(
    accountChoices(demos).map((a) => a.id),
    ["a", "m"]
  );
});

test("playerHits finds a part of a name and forgives typos, but not short guesses", () => {
  const players = ["UNIQUE FRVR 193!", "TikTok Space Aura FN"];
  const hit = (/** @type {string} */ q) => playerHits(q, players).names;
  assert.deepEqual(hit("Unique"), [players[0]]);
  assert.deepEqual(hit("aura"), [players[1]]);
  assert.deepEqual(hit("tiktok spce aura"), [players[1]]);
  assert.deepEqual(hit("uniqe"), [players[0]]);
  assert.deepEqual(hit("bob"), []);
  assert.deepEqual(playerHits("aura", undefined).names, []);
  assert.deepEqual(playerHits("  !", players).names, []);
});

test("filterDemos keeps only the closest matches", () => {
  const demos = [
    demo({ name: "1", players: ["Bobby"] }),
    demo({ name: "2", players: ["Bobb"] }),
    demo({ name: "3", players: ["Al"] }),
    demo({ name: "4" }),
  ];
  const names = (/** @type {string} */ player) =>
    filterDemos(demos, { ...emptyFilters(), player }).map((d) => d.name);
  assert.deepEqual(names("bobby"), ["1"]);
  assert.deepEqual(names("bobb"), ["1", "2"]);
  assert.deepEqual(names("zzzz"), []);
});
