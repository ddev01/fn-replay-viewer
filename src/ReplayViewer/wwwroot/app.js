// @ts-check
// Page bootstrap: scraping a replay, the URL, the update banner and event wiring. The match logic is
// in model.js, rendering in view.js and the Demos panel in demos.js.
import { $, $input, setPressed, showStatus, tabButtons } from "./dom.js";
import { loadDemos, onOpenDemo, pageInDemos, refreshDemos, renderDemos, setDemosOpen } from "./demos.js";
import { idsEqual } from "./format.js";
import { render, renderTimeline } from "./view.js";
import { state } from "./state.js";

const REPLAY_PARAM = "replay";

/** @returns {string | null} */
function replayFromUrl() {
  return new URLSearchParams(location.search).get(REPLAY_PARAM);
}

/**
 * Keeps the address bar on the replay being viewed, so back, forward and the browser's history
 * can reach it. push adds an entry; the default replaces the current one.
 * @param {string | null} name
 * @param {boolean} [push]
 */
function setReplayUrl(name, push) {
  const url = new URL(location.href);
  if (name) url.searchParams.set(REPLAY_PARAM, name);
  else url.searchParams.delete(REPLAY_PARAM);
  if (url.href === location.href) return;
  history[push ? "pushState" : "replaceState"](null, "", url);
}

/** @type {boolean} */
let scraping = false;

/**
 * A click that arrived while a scrape was running; the last one wins.
 * @type {{ path: string, name: string | undefined, push: boolean } | null}
 */
let queuedScrape = null;

/**
 * With no match on screen a failed scrape would leave a blank page and one status line, so say what
 * happened and offer the two ways forward.
 * @param {string} message
 */
function showEmpty(message) {
  if (state.match) return;
  $("empty-text").textContent = message;
  $("empty").hidden = false;
}

/**
 * @param {string} path
 * @param {string} [name]
 * @param {boolean} [push] add a history entry instead of replacing the current one
 */
async function scrape(path, name, push) {
  // Two overlapping scrapes race: whichever response arrives last wins, so the roster can
  // show a different demo than the one clicked last. Guard here, not on the button, because
  // the Demos panel calls this directly. Back and forward are real navigation, so the last one
  // is run once the current scrape finishes rather than dropped.
  if (scraping) {
    queuedScrape = { path, name, push: push === true };
    return;
  }
  scraping = true;
  const previousFile = state.currentFile;
  if (name) {
    state.currentFile = name;
    renderDemos();
  }
  // A cached match comes back in ~18ms; flashing "Scraping…" for that reads as a glitch, so
  // the banner only appears once the wait is long enough to notice.
  const bannerDelay = setTimeout(() => showStatus("Scraping…"), 250);
  try {
    const res = await fetch("/api/scrape", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ path: path || "" }),
    });
    /** @type {Partial<MatchDocument> & Partial<ApiError>} */
    const data = await res.json().catch(() => ({}));
    if (!res.ok) {
      state.currentFile = previousFile;
      setReplayUrl(previousFile);
      renderDemos();
      const message = data.error || `Scrape failed (${res.status})`;
      showStatus(message);
      showEmpty(message);
      return;
    }
    const cached = res.headers.get("x-replay-cached") === "1";
    showStatus(cached ? "" : "Done", cached ? undefined : 2500);
    state.match = /** @type {MatchDocument} */ (data);
    state.currentFile = state.match.sourceFile || name || null;
    state.focusTeam = null;
    state.focusPlayerId = null;
    state.teamSelected = new Set();
    setReplayUrl(state.currentFile, push);
    render();
    renderDemos();
    loadDemos();
  } catch {
    // The app exited or the listener is gone; leaving "Scraping…" up would be a lie.
    showStatus("Could not reach Replay Viewer.");
    showEmpty("Could not reach Replay Viewer. Is it still running in the tray?");
  } finally {
    clearTimeout(bannerDelay);
    scraping = false;
    const next = queuedScrape;
    queuedScrape = null;
    if (next) scrape(next.path, next.name, next.push);
  }
}

/**
 * Shows the update banner when the app has an update waiting. Silent updates apply on the next
 * launch, so this is the way to take one immediately.
 */
async function loadUpdateBanner() {
  try {
    const res = await fetch("/api/update");
    if (!res.ok) return;
    /** @type {UpdateInfo} */
    const info = await res.json();
    if (!info.staged && !info.available) return;
    const version = info.stagedVersion || info.available || "";
    $("update-title").textContent = info.staged
      ? `Version ${version} is ready to install`
      : `Version ${version} is available`;
    $("update-detail").textContent = info.staged
      ? "It installs by itself the next time Replay Viewer starts."
      : "Downloading in the background.";
    $("update-banner").hidden = false;
  } catch {
    /* the banner is optional: the tray still handles updates */
  }
}

onOpenDemo((demo) => scrape(demo.path, demo.name, true));

$("update-apply").addEventListener("click", async () => {
  const button = /** @type {HTMLButtonElement} */ ($("update-apply"));
  button.disabled = true;
  try {
    const res = await fetch("/api/update/apply", { method: "POST" });
    if (!res.ok) {
      $("update-detail").textContent = "No update is ready yet.";
      button.disabled = false;
      return;
    }
    $("update-detail").textContent = "Restarting Replay Viewer…";
  } catch {
    $("update-detail").textContent = "Could not reach Replay Viewer.";
    button.disabled = false;
  }
});
$("empty-retry").addEventListener("click", () => {
  $("empty").hidden = true;
  scrape(replayFromUrl() || "");
});
$("empty-demos").addEventListener("click", () => setDemosOpen(true));

// Rebuilding the roster on every keystroke is wasted work while someone is still typing.
/** @type {number | undefined} */
let searchTimer;
$input("search").addEventListener("input", () => {
  clearTimeout(searchTimer);
  searchTimer = setTimeout(render, 100);
});
$input("show-bots").addEventListener("change", render);
$("sort-placement").addEventListener("click", () => {
  state.sort = "placement";
  render();
});
$("sort-kills").addEventListener("click", () => {
  state.sort = "kills";
  render();
});
$("toggle-demos").addEventListener("click", () => setDemosOpen(!state.demosOpen));
$("close-demos").addEventListener("click", () => setDemosOpen(false));
$("demos-backdrop").addEventListener("click", () => setDemosOpen(false));
$("demos-panel").addEventListener("scroll", pageInDemos);
[
  "hide-bots",
  "only-you",
  "flt-kills",
  "flt-knocks",
  "flt-deaths",
  "team-kills",
  "team-knocks",
  "team-deaths",
].forEach((id) => $input(id).addEventListener("change", renderTimeline));
tabButtons().forEach((btn) => {
  btn.addEventListener("click", () => {
    const tab = btn.dataset.tab;
    if (tab === "game" || tab === "you" || tab === "team") state.tab = tab;
    tabButtons().forEach((b) => {
      setPressed(b, b === btn);
    });
    render();
  });
});

document.addEventListener("keydown", (ev) => {
  if (ev.key === "Escape" && state.demosOpen) setDemosOpen(false);
});
window.addEventListener("focus", () => refreshDemos(true));
document.addEventListener("visibilitychange", () => refreshDemos(true));
document.addEventListener("mousemove", () => refreshDemos(false));
document.addEventListener("wheel", () => refreshDemos(false));
document.addEventListener("keydown", () => refreshDemos(false));

fetch("/api/config")
  .then((res) => (res.ok ? res.json() : null))
  .then((/** @type {ApiConfig | null} */ cfg) => {
    if (cfg && cfg.nameFnPublicUrl) state.nameFnPublicUrl = String(cfg.nameFnPublicUrl).replace(/\/$/, "");
  })
  .catch(() => {
    /* config is optional: no NameFN links when the endpoint is down */
  });
loadDemos();
loadUpdateBanner();
// Show what the server already has, so reloading stays on the same replay. The URL wins when it
// names a replay: that covers a browser restart, where the server has nothing cached yet. Only
// when neither exists does the page pick the newest finished replay by itself.
fetch("/api/match")
  .then((res) => (res.ok ? res.json() : null))
  .then((/** @type {MatchDocument | null} */ match) => {
    if (match) {
      state.match = match;
      state.currentFile = match.sourceFile || null;
      render();
      renderDemos();
    }
    const wanted = replayFromUrl();
    if (match && (!wanted || idsEqual(match.sourceFile, wanted))) {
      setReplayUrl(state.currentFile);
      return undefined;
    }

    return scrape(wanted || "");
  })
  .catch(() => {
    /* the listener is gone; scrape() already reports that on the status line */
  });

window.addEventListener("popstate", () => scrape(replayFromUrl() || ""));
