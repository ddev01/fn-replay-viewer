// @ts-check
// The Demos panel: the replay list, its paging, its refresh rules and its dialog behaviour.
import {
  accountChoices,
  activeFilterCount,
  emptyFilters,
  filterDemos,
  filtersKey,
  isAccountHidden,
  mapChoices,
  saveHiddenAccounts,
} from "./demo-filter.js";
import { $, showStatus } from "./dom.js";
import { prettyPlaylist } from "./format.js";
import { demoFlag, fetchLookups } from "./linking.js";
import { themedIcon } from "./markup.js";
import { state } from "./state.js";

const DEMO_PAGE = 20;
const DEMO_REFRESH_MS = 10000;

/** @type {(demo: DemoListItem) => void} */
let openDemo = () => {};

/**
 * The panel opens a replay by calling back into the page, which owns scraping.
 * @param {(demo: DemoListItem) => void} handler
 */
export function onOpenDemo(handler) {
  openDemo = handler;
}

/** @param {boolean} open */
export function setDemosOpen(open) {
  const wasOpen = state.demosOpen;
  state.demosOpen = open;
  $("demos-panel").hidden = !open;
  $("demos-backdrop").hidden = !open;
  $("toggle-demos").setAttribute("aria-expanded", String(open));
  // The panel is modal: keep the page behind it out of the tab order and away from readers.
  /** @type {HTMLElement} */ (document.querySelector("main")).inert = open;
  if (open) {
    loadDemos();
    if (state.linking) loadLinkFlags();
    // The rows rendered while the panel was hidden could not measure it.
    fillDemos();
    $("close-demos").focus();
  } else if (wasOpen) {
    $("toggle-demos").focus();
  }
  // Closing the panel hides what a streamer would not want left on screen.
  if (!open) collapseTools();
}

/**
 * @param {DemoListItem} demo
 * @returns {string}
 */
function demoTitle(demo) {
  // A file the parser stalls on has no map at all, and its file name means nothing to a player.
  if (demo.unreadable) return "Broken replay file";
  if (demo.mapName) return demo.mapName;
  if (demo.playlist) return prettyPlaylist(demo.playlist);
  return demo.name.replace(/\.replay$/i, "");
}

/**
 * Date plus the start and end clock time: two bare times on one line read as a duration and a
 * time, which is not what they are.
 * @param {DemoListItem} demo
 * @returns {string}
 */
function demoMeta(demo) {
  const stamp = demo.recordedAt || demo.lastWriteUtc;
  const start = stamp ? new Date(stamp) : null;
  if (!start || Number.isNaN(start.getTime())) return demo.cached ? "" : "not cached";
  const clock = (/** @type {Date} */ date) =>
    date.toLocaleTimeString(undefined, { hour: "2-digit", minute: "2-digit", hourCycle: "h23" });
  const end = demo.durationSeconds != null ? new Date(start.getTime() + demo.durationSeconds * 1000) : null;
  const day = start.toLocaleDateString(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
  const bits = [day, end ? `${clock(start)}-${clock(end)}` : clock(start)];
  if (!demo.cached && !demo.unreadable) bits.push("not cached");
  return bits.join(" · ");
}

/**
 * Kills and placement sit on the title line, right aligned, the way the game's own list shows
 * them.
 * @param {DemoListItem} demo
 * @returns {HTMLElement}
 */
function demoScore(demo) {
  const score = document.createElement("span");
  score.className = "demo-score";
  if (demo.elims != null) {
    const kills = document.createElement("span");
    kills.className = "demo-kills";
    kills.innerHTML = `${themedIcon("kill.svg", "Eliminations")}<span class="sr-only">Eliminations </span>${demo.elims}`;
    score.appendChild(kills);
  }
  if (demo.placement != null) {
    const place = document.createElement("span");
    place.className = "demo-place";
    const outOf = demo.totalPlayers != null ? `/${demo.totalPlayers}` : "";
    place.innerHTML = `<span class="sr-only">Placement </span>#${demo.placement}${outOf}`;
    score.appendChild(place);
  }
  return score;
}

/**
 * @param {DemoListItem} demo
 * @returns {boolean}
 */
function isCurrentDemo(demo) {
  const current = state.currentFile;
  return current !== null && demo.name.toLowerCase() === current.toLowerCase();
}

/** @returns {string} */
function unreadableText() {
  return "Our parser can't read this replay file, so its map, kills and placement are unknown. The game itself can still play it.";
}

/**
 * The file stays listed so it is not silently lost, but it cannot be opened; the icon carries the
 * explanation instead of bloating the date line.
 * @returns {HTMLElement}
 */
function demoHelp() {
  const help = document.createElement("span");
  help.className = "demo-help";
  help.setAttribute("role", "img");
  help.setAttribute("aria-label", unreadableText());
  help.title = unreadableText();
  help.innerHTML = themedIcon("help.svg", unreadableText());
  return help;
}

/**
 * Changes whenever the rows would look different, so the ten second refresh leaves a list the
 * user is scrolling alone.
 * @returns {string}
 */
function demoSignature() {
  const shown = state.visibleDemos;
  return `${shown.length}|${shown[0]?.name ?? ""}|${state.currentFile ?? ""}|${
    shown.filter((demo) => demo.cached).length
  }|${state.demosRev}|${state.linking}`;
}

/**
 * @param {DemoListItem} demo
 * @returns {HTMLElement}
 */
function demoRow(demo) {
  const li = document.createElement("li");
  li.className = "demo";
  if (isCurrentDemo(demo)) {
    li.classList.add("current");
    li.setAttribute("aria-current", "true");
  }
  li.title = demo.path;
  if (demo.unreadable) li.classList.add("unreadable");
  const row = document.createElement("div");
  row.className = "demo-row";
  /** @type {HTMLInputElement | null} */
  let check = null;
  if (state.selecting) {
    check = document.createElement("input");
    check.type = "checkbox";
    check.className = "check demo-check";
    check.checked = state.selected.has(demo.name);
    li.classList.toggle("selected", check.checked);
    check.setAttribute("aria-label", `Select ${demoTitle(demo)} ${demoMeta(demo)}`);
    row.append(check);
  }
  // A button, not a clickable li: it is what makes a replay reachable from the keyboard. Its
  // click bubbles to the li handler below, so there is still one code path.
  /** @type {HTMLElement} */
  let title;
  if (demo.unreadable) {
    title = document.createElement("span");
  } else {
    const button = document.createElement("button");
    button.type = "button";
    button.classList.add("reset-btn", "demo-open");
    title = button;
  }
  title.classList.add("demo-title");
  title.textContent = demoTitle(demo);
  row.append(title);
  const flag = demoFlag(demo);
  if (flag) row.append(flag);
  if (demo.unreadable) row.append(demoHelp());
  row.append(demoScore(demo));
  const meta = document.createElement("div");
  meta.className = "demo-meta";
  meta.textContent = demoMeta(demo);
  li.append(row, meta);
  if (check) {
    // Selecting turns every row, broken ones included, into a toggle instead of an opener.
    li.addEventListener("click", (ev) => {
      if (ev.target !== check) check.checked = !check.checked;
      if (check.checked) state.selected.add(demo.name);
      else state.selected.delete(demo.name);
      li.classList.toggle("selected", check.checked);
      updateSelectBar();
    });
  } else if (!demo.unreadable) {
    li.addEventListener("click", () => {
      setDemosOpen(false);
      openDemo(demo);
    });
  }
  return li;
}

/**
 * @param {number} count
 */
export function appendDemos(count) {
  const list = $("demos");
  const end = Math.min(state.visibleDemos.length, state.demosShown + count);
  for (let i = state.demosShown; i < end; i += 1) {
    list.appendChild(demoRow(state.visibleDemos[i]));
  }
  state.demosShown = end;
}

/**
 * A tall window fits the whole first page, so the panel never scrolls and no scroll event ever
 * pages in the rest. Keep adding rows until the list is taller than the panel.
 */
export function fillDemos() {
  const panel = $("demos-panel");
  if (panel.clientHeight === 0) return;
  while (state.demosShown < state.visibleDemos.length && panel.scrollHeight <= panel.clientHeight) {
    const shown = state.demosShown;
    appendDemos(DEMO_PAGE);
    if (state.demosShown === shown) break;
  }
}

/** Called when the panel scrolls near its end. */
export function pageInDemos() {
  const panel = $("demos-panel");
  if (panel.scrollTop + panel.clientHeight >= panel.scrollHeight - 240) {
    appendDemos(DEMO_PAGE);
    fillDemos();
  }
}

/**
 * Renders the page of rows that is already on screen. Rebuilding only what is visible keeps a
 * long list cheap and leaves the scroll position alone.
 */
export function renderDemos() {
  const signature = demoSignature();
  if (signature === state.demosSignature) return;
  state.demosSignature = signature;
  const panel = $("demos-panel");
  const scroll = panel.scrollTop;
  const target = Math.max(state.demosShown, DEMO_PAGE);
  $("demos").innerHTML = "";
  state.demosShown = 0;
  appendDemos(target);
  fillDemos();
  panel.scrollTop = scroll;
}

/** The sidebar marks come from the lookup list; asked each time the panel opens, never kept. */
async function loadLinkFlags() {
  state.lookups = await fetchLookups();
  refreshDemoFlags();
}

/** Redraws the rows after the flags changed. */
export function refreshDemoFlags() {
  state.demosRev += 1;
  renderDemos();
}

export async function loadDemos() {
  if (state.loadingDemos) return;
  state.loadingDemos = true;
  state.lastDemosRefresh = Date.now();
  clearTimeout(state.demosTimer);
  try {
    const res = await fetch("/api/demos");
    /** @type {DemosResponse} */
    const data = await res.json();
    state.demos = data.demos;
    state.backfill = data.backfill;
    const names = new Set(data.demos.map((demo) => demo.name));
    state.selected.forEach((name) => names.has(name) || state.selected.delete(name));
    syncMapChoices();
    renderAccounts();
    applyFilters();
  } catch {
    showStatus("Could not load the replay list.");
  } finally {
    state.loadingDemos = false;
    if (state.backfill) {
      state.demosTimer = setTimeout(loadDemos, 2000);
    }
  }
}

/** Re-filters the list and redraws it; every filter or selection-mode change comes through here. */
function applyFilters() {
  state.visibleDemos = filterDemos(state.demos, state.filters);
  // The selection follows what is on screen, so a hidden or filtered-out replay can never be
  // deleted by a button that only mentions what the user can see.
  const shown = new Set(state.visibleDemos.map((demo) => demo.name));
  state.selected.forEach((name) => shown.has(name) || state.selected.delete(name));
  $("demos-none").hidden = state.visibleDemos.length > 0 || state.demos.length === 0;
  $("demos-count").textContent = state.demos.length ? String(state.visibleDemos.length) : "";
  const count = activeFilterCount(state.filters);
  const badge = $("filter-count");
  badge.hidden = count === 0;
  badge.textContent = String(count);
  renderDemos();
  updateSelectBar();
}

/**
 * True when the replay should stay out of sight: its account is switched off. Used by the page
 * before it opens anything by itself, so the alt can never be the replay that shows on start.
 * @param {string | null | undefined} name
 * @returns {boolean}
 */
export function isDemoNameHidden(name) {
  if (!state.filters.hiddenAccounts.size) return false;
  const demo = state.demos.find((d) => d.name.toLowerCase() === String(name).toLowerCase());
  return isAccountHidden(demo, state.filters);
}

/** The replay to open when the page starts with nothing to show: the newest one the list shows. */
export function newestOpenableDemo() {
  return state.visibleDemos.find((demo) => !demo.unreadable) ?? null;
}

/** The map chips come from the replays themselves, so they only list maps that exist. */
function syncMapChoices() {
  const maps = mapChoices(state.demos);
  const box = $("filter-maps");
  if (box.dataset.maps === maps.join("|")) return;
  box.dataset.maps = maps.join("|");
  box.replaceChildren(
    ...maps.map((map) => {
      const label = document.createElement("label");
      label.className = "fchip";
      const input = document.createElement("input");
      input.type = "checkbox";
      input.dataset.filter = "maps";
      input.value = map;
      input.checked = state.filters.maps.has(map);
      const text = document.createElement("span");
      text.textContent = map;
      label.append(input, text);
      return label;
    })
  );
  $("filter-maps-group").hidden = maps.length === 0;
}

function readFilters() {
  const next = {
    ...emptyFilters(),
    hiddenAccounts: state.filters.hiddenAccounts,
    player: state.filters.player,
  };
  $("filters-form")
    .querySelectorAll("input[data-filter]")
    .forEach((node) => {
      const input = /** @type {HTMLInputElement} */ (node);
      const key = /** @type {"sizes" | "ranked" | "builds" | "maps"} */ (input.dataset.filter);
      if (input.checked) next[key].add(input.value);
    });
  next.from = /** @type {HTMLInputElement} */ ($("filter-from")).value;
  next.to = /** @type {HTMLInputElement} */ ($("filter-to")).value;
  next.minKills = /** @type {HTMLInputElement} */ ($("filter-min-kills")).value;
  next.maxKills = /** @type {HTMLInputElement} */ ($("filter-max-kills")).value;
  if (filtersKey(next) !== filtersKey(state.filters)) {
    state.filters = next;
    state.demosRev += 1;
    applyFilters();
  }
}

// Accounts -------------------------------------------------------------------------------------

const EYE =
  '<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12z"/><circle cx="12" cy="12" r="3"/></svg>';
const EYE_OFF =
  '<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M3 3l18 18M10.6 5.1A10 10 0 0 1 12 5c6.4 0 10 7 10 7a17 17 0 0 1-3.2 4M6.5 6.6C3.6 8.4 2 12 2 12s3.6 7 10 7c1.7 0 3.2-.4 4.5-1M9.9 9.9a3 3 0 0 0 4.2 4.2"/></svg>';
const MASK = "••••••••";

/** Names the user has revealed this visit; closing the panel masks them again. @type {Set<string>} */
const revealed = new Set();
/** @type {string | null} */
let pendingReveal = null;

function renderAccounts() {
  const accounts = accountChoices(state.demos);
  const key = JSON.stringify([
    accounts.map((a) => [a.id, a.count]),
    [...state.filters.hiddenAccounts],
    [...revealed],
  ]);
  const list = $("account-list");
  if (list.dataset.key === key) return;
  list.dataset.key = key;
  list.replaceChildren(
    ...accounts.map((account, index) => {
      const shown = !state.filters.hiddenAccounts.has(account.id);
      const open = revealed.has(account.id);
      const row = document.createElement("div");
      row.className = "account";
      const label = document.createElement("label");
      label.className = "check-label";
      const check = document.createElement("input");
      check.type = "checkbox";
      check.className = "check";
      check.checked = shown;
      check.dataset.account = account.id;
      // A masked account is announced by position, never by name.
      check.setAttribute(
        "aria-label",
        open ? `Show replays from ${account.name}` : `Show replays from account ${index + 1}`
      );
      const name = document.createElement("span");
      name.className = open ? "account-name" : "account-name masked";
      name.textContent = open ? account.name : MASK;
      label.append(check, name);
      const count = document.createElement("span");
      count.className = "account-count";
      count.textContent = String(account.count);
      const eye = document.createElement("button");
      eye.type = "button";
      eye.className = "icon-btn small";
      eye.dataset.eye = account.id;
      eye.setAttribute("aria-pressed", String(open));
      eye.setAttribute("aria-label", open ? "Hide account name" : "Show account name");
      eye.title = eye.getAttribute("aria-label") ?? "";
      eye.innerHTML = open ? EYE_OFF : EYE;
      row.append(label, count, eye);
      return row;
    })
  );
}

/** @param {string} id @param {boolean} shown */
function setAccountShown(id, shown) {
  const next = new Set(state.filters.hiddenAccounts);
  if (shown) next.delete(id);
  else next.add(id);
  state.filters = { ...state.filters, hiddenAccounts: next };
  saveHiddenAccounts(next);
  state.demosRev += 1;
  renderAccounts();
  applyFilters();
}

function closeAccountConfirm() {
  pendingReveal = null;
  $("account-confirm").hidden = true;
}

function onAccountClick(/** @type {Event} */ ev) {
  const button = /** @type {HTMLElement} */ (ev.target).closest("[data-eye]");
  if (!(button instanceof HTMLElement)) return;
  const id = button.dataset.eye ?? "";
  if (revealed.has(id)) {
    // Hiding a name needs no confirmation: it only makes the screen safer.
    revealed.delete(id);
    renderAccounts();
    return;
  }
  pendingReveal = id;
  $("account-confirm").hidden = false;
  $("account-confirm-no").focus();
}

function maskAccounts() {
  revealed.clear();
  closeAccountConfirm();
  renderAccounts();
}

/** Everything a streamer would not want left open: names masked, tools folded away. */
function collapseTools() {
  maskAccounts();
  setPanel("demos-settings", "toggle-settings", false);
  setPanel("demos-filters", "toggle-filters", false);
  setPanel("filter-accounts", "toggle-accounts", false);
  $("demos-confirm").hidden = true;
  if (state.selecting) setSelecting(false);
}

/**
 * Closing the sidebar leaves the search alone, so a second replay can be tried; only the user
 * closing the box (or its clear button) drops the term.
 * @param {boolean} open
 */
function setSearch(open) {
  setPanel("demos-search", "toggle-search", open);
  const input = /** @type {HTMLInputElement} */ ($("player-search"));
  if (open) input.focus();
  else if (state.filters.player) {
    input.value = "";
    setPlayerSearch("");
  }
}

/** @param {string} player */
function setPlayerSearch(player) {
  $("player-search-clear").hidden = player === "";
  state.filters = { ...state.filters, player };
  state.demosRev += 1;
  applyFilters();
}

/** @param {string} panelId @param {string} buttonId @param {boolean} open */
function setPanel(panelId, buttonId, open) {
  $(panelId).hidden = !open;
  $(buttonId).setAttribute("aria-expanded", String(open));
}

// Selecting and deleting -----------------------------------------------------------------------

function updateSelectBar() {
  const picked = state.selected.size;
  const all = /** @type {HTMLInputElement} */ ($("select-all"));
  all.checked = picked > 0 && picked === state.visibleDemos.length;
  all.indeterminate = picked > 0 && !all.checked;
  all.disabled = state.visibleDemos.length === 0;
  $("select-count").textContent = `${picked} selected`;
  /** @type {HTMLButtonElement} */ ($("delete-selected")).disabled = picked === 0;
}

/** @param {boolean} on */
function setSelecting(on) {
  state.selecting = on;
  state.selected.clear();
  $("toggle-select").setAttribute("aria-pressed", String(on));
  $("demos-select-bar").hidden = !on;
  $("demos-confirm").hidden = true;
  state.demosRev += 1;
  applyFilters();
}

function askDelete() {
  const n = state.selected.size;
  if (n === 0) return;
  const noun = n === 1 ? "replay" : "replays";
  $("confirm-text").textContent =
    `Delete ${n} ${noun}? This also deletes the actual ${noun === "replay" ? "replay file" : "replay files"} ` +
    "inside Fortnite and cannot be undone. Are you sure?";
  $("demos-confirm").hidden = false;
  $("confirm-no").focus();
}

async function deleteSelected() {
  $("demos-confirm").hidden = true;
  try {
    const res = await fetch("/api/demos/delete", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ names: [...state.selected] }),
    });
    if (!res.ok) throw new Error(String(res.status));
    /** @type {{ deleted: string[], failed: string[] }} */
    const result = await res.json();
    if (result.failed.length) {
      showStatus(`Could not delete ${result.failed.length} of the replays. Is Fortnite still using them?`);
    }
    state.selected = new Set(result.failed);
  } catch {
    showStatus("Could not delete the selected replays.");
  }
  loadDemos();
}

/** Wires the filter, account and selection tools; called once at startup. */
export function initDemoTools() {
  const form = /** @type {HTMLFormElement} */ ($("filters-form"));
  form.addEventListener("input", readFilters);
  form.addEventListener("submit", (ev) => ev.preventDefault());
  $("filters-reset").addEventListener("click", () => {
    form.reset();
    readFilters();
  });
  $("toggle-search").addEventListener("click", () => setSearch($("demos-search").hidden === true));
  $("player-search").addEventListener("input", (ev) =>
    setPlayerSearch(/** @type {HTMLInputElement} */ (ev.target).value)
  );
  $("player-search-clear").addEventListener("click", () => {
    /** @type {HTMLInputElement} */ ($("player-search")).value = "";
    setPlayerSearch("");
    $("player-search").focus();
  });
  $("toggle-settings").addEventListener("click", () =>
    setPanel("demos-settings", "toggle-settings", $("demos-settings").hidden === true)
  );
  $("toggle-filters").addEventListener("click", () =>
    setPanel("demos-filters", "toggle-filters", $("demos-filters").hidden === true)
  );
  $("toggle-accounts").addEventListener("click", () => {
    const open = $("filter-accounts").hidden === true;
    setPanel("filter-accounts", "toggle-accounts", open);
    if (!open) maskAccounts();
  });
  $("account-list").addEventListener("click", onAccountClick);
  $("account-list").addEventListener("change", (ev) => {
    const input = /** @type {HTMLInputElement} */ (ev.target);
    if (input.dataset.account) setAccountShown(input.dataset.account, input.checked);
  });
  $("account-confirm-yes").addEventListener("click", () => {
    if (pendingReveal) revealed.add(pendingReveal);
    closeAccountConfirm();
    renderAccounts();
  });
  $("account-confirm-no").addEventListener("click", closeAccountConfirm);
  $("toggle-select").addEventListener("click", () => setSelecting(!state.selecting));
  $("select-all").addEventListener("change", (ev) => {
    const on = /** @type {HTMLInputElement} */ (ev.target).checked;
    for (const demo of state.visibleDemos) {
      if (on) state.selected.add(demo.name);
      else state.selected.delete(demo.name);
    }
    state.demosRev += 1;
    renderDemos();
    updateSelectBar();
  });
  $("delete-selected").addEventListener("click", askDelete);
  $("confirm-yes").addEventListener("click", deleteSelected);
  $("confirm-no").addEventListener("click", () => {
    $("demos-confirm").hidden = true;
    $("delete-selected").focus();
  });
}

/**
 * @param {boolean} [force]
 */
export function refreshDemos(force) {
  // Skip while the page is hidden or another window (the game) has focus.
  if (document.visibilityState !== "visible" || !document.hasFocus()) return;
  if (!force && Date.now() - state.lastDemosRefresh < DEMO_REFRESH_MS) return;
  loadDemos();
}
