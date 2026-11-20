// @ts-check
// The Demos panel: the replay list, its paging, its refresh rules and its dialog behaviour.
import { $, showStatus } from "./dom.js";
import { prettyPlaylist } from "./format.js";
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
    // The rows rendered while the panel was hidden could not measure it.
    fillDemos();
    $("close-demos").focus();
  } else if (wasOpen) {
    $("toggle-demos").focus();
  }
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
  return `${state.demos.length}|${state.demos[0]?.name ?? ""}|${
    state.currentFile ?? ""
  }|${state.demos.filter((demo) => demo.cached).length}`;
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
  if (demo.unreadable) row.append(demoHelp());
  row.append(demoScore(demo));
  const meta = document.createElement("div");
  meta.className = "demo-meta";
  meta.textContent = demoMeta(demo);
  li.append(row, meta);
  if (!demo.unreadable) {
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
  const end = Math.min(state.demos.length, state.demosShown + count);
  for (let i = state.demosShown; i < end; i += 1) {
    list.appendChild(demoRow(state.demos[i]));
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
  while (state.demosShown < state.demos.length && panel.scrollHeight <= panel.clientHeight) {
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
    renderDemos();
  } catch {
    showStatus("Could not load the replay list.");
  } finally {
    state.loadingDemos = false;
    if (state.backfill) {
      state.demosTimer = setTimeout(loadDemos, 2000);
    }
  }
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
