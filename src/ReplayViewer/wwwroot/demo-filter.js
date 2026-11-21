// @ts-check
// The Demos panel's filters: pure functions of the replay list and the filter values, no DOM.

/**
 * @typedef {{
 *   sizes: Set<string>,
 *   ranked: Set<string>,
 *   builds: Set<string>,
 *   maps: Set<string>,
 *   from: string,
 *   to: string,
 *   minKills: string,
 *   maxKills: string,
 *   hiddenAccounts: Set<string>
 * }} DemoFilters
 */

/** @returns {DemoFilters} */
export function emptyFilters() {
  return {
    sizes: new Set(),
    ranked: new Set(),
    builds: new Set(),
    maps: new Set(),
    from: "",
    to: "",
    minKills: "",
    maxKills: "",
    hiddenAccounts: new Set(),
  };
}

const HIDDEN_KEY = "replayViewer.hiddenAccounts";

/**
 * Accounts the user switched off in the sidebar, kept across restarts so a streamer never has to
 * redo it. Best effort: storage can be blocked, and then nothing is hidden.
 * @returns {Set<string>}
 */
export function loadHiddenAccounts() {
  try {
    const raw = JSON.parse(globalThis.localStorage?.getItem(HIDDEN_KEY) ?? "[]");
    return new Set(Array.isArray(raw) ? raw.filter((id) => typeof id === "string") : []);
  } catch {
    return new Set();
  }
}

/** @param {Set<string>} ids */
export function saveHiddenAccounts(ids) {
  try {
    globalThis.localStorage?.setItem(HIDDEN_KEY, JSON.stringify([...ids]));
  } catch {
    /* storage blocked: the choice lasts until the page closes */
  }
}

/**
 * A replay is hidden when its account is switched off, and also when its account is not known
 * yet (not parsed): showing it could be the very account the user hid.
 * @param {DemoListItem | undefined} demo
 * @param {DemoFilters} f
 * @returns {boolean}
 */
export function isAccountHidden(demo, f) {
  if (!f.hiddenAccounts.size) return false;
  return !demo?.accountId || f.hiddenAccounts.has(demo.accountId);
}

/**
 * @param {DemoListItem[]} demos
 * @returns {{ id: string, name: string, count: number }[]}
 */
export function accountChoices(demos) {
  /** @type {Map<string, { id: string, name: string, count: number }>} */
  const byId = new Map();
  for (const demo of demos) {
    if (!demo.accountId) continue;
    const entry = byId.get(demo.accountId) ?? {
      id: demo.accountId,
      name: demo.accountName || demo.accountId,
      count: 0,
    };
    entry.count += 1;
    byId.set(demo.accountId, entry);
  }
  return [...byId.values()].sort((a, b) => b.count - a.count || a.id.localeCompare(b.id));
}

/**
 * What a playlist id says about the match. The ids are Epic's internal names, so this is a
 * best-effort read; an id it cannot read yields nulls and fails any filter on that facet.
 * shortcut: "Habanero" is treated as ranked from the ids seen so far; add ids as they turn up.
 * @param {string | undefined} playlist
 * @returns {{ size: string | null, ranked: string | null, build: string | null }}
 */
export function classifyPlaylist(playlist) {
  if (!playlist) return { size: null, ranked: null, build: null };
  const size = /solo/i.test(playlist)
    ? "solo"
    : /duo/i.test(playlist)
      ? "duos"
      : /trio/i.test(playlist)
        ? "trios"
        : /squad/i.test(playlist)
          ? "squads"
          : null;
  const creative = /playground|creative/i.test(playlist);
  return {
    size,
    ranked: /ranked|habanero/i.test(playlist) ? "ranked" : "unranked",
    build: creative ? null : /no_?build|zero_?build/i.test(playlist) ? "zerobuild" : "build",
  };
}

/**
 * The local calendar day of a replay, as YYYY-MM-DD, which is what a date input holds.
 * @param {DemoListItem} demo
 * @returns {string}
 */
export function demoDay(demo) {
  const stamp = demo.recordedAt || demo.lastWriteUtc;
  const date = stamp ? new Date(stamp) : null;
  if (!date || Number.isNaN(date.getTime())) return "";
  const pad = (/** @type {number} */ n) => String(n).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/**
 * @param {DemoListItem} demo
 * @param {DemoFilters} f
 * @returns {boolean}
 */
export function matchesFilters(demo, f) {
  if (isAccountHidden(demo, f)) return false;
  const { size, ranked, build } = classifyPlaylist(demo.playlist);
  if (f.sizes.size && !(size && f.sizes.has(size))) return false;
  if (f.ranked.size && !(ranked && f.ranked.has(ranked))) return false;
  if (f.builds.size && !(build && f.builds.has(build))) return false;
  if (f.maps.size && !(demo.mapName && f.maps.has(demo.mapName))) return false;
  if (f.from || f.to) {
    const day = demoDay(demo);
    if (!day || (f.from && day < f.from) || (f.to && day > f.to)) return false;
  }
  const min = f.minKills === "" ? null : Number(f.minKills);
  const max = f.maxKills === "" ? null : Number(f.maxKills);
  if (min !== null || max !== null) {
    if (demo.elims == null) return false;
    if (min !== null && demo.elims < min) return false;
    if (max !== null && demo.elims > max) return false;
  }
  return true;
}

/**
 * @param {DemoListItem[]} demos
 * @param {DemoFilters} filters
 * @returns {DemoListItem[]}
 */
export function filterDemos(demos, filters) {
  return demos.filter((demo) => matchesFilters(demo, filters));
}

/**
 * How many filters are narrowing the list, for the badge on the filter button.
 * @param {DemoFilters} f
 * @returns {number}
 */
export function activeFilterCount(f) {
  return (
    [f.sizes, f.ranked, f.builds, f.maps].filter((set) => set.size).length +
    (f.from || f.to ? 1 : 0) +
    (f.minKills !== "" || f.maxKills !== "" ? 1 : 0)
  );
}

/**
 * @param {DemoListItem[]} demos
 * @returns {string[]}
 */
export function mapChoices(demos) {
  const maps = new Set();
  for (const demo of demos) if (demo.mapName) maps.add(demo.mapName);
  return [...maps].sort();
}

/**
 * @param {DemoFilters} f
 * @returns {string}
 */
export function filtersKey(f) {
  return JSON.stringify([
    [...f.sizes],
    [...f.ranked],
    [...f.builds],
    [...f.maps],
    f.from,
    f.to,
    f.minKills,
    f.maxKills,
    [...f.hiddenAccounts],
  ]);
}
