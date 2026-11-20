// @ts-check
/** @typedef {"game" | "you" | "team"} Tab */
/** @typedef {"placement" | "kills"} SortMode */

/** @type {Record<string, { file: string, themed: boolean, label: string }>} */
const PLATFORM_ICONS = {
  PC: { file: "epic.svg", themed: false, label: "PC" },
  PlayStation: { file: "playstation.svg", themed: false, label: "PlayStation" },
  Xbox: { file: "xbox.svg", themed: false, label: "Xbox" },
  Switch: { file: "nintendo.svg", themed: false, label: "Switch" },
  iOS: { file: "apple.svg", themed: true, label: "iOS" },
  Android: { file: "android.svg", themed: false, label: "Android" },
};

/**
 * @type {{
 *   match: MatchDocument | null,
 *   tab: Tab,
 *   focusTeam: number | null,
 *   focusPlayerId: string | null,
 *   teamSelected: Set<string>,
 *   sort: SortMode,
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
const state = {
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

/**
 * Active state must reach assistive tech, not only the stylesheet.
 * @param {Element} el
 * @param {boolean} on
 */
function setPressed(el, on) {
  el.classList.toggle("active", on);
  el.setAttribute("aria-pressed", String(on));
}

/** @returns {ScrollBehavior} */
function scrollBehavior() {
  return matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth";
}

/**
 * @param {string} id
 * @returns {HTMLElement}
 */
const $ = (id) => {
  const el = document.getElementById(id);
  if (!el) throw new Error(`missing #${id}`);
  return el;
};

/** @type {boolean} */
let scraping = false;

/** A click that arrived while a scrape was running; the last one wins. */
/** @type {{ path: string, name: string | undefined, push: boolean } | null} */
let queuedScrape = null;

/**
 * The ids read through here are the page's checkboxes and search box.
 * @param {string} id
 * @returns {HTMLInputElement}
 */
const $input = (id) => /** @type {HTMLInputElement} */ ($(id));

/**
 * @returns {NodeListOf<HTMLButtonElement>}
 */
function tabButtons() {
  return /** @type {NodeListOf<HTMLButtonElement>} */ (document.querySelectorAll(".tabs button"));
}

/**
 * @param {string | null | undefined} a
 * @param {string | null | undefined} b
 * @returns {boolean}
 */
function idsEqual(a, b) {
  return Boolean(a && b && String(a).toLowerCase() === String(b).toLowerCase());
}

/**
 * @param {MatchDocument} match
 * @returns {string | null}
 */
function youId(match) {
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
function findPlayer(match, id) {
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
function yourTeam(match) {
  return match.teams.find((t) => t.players.some((p) => p.isYou)) ?? null;
}

/**
 * @param {MatchDocument} match
 * @param {number | null | undefined} index
 * @returns {TeamRoster | null}
 */
function teamByIndex(match, index) {
  // Number(null) is 0, so without this a missing index would resolve to team 0.
  if (index == null) return null;
  return match.teams.find((t) => t.teamIndex === Number(index)) ?? null;
}

/**
 * @param {TeamRoster | null | undefined} team
 * @param {string | null} [playerId]
 */
function selectSquad(team, playerId) {
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
function selectOnlyPlayer(team, playerId) {
  if (!team || !playerId) return;
  state.focusTeam = team.teamIndex;
  state.focusPlayerId = playerId;
  state.teamSelected = new Set([String(playerId).toLowerCase()]);
}

function openTeamTimeline() {
  state.tab = "team";
  tabButtons().forEach((b) => {
    setPressed(b, b.dataset.tab === "team");
  });
  render();
  requestAnimationFrame(() => {
    document.getElementById("timeline-section")?.scrollIntoView({
      behavior: scrollBehavior(),
      block: "start",
    });
  });
}

/**
 * @param {TeamRoster} team
 * @returns {number}
 */
function teamKills(team) {
  return (team.players || []).reduce((sum, p) => sum + (p.kills || 0), 0);
}

/**
 * @param {TeamRoster[]} teams
 * @param {SortMode} mode
 * @returns {TeamRoster[]}
 */
function sortTeams(teams, mode) {
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
function youEvents(match) {
  const you = youId(match);
  return match.events.filter((e) => idsEqual(e.actorId, you) || idsEqual(e.targetId, you));
}

/**
 * @param {MatchDocument} match
 * @returns {MatchEvent | undefined}
 */
function latestYouDeath(match) {
  const you = youId(match);
  return youEvents(match)
    .filter((e) => (e.kind === "finish" || e.kind === "selfElim") && idsEqual(e.targetId, you))
    .sort((a, b) => b.timeSeconds - a.timeSeconds)[0];
}

/**
 * @param {MatchDocument} match
 * @returns {MatchEvent[]}
 */
function filterEvents(match) {
  const you = youId(match);
  /** @type {MatchEvent[]} */
  let rows;
  if (state.tab === "game") {
    rows = match.events.filter((e) => e.kind === "knock" || e.kind === "finish");
  } else if (state.tab === "you") {
    rows = youEvents(match);
    const allowKills = $input("flt-kills").checked;
    const allowKnocks = $input("flt-knocks").checked;
    const allowDeaths = $input("flt-deaths").checked;
    rows = rows.filter((e) => {
      if (e.kind === "finish" && idsEqual(e.actorId, you)) return allowKills;
      if (e.kind === "knock" && (idsEqual(e.actorId, you) || idsEqual(e.targetId, you))) {
        return allowKnocks;
      }
      if ((e.kind === "finish" || e.kind === "selfElim") && idsEqual(e.targetId, you)) {
        return allowDeaths;
      }
      return e.kind === "revive";
    });
  } else {
    if (state.focusTeam == null) {
      selectSquad(yourTeam(match), you);
    }
    const teamIndex = state.focusTeam;
    rows = match.events.filter((e) => e.actorTeam === teamIndex || e.targetTeam === teamIndex);
    const allowKills = $input("team-kills").checked;
    const allowKnocks = $input("team-knocks").checked;
    const allowDeaths = $input("team-deaths").checked;
    rows = rows.filter((e) => {
      if (e.kind === "knock") return allowKnocks;
      if (e.kind === "finish" && e.actorTeam === teamIndex) return allowKills;
      if (e.kind === "finish" || e.kind === "selfElim") return allowDeaths;
      return true;
    });
    rows = rows.filter((e) => {
      const actorOn =
        e.actorTeam === teamIndex && state.teamSelected.has(String(e.actorId || "").toLowerCase());
      const targetOn =
        e.targetTeam === teamIndex && state.teamSelected.has(String(e.targetId || "").toLowerCase());
      return actorOn || targetOn;
    });
  }

  if ($input("hide-bots").checked) {
    rows = rows.filter((e) => keepWithHideBots(match, e, you));
  }
  if ($input("only-you").checked) {
    rows = rows.filter((e) => idsEqual(e.actorId, you) || idsEqual(e.targetId, you));
  }
  return rows.slice().sort((a, b) => a.timeSeconds - b.timeSeconds);
}

/**
 * @param {number | null | undefined} seconds
 * @returns {string}
 */
function formatTime(seconds) {
  const s = Math.max(0, Math.floor(seconds || 0));
  const m = Math.floor(s / 60);
  const r = String(s % 60).padStart(2, "0");
  return `${m}:${r}`;
}

/**
 * @param {string} text
 * @param {number} [autoHideMs]
 */
function showStatus(text, autoHideMs) {
  const el = $("status");
  el.hidden = !text;
  el.textContent = text || "";
  clearTimeout(state.statusTimer);
  if (text && autoHideMs) {
    state.statusTimer = setTimeout(() => showStatus(""), autoHideMs);
  }
}

/**
 * @param {boolean} open
 */
function setDemosOpen(open) {
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
 * @param {string} file
 * @param {string} title
 * @param {string} [extraClass]
 * @returns {string}
 */
function themedIcon(file, title, extraClass) {
  // The URL is set by the .icon-<name> rule in app.css. An inline style attribute would be
  // blocked by the page's Content-Security-Policy and the icon would render as a solid block.
  const name = file.replace(/\.svg$/, "");
  return `<span class="icon themed icon-${name} ${extraClass || ""}" title="${escapeHtml(title)}"></span>`;
}

/**
 * @param {string | null | undefined} platform
 * @returns {string}
 */
function platformMarkup(platform) {
  const spec = platform ? PLATFORM_ICONS[platform] : undefined;
  if (!spec) return "";
  if (spec.themed) {
    return `<span class="plat">${themedIcon(spec.file, spec.label)}</span>`;
  }
  return `<span class="plat"><img class="icon" src="/icons/${spec.file}" alt="${escapeHtml(spec.label)}" title="${escapeHtml(spec.label)}" /></span>`;
}

/**
 * @param {EventKind} kind
 * @param {string} side
 * @returns {string}
 */
function kindMarkup(kind, side) {
  const sideClass = side ? ` ${side}` : "";
  if (kind === "knock") return themedIcon("knock.svg", "Knock", `event knock${sideClass}`);
  if (kind === "finish" || kind === "selfElim") {
    return themedIcon("kill.svg", kind === "selfElim" ? "Self elim" : "Elim", `event kill${sideClass}`);
  }
  return `<span class="kind">${escapeHtml(kind)}</span>`;
}

/**
 * @param {MatchDocument} match
 * @param {MatchEvent} e
 * @returns {"taken" | "dealt" | ""}
 */
function eventSide(match, e) {
  if (e.kind !== "knock" && e.kind !== "finish" && e.kind !== "selfElim") {
    return "";
  }
  if (state.tab === "you") {
    const you = youId(match);
    const dealt = idsEqual(e.actorId, you);
    const taken = idsEqual(e.targetId, you);
    if (taken && !dealt) return "taken";
    if (dealt && !taken) return "dealt";
    if (taken) return "taken";
    return "";
  }
  if (state.tab === "team") {
    const team = Number(state.focusTeam);
    const actorOn = e.actorTeam === team && state.teamSelected.has(String(e.actorId || "").toLowerCase());
    const targetOn = e.targetTeam === team && state.teamSelected.has(String(e.targetId || "").toLowerCase());
    if (targetOn && !actorOn) return "taken";
    if (actorOn && !targetOn) return "dealt";
    if (targetOn) return "taken";
  }
  return "";
}

/**
 * @returns {string}
 */
function arrowMarkup() {
  return themedIcon("arrow.svg", "", "arrow");
}

/**
 * @param {string} name
 * @returns {string}
 */
function trackerUrl(name) {
  return "https://fortnitetracker.com/profile/search?q=" + encodeURIComponent(name).replaceAll("%20", "+");
}

/**
 * @param {string | null | undefined} name
 * @returns {string}
 */
function trackerButton(name) {
  if (!name) return "";
  return `<a class="tracker" aria-label="Fortnite Tracker" href="${escapeHtml(trackerUrl(name))}" target="_blank" rel="noopener noreferrer" title="Fortnite Tracker">${themedIcon("external.svg", "Fortnite Tracker")}</a>`;
}

/**
 * @param {string | null | undefined} id
 * @returns {string | null}
 */
function epicUuid(id) {
  const h = String(id).replaceAll("-", "").toLowerCase();
  if (!/^[0-9a-f]{32}$/.test(h)) return null;
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`;
}

/**
 * @param {string | null | undefined} epicId
 * @returns {string}
 */
function nameFnButton(epicId) {
  if (!state.nameFnPublicUrl || !epicId) return "";
  const q = epicUuid(epicId);
  if (!q) return "";
  const url = state.nameFnPublicUrl + "/search?q=" + encodeURIComponent(q);
  return `<a class="tracker namefn" aria-label="NameFN" href="${escapeHtml(url)}" target="_blank" rel="noopener noreferrer" title="NameFN">${themedIcon("external.svg", "NameFN")}</a>`;
}

/**
 * @param {number | null | undefined} value
 * @returns {string | null}
 */
function formatAccuracy(value) {
  if (value == null || Number.isNaN(Number(value))) return null;
  const n = Number(value);
  const pct = n <= 1 ? n * 100 : n;
  return `${pct.toFixed(0)}%`;
}

/**
 * @param {OwnerStats | null | undefined} stats
 * @returns {string}
 */
function ownerStatChips(stats) {
  if (!stats) return "";
  /** @type {string[]} */
  const bits = [];
  const acc = formatAccuracy(stats.accuracy);
  if (acc) bits.push(`Acc ${acc}`);
  if (stats.damageDealt != null) bits.push(`Dmg ${Math.round(stats.damageDealt)}`);
  if (stats.damageTaken != null) bits.push(`Taken ${stats.damageTaken}`);
  if (stats.assists != null) bits.push(`Ast ${stats.assists}`);
  if (stats.revives != null) bits.push(`Rev ${stats.revives}`);
  if (stats.eliminations != null) bits.push(`Elms ${stats.eliminations}`);
  if (bits.length === 0) return "";
  return bits.map((b) => `<span class="stat">${escapeHtml(b)}</span>`).join("");
}

/**
 * @param {number | string | null | undefined} teamIndex
 * @param {string | null | undefined} playerId
 */
function highlightPlayer(teamIndex, playerId) {
  state.focusPlayerId = playerId || null;
  if (teamIndex != null && teamIndex !== "") {
    const el = document.getElementById(`team-${teamIndex}`);
    if (el) {
      el.scrollIntoView({ behavior: scrollBehavior(), block: "center" });
      el.classList.add("flash");
      setTimeout(() => el.classList.remove("flash"), 1400);
    }
  }
  document.querySelectorAll(".player.highlight").forEach((n) => n.classList.remove("highlight"));
  if (playerId) {
    const row = document.querySelector(`[data-player="${cssEscape(String(playerId).toLowerCase())}"]`);
    if (row) {
      row.classList.add("highlight");
      row.scrollIntoView({ behavior: scrollBehavior(), block: "nearest" });
    }
  }
}

/**
 * @param {string} value
 * @returns {string}
 */
function cssEscape(value) {
  if (window.CSS && CSS.escape) return CSS.escape(value);
  return String(value).replace(/"/g, '\\"');
}

/**
 * @param {string | undefined} id
 * @returns {string}
 */
function prettyPlaylist(id) {
  if (!id) return "Unknown playlist";
  return id.replace(/^Playlist_/, "").replaceAll("_", " ");
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

const DEMO_PAGE = 20;

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
      scrape(demo.path, demo.name, true);
    });
  }
  return li;
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

/** @returns {string} */
function unreadableText() {
  return "Our parser can't read this replay file, so its map, kills and placement are unknown. The game itself can still play it.";
}

/**
 * @param {number} count
 */
function appendDemos(count) {
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
function fillDemos() {
  const panel = $("demos-panel");
  if (panel.clientHeight === 0) return;
  while (state.demosShown < state.demos.length && panel.scrollHeight <= panel.clientHeight) {
    const shown = state.demosShown;
    appendDemos(DEMO_PAGE);
    if (state.demosShown === shown) break;
  }
}

/**
 * Renders the page of rows that is already on screen. Rebuilding only what is visible keeps a
 * long list cheap and leaves the scroll position alone.
 */
function renderDemos() {
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

async function loadDemos() {
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
  } catch (err) {
    showStatus(String(err));
  } finally {
    state.loadingDemos = false;
    if (state.backfill) {
      state.demosTimer = setTimeout(loadDemos, 2000);
    }
  }
}

const DEMO_REFRESH_MS = 10000;

/**
 * @param {boolean} [force]
 */
function refreshDemos(force) {
  // Skip while the page is hidden or another window (the game) has focus.
  if (document.visibilityState !== "visible" || !document.hasFocus()) return;
  if (!force && Date.now() - state.lastDemosRefresh < DEMO_REFRESH_MS) return;
  loadDemos();
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
    const data = await res.json().catch(() => ({}));
    if (!res.ok) {
      state.currentFile = previousFile;
      setReplayUrl(previousFile);
      renderDemos();
      showStatus(data.error || `Scrape failed (${res.status})`);
      return;
    }
    const cached = res.headers.get("x-replay-cached") === "1";
    showStatus(cached ? "" : "Done", cached ? undefined : 2500);
    state.match = data;
    state.currentFile = data.sourceFile || name || null;
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
  } finally {
    clearTimeout(bannerDelay);
    scraping = false;
    const next = queuedScrape;
    queuedScrape = null;
    if (next) scrape(next.path, next.name, next.push);
  }
}

/**
 * @param {MatchDocument} match
 */
function renderTeamPlayerFilters(match) {
  const host = $("team-players");
  host.innerHTML = "";
  const team = teamByIndex(match, state.focusTeam) ?? yourTeam(match);
  if (!team) return;
  for (const p of team.players) {
    if (!p.epicId) continue;
    const key = p.epicId.toLowerCase();
    const label = document.createElement("label");
    const box = document.createElement("input");
    box.type = "checkbox";
    box.checked = state.teamSelected.has(key);
    box.addEventListener("change", () => {
      if (box.checked) state.teamSelected.add(key);
      else state.teamSelected.delete(key);
      renderTimeline();
    });
    label.append(box, document.createTextNode(" " + p.name));
    host.appendChild(label);
  }
}

function render() {
  const match = state.match;
  if (!match) return;
  const summary = $("summary");
  summary.hidden = false;
  const recorded = match.recordedAt ? new Date(match.recordedAt).toLocaleString() : "—";
  const youStats = ownerStatChips(match.ownerStats);
  summary.innerHTML = `
    <div class="match-chips">
      ${match.mapName ? `<span class="stat">${escapeHtml(match.mapName)}</span>` : ""}
      <span class="stat">${escapeHtml(recorded)}</span>
      ${match.teamPlacement != null ? `<span class="stat">Team place ${match.teamPlacement}</span>` : ""}
      ${youStats}
    </div>
  `;

  setPressed($("sort-placement"), state.sort === "placement");
  setPressed($("sort-kills"), state.sort === "kills");

  const q = $input("search").value.trim().toLowerCase();
  const showBots = $input("show-bots").checked;
  const roster = $("roster");
  roster.innerHTML = "";
  for (const team of sortTeams(match.teams, state.sort)) {
    const visiblePlayers = team.players.filter((p) => {
      if (!showBots && p.isBot) return false;
      if (q && !p.name.toLowerCase().includes(q)) return false;
      return true;
    });
    if (visiblePlayers.length === 0) continue;

    const card = document.createElement("article");
    card.className = "team";
    if (team.players.some((p) => p.isYou)) card.classList.add("yours");
    card.id = `team-${team.teamIndex}`;
    const place = team.placement != null ? `#${team.placement}` : "#?";
    card.innerHTML = `
      <h3 class="squad-head">
        <button type="button" class="reset-btn squad-open" aria-label="Squad ${place}, ${teamKills(team)} eliminations. Show timeline">
          <span class="place">${place}</span>
          <span class="team-elims">${themedIcon("kill.svg", "Elims", "kill")}${teamKills(team)}</span>
        </button>
      </h3>
    `;
    card.querySelector(".squad-head")?.addEventListener("click", () => {
      selectSquad(team);
      openTeamTimeline();
    });
    const sorted = visiblePlayers.slice().sort((a, b) => (b.kills || 0) - (a.kills || 0));
    for (const p of sorted) {
      const row = document.createElement("div");
      row.className = "player";
      if (p.epicId) row.dataset.player = p.epicId.toLowerCase();
      if (idsEqual(p.epicId, state.focusPlayerId)) row.classList.add("highlight");
      row.innerHTML = `
        <div>
          <div class="pname-row">
            <button type="button" class="reset-btn pname">${escapeHtml(p.name)}</button>
          </div>
          <div class="badges">
            ${platformMarkup(p.platform)}
            ${trackerButton(p.name)}
            ${nameFnButton(p.epicId)}
            ${p.isYou ? `<span class="badge you">you</span>` : ""}
            ${p.isBot ? `<span class="badge">bot</span>` : ""}
            ${p.fill === "fill" ? `<span class="badge fill">fill</span>` : ""}
          </div>
        </div>
        <div class="kills">${p.kills}</div>
      `;
      row.querySelectorAll(".tracker").forEach((el) => {
        el.addEventListener("click", (ev) => ev.stopPropagation());
      });
      row.addEventListener("click", () => {
        selectOnlyPlayer(team, p.epicId);
        openTeamTimeline();
      });
      card.appendChild(row);
    }
    roster.appendChild(card);
  }

  $("you-filters").hidden = state.tab !== "you";
  $("team-filters").hidden = state.tab !== "team";
  if (state.tab === "team") {
    if (state.focusTeam == null) selectSquad(yourTeam(match), youId(match));
    renderTeamPlayerFilters(match);
  }
  renderTimeline();
}

/**
 * @param {string | null | undefined} label
 * @returns {boolean}
 */
function isEnvironmentName(label) {
  return label === "Storm" || label === "Fall" || label === "Environment" || label === "Bleed out";
}

/**
 * @param {MatchDocument} match
 * @param {MatchEvent} e
 * @param {string | null} you
 * @returns {boolean}
 */
function keepWithHideBots(match, e, you) {
  const actorBot = findPlayer(match, e.actorId)?.player?.isBot === true;
  const targetBot = findPlayer(match, e.targetId)?.player?.isBot === true;
  if (idsEqual(e.actorId, you)) return !targetBot;
  if (idsEqual(e.targetId, you)) return !actorBot;
  if (!e.actorId && isEnvironmentName(e.actorName)) return !targetBot;
  return !actorBot && !targetBot;
}

/**
 * @param {string | null | undefined} label
 * @param {number | null | undefined} team
 * @param {string | null | undefined} id
 * @param {string} role
 * @returns {string}
 */
function nameButton(label, team, id, role) {
  const text = label || "—";
  if (text === "—") return `<span class="name-missing">${escapeHtml(text)}</span>`;
  if (!id && isEnvironmentName(text)) {
    return `<span class="name-env ${role}">${escapeHtml(text)}</span>`;
  }
  const teamAttr = team == null ? "" : String(team);
  return `<button type="button" class="name-link ${role}" data-team="${escapeHtml(teamAttr)}" data-id="${escapeHtml(id || "")}">${escapeHtml(text)}</button>`;
}

/**
 * @param {MatchDocument} match
 * @returns {string}
 */
function emptyTimelineMessage(match) {
  if (state.tab === "you" && youId(match) == null) {
    return "This replay doesn't record which player you were, so there is no personal timeline.";
  }

  if (state.tab === "team" && state.focusTeam == null) {
    return "Select a squad to see its timeline.";
  }

  return "Nothing recorded here.";
}

function renderTimeline() {
  const match = state.match;
  const ol = $("timeline");
  ol.innerHTML = "";
  if (!match) return;
  const death = latestYouDeath(match);
  const rows = filterEvents(match);
  const storm = match.safeZonesStartTime;
  let stormDone = storm == null;

  if (rows.length === 0) {
    // An empty list with no explanation reads as a broken page, so say why it is empty.
    const empty = document.createElement("li");
    empty.className = "empty";
    empty.textContent = emptyTimelineMessage(match);
    ol.appendChild(empty);
    return;
  }

  for (const e of rows) {
    if (!stormDone && storm != null && e.timeSeconds >= storm) {
      const marker = document.createElement("li");
      marker.className = "storm";
      marker.textContent = `${formatTime(storm)} Storm start`;
      ol.appendChild(marker);
      stormDone = true;
    }
    const li = document.createElement("li");
    const side = eventSide(match, e);
    const isLatestDeath = death && e === death && state.tab === "you";
    if (side) li.classList.add(`side-${side}`);
    li.innerHTML = `
      <time>${formatTime(e.timeSeconds)}</time>
      ${kindMarkup(e.kind, side)}
      <span class="names">
        ${nameButton(e.actorName, e.actorTeam, e.actorId, "actor")}
        ${arrowMarkup()}
        ${nameButton(e.targetName, e.targetTeam, e.targetId, "target")}
        ${isLatestDeath && e.actorTeam != null ? `<button type="button" class="their-team" data-team="${e.actorTeam}" data-id="${escapeHtml(e.actorId || "")}">Their team</button>` : ""}
      </span>
    `;
    /** @type {NodeListOf<HTMLElement>} */ (li.querySelectorAll(".name-link, .their-team")).forEach((btn) => {
      btn.addEventListener("click", () => {
        /** @type {string | number | undefined} */
        let team = btn.dataset.team;
        const id = btn.dataset.id;
        if (!team && id) {
          team = findPlayer(match, id)?.team?.teamIndex;
        }
        highlightPlayer(team, id);
      });
    });
    ol.appendChild(li);
  }
  if (!stormDone && storm != null) {
    const marker = document.createElement("li");
    marker.className = "storm";
    marker.textContent = `${formatTime(storm)} Storm start`;
    ol.appendChild(marker);
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

/**
 * @param {unknown} value
 * @returns {string}
 */
function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

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
$input("search").addEventListener("input", render);
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
$("demos-panel").addEventListener("scroll", () => {
  const panel = $("demos-panel");
  if (panel.scrollTop + panel.clientHeight >= panel.scrollHeight - 240) {
    appendDemos(DEMO_PAGE);
    fillDemos();
  }
});
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
  .then((cfg) => {
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
  .then((match) => {
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
