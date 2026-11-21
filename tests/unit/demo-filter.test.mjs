import test from "node:test";
import assert from "node:assert/strict";
import {
  accountChoices,
  activeFilterCount,
  classifyPlaylist,
  emptyFilters,
  filterDemos,
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
