// @ts-check
// Owner-only streamer linking, the DOM and network side. Everything here is inert unless
// /api/config said `linking: true`: the page then asks the C# loopback routes (which hold the
// token) what F9 found per player and lets the owner correct it. The pure logic is in
// linking-model.js. Nothing is cached: every open asks again.
import { showStatus } from "./dom.js";
import { escapeHtml } from "./format.js";
import {
  attemptText,
  flagForDemo,
  flagText,
  indexPlayers,
  normId,
  platformName,
  removeLink,
  replayRequest,
  searchUrl,
  sourceName,
  upsertLink,
} from "./linking-model.js";
import { state } from "./state.js";

const LOOKUP_DAYS = 30;

const PENCIL =
  '<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M4 20h4L19 9l-4-4L4 16v4z"/><path d="M13.5 6.5l4 4"/></svg>';
const BROADCAST =
  '<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><circle cx="12" cy="12" r="2"/><path d="M16.2 7.8a6 6 0 0 1 0 8.4M7.8 16.2a6 6 0 0 1 0-8.4M19 5a10 10 0 0 1 0 14M5 19A10 10 0 0 1 5 5"/></svg>';

/**
 * @param {string} method
 * @param {string} path
 * @param {object} [body]
 * @returns {Promise<any>} the parsed JSON ({} for a 204)
 */
async function api(method, path, body) {
  let res;
  try {
    res = await fetch(path, {
      method,
      headers: body ? { "Content-Type": "application/json" } : undefined,
      body: body ? JSON.stringify(body) : undefined,
    });
  } catch {
    throw new Error("Could not reach Replay Viewer.");
  }
  /** @type {any} */
  const data = res.status === 204 ? {} : await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(data.error || `Request failed (${res.status})`);
  return data;
}

// Sidebar --------------------------------------------------------------------------------------

/** @returns {Promise<LinkingLookup[]>} */
export async function fetchLookups() {
  const since = Math.floor(Date.now() / 1000) - LOOKUP_DAYS * 86400;
  try {
    /** @type {{ lookups?: LinkingLookup[] }} */
    const data = await api("GET", `/api/linking/lookups?since=${since}`);
    return data.lookups ?? [];
  } catch (err) {
    showStatus(`Linking: ${err instanceof Error ? err.message : err}`, 6000);
    return [];
  }
}

/**
 * The mark for a replay whose F9 lookup found nobody, or only weak guesses.
 * @param {DemoListItem} demo
 * @returns {HTMLElement | null}
 */
export function demoFlag(demo) {
  if (!state.linking) return null;
  const flag = state.linkFlags.get(demo.name.toLowerCase()) ?? flagForDemo(demo, state.lookups);
  const text = flagText(flag);
  if (!text) return null;
  const el = document.createElement("span");
  el.className = `link-flag ${flag}`;
  el.setAttribute("role", "img");
  el.setAttribute("aria-label", text);
  el.title = text;
  el.innerHTML = BROADCAST;
  return el;
}

// The open replay ------------------------------------------------------------------------------

/**
 * Asks once for what is known about the players of this replay.
 * @param {MatchDocument} match
 * @returns {Promise<boolean>} true when new data was stored and the page should redraw
 */
export async function loadReplayLinks(match) {
  const file = match.sourceFile;
  if (!state.linking || state.linkLoading === file) return false;
  const body = replayRequest(match);
  if (body.epic_ids.length === 0) return false;
  state.linkLoading = file;
  try {
    /** @type {LinkingReplay} */
    const data = await api("POST", "/api/linking/replay", body);
    // The user may have opened another replay while this one was answering.
    if (state.match?.sourceFile !== file) return false;
    state.linkData = indexPlayers(data.players);
    state.linkFlags.set(file.toLowerCase(), data.lookup ? data.lookup.flag : "none");
    return true;
  } catch (err) {
    showStatus(`Linking: ${err instanceof Error ? err.message : err}`, 6000);
    return false;
  } finally {
    if (state.linkLoading === file) state.linkLoading = null;
  }
}

/** @param {string | null | undefined} epicId */
function entryFor(epicId) {
  return state.linkData?.get(normId(epicId));
}

/**
 * @param {string | null | undefined} epicId
 * @returns {string} chips for the player's links and F9 attempts
 */
function linkInfoMarkup(epicId) {
  const entry = entryFor(epicId);
  if (!entry) return "";
  const links = entry.links.map(
    (l) =>
      `<li class="lchip link">${escapeHtml(platformName(l.platform))} ${escapeHtml(l.handle)} <span class="lsrc">${escapeHtml(sourceName(l.source))}</span></li>`
  );
  const attempts = entry.attempts.map(
    (a) =>
      `<li class="lchip attempt${a.weak ? " weak" : ""}${a.status === "live" ? " live" : ""}">${escapeHtml(attemptText(a))}</li>`
  );
  const items = links.concat(attempts);
  return items.length
    ? `<ul class="linkinfo-list" aria-label="Streamer links and F9 attempts">${items.join("")}</ul>`
    : "";
}

/**
 * The pencil button and the slot for the player's links, for the roster row. Empty with linking off.
 * @param {RosterPlayer} player
 * @returns {{ button: string, slot: string }}
 */
export function linkingMarkup(player) {
  if (!state.linking || !player.epicId || player.isBot) return { button: "", slot: "" };
  const id = escapeHtml(normId(player.epicId));
  return {
    button: `<button type="button" class="icon-btn small link-edit" aria-label="Streamer links for ${escapeHtml(player.name)}" title="Streamer links">${PENCIL}</button>`,
    slot: `<div class="linkinfo" data-linkinfo="${id}">${linkInfoMarkup(player.epicId)}</div>`,
  };
}

/**
 * Wires the pencil of one roster row. The row itself opens the timeline, so stop that here.
 * @param {HTMLElement} row
 * @param {RosterPlayer} player
 */
export function bindLinkEdit(row, player) {
  row.querySelector(".link-edit")?.addEventListener("click", (ev) => {
    ev.stopPropagation();
    openLinkDialog(player);
  });
  row.querySelector(".linkinfo")?.addEventListener("click", (ev) => ev.stopPropagation());
}

/** Redraws every player's links from the page state, without rebuilding the roster. */
export function renderLinkInfo() {
  document.querySelectorAll("[data-linkinfo]").forEach((node) => {
    node.innerHTML = linkInfoMarkup(/** @type {HTMLElement} */ (node).dataset.linkinfo);
  });
}

// The panel ------------------------------------------------------------------------------------

/** @type {HTMLDialogElement | null} */
let dialog = null;
/** @type {RosterPlayer | null} */
let current = null;

/** Built on first use, so a page with linking off contains none of it. */
function ensureDialog() {
  if (dialog) return dialog;
  const el = document.createElement("dialog");
  el.className = "link-dialog";
  el.setAttribute("aria-labelledby", "link-title");
  el.innerHTML = `
    <h2 id="link-title"></h2>
    <div id="link-lists"></div>
    <p class="hint">Find them yourself, then paste their page below.</p>
    <p id="link-search" class="link-search"></p>
    <form id="link-form" class="link-form">
      <label for="link-url">Correct Twitch or TikTok URL</label>
      <input id="link-url" class="field" type="text" inputmode="url" autocomplete="off" spellcheck="false" placeholder="https://www.twitch.tv/name" />
      <div class="link-actions">
        <button type="submit" id="link-save" class="btn small solid">Save link</button>
        <button type="button" id="link-close" class="btn small">Close</button>
      </div>
    </form>
    <p id="link-msg" class="link-msg" role="status"></p>
  `;
  el.addEventListener("click", onDialogClick);
  el.querySelector("#link-form")?.addEventListener("submit", onSubmit);
  el.querySelector("#link-close")?.addEventListener("click", () => el.close());
  document.body.appendChild(el);
  dialog = el;
  return el;
}

/**
 * @param {string} id
 * @returns {HTMLElement}
 */
function part(id) {
  const node = ensureDialog().querySelector(`#${id}`);
  if (!node) throw new Error(`missing #${id}`);
  return /** @type {HTMLElement} */ (node);
}

/**
 * @param {string} text
 * @param {boolean} [isError]
 */
function setMessage(text, isError) {
  const msg = part("link-msg");
  msg.textContent = text;
  msg.classList.toggle("error", Boolean(isError));
  // A polite status for success, an assertive alert for a refused URL.
  msg.setAttribute("role", isError ? "alert" : "status");
}

function renderLists() {
  if (!current) return;
  const entry = entryFor(current.epicId);
  const links = (entry?.links ?? []).map(
    (l) =>
      `<li><span>${escapeHtml(platformName(l.platform))} <strong>${escapeHtml(l.handle)}</strong> <span class="lsrc">${escapeHtml(sourceName(l.source))}</span></span>
        <button type="button" class="btn small danger" data-act="delete" data-platform="${escapeHtml(l.platform)}" aria-label="Delete ${escapeHtml(platformName(l.platform))} link ${escapeHtml(l.handle)}">Delete</button></li>`
  );
  const attempts = (entry?.attempts ?? []).map((a, i) => {
    const wrong =
      a.status === "live" && !a.rejected
        ? `<button type="button" class="btn small" data-act="reject" data-index="${i}" aria-label="Wrong guess: ${escapeHtml(attemptText(a))}">Wrong guess</button>`
        : a.rejected
          ? `<span class="lsrc">rejected</span>`
          : "";
    return `<li><span>${escapeHtml(attemptText(a))}</span>${wrong}</li>`;
  });
  part("link-lists").innerHTML = `
    <h3>Links</h3>
    ${links.length ? `<ul class="link-rows">${links.join("")}</ul>` : `<p class="hint">No link yet.</p>`}
    ${attempts.length ? `<h3>F9 tried</h3><ul class="link-rows">${attempts.join("")}</ul>` : ""}
  `;
}

/**
 * @param {RosterPlayer} player
 */
export function openLinkDialog(player) {
  const el = ensureDialog();
  current = player;
  part("link-title").textContent = `Streamer links: ${player.name}`;
  part("link-search").innerHTML = ["twitch", "tiktok"]
    .map(
      (p) =>
        `<a href="${escapeHtml(searchUrl(/** @type {"twitch" | "tiktok"} */ (p), player.name))}" target="_blank" rel="noopener noreferrer">Search ${escapeHtml(platformName(p))} for ${escapeHtml(player.name)}</a>`
    )
    .join(" ");
  /** @type {HTMLInputElement} */ (part("link-url")).value = "";
  setMessage("");
  renderLists();
  el.showModal();
  part("link-url").focus();
}

export function closeLinkDialog() {
  if (dialog?.open) dialog.close();
}

/** @param {Event} ev */
async function onDialogClick(ev) {
  const button = /** @type {HTMLElement} */ (ev.target).closest("button[data-act]");
  if (!(button instanceof HTMLButtonElement) || !current || !state.linkData) return;
  const epicId = current.epicId;
  if (!epicId) return;
  const entry = entryFor(epicId);
  button.disabled = true;
  try {
    if (button.dataset.act === "delete") {
      const platform = String(button.dataset.platform);
      await api("DELETE", "/api/linking/links", { epic_id: epicId, platform });
      removeLink(state.linkData, epicId, platform);
      setMessage("Link deleted.");
    } else if (button.dataset.act === "reject") {
      const attempt = entry?.attempts[Number(button.dataset.index)];
      if (!attempt) return;
      await api("POST", "/api/linking/rejections", {
        epic_id: epicId,
        platform: attempt.platform,
        handle: attempt.handle ?? attempt.query,
      });
      attempt.rejected = true;
      setMessage("F9 will stop proposing that handle for this player.");
    }
    renderLists();
    renderLinkInfo();
    // The clicked button is gone; keep the keyboard user inside the panel.
    part("link-url").focus();
  } catch (err) {
    button.disabled = false;
    setMessage(err instanceof Error ? err.message : String(err), true);
  }
}

/** @param {Event} ev */
async function onSubmit(ev) {
  ev.preventDefault();
  const epicId = current?.epicId;
  if (!current || !epicId) return;
  const input = /** @type {HTMLInputElement} */ (part("link-url"));
  const save = /** @type {HTMLButtonElement} */ (part("link-save"));
  const url = input.value.trim();
  if (!url) {
    setMessage("Paste a Twitch or TikTok URL first.", true);
    input.focus();
    return;
  }
  save.disabled = true;
  try {
    /** @type {{ link: LinkingLink }} */
    const data = await api("POST", "/api/linking/links", {
      epic_id: epicId,
      url,
      epic_name: current.name || null,
    });
    state.linkData ??= new Map();
    upsertLink(state.linkData, data.link, epicId);
    input.value = "";
    setMessage(`Saved ${platformName(data.link.platform)} ${data.link.handle}.`);
    renderLists();
    renderLinkInfo();
  } catch (err) {
    setMessage(err instanceof Error ? err.message : String(err), true);
  } finally {
    save.disabled = false;
    input.focus();
  }
}
