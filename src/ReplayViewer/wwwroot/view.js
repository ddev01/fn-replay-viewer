// @ts-check
// Renders the match: summary chips, roster cards and the timeline.
import { playerHits } from "./demo-filter.js";
import { $, $input, scrollBehavior, tabButtons, setPressed } from "./dom.js";
import { cssEscape, escapeHtml, formatTime, idsEqual } from "./format.js";
import {
  arrowMarkup,
  kindMarkup,
  nameButton,
  nameFnButton,
  ownerStatChips,
  platformMarkup,
  themedIcon,
  trackerButton,
} from "./markup.js";
import { bindLinkEdit, linkingMarkup } from "./linking.js";
import {
  emptyTimelineMessage,
  eventSide,
  filterEvents,
  findPlayer,
  latestYouDeath,
  sortTeams,
  teamByIndex,
  teamKills,
  youId,
  yourTeam,
} from "./model.js";
import { selectOnlyPlayer, selectSquad, state } from "./state.js";

/**
 * What the timeline filters need, read from the page controls and the page state.
 * @returns {import("./model.js").TimelineView}
 */
function readView() {
  return {
    tab: state.tab,
    focusTeam: state.focusTeam,
    teamSelected: state.teamSelected,
    you: {
      kills: $input("flt-kills").checked,
      knocks: $input("flt-knocks").checked,
      deaths: $input("flt-deaths").checked,
    },
    team: {
      kills: $input("team-kills").checked,
      knocks: $input("team-knocks").checked,
      deaths: $input("team-deaths").checked,
    },
    hideBots: state.hideBots,
    onlyYou: $input("only-you").checked,
  };
}

export function openTeamTimeline() {
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

/** The match the page last scrolled to a found player in, so a re-render does not scroll again. */
/** @type {MatchDocument | null} */
let scrolledToFound = null;

export function render() {
  const match = state.match;
  if (!match) return;
  $("empty").hidden = true;
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

  const roster = $("roster");
  roster.innerHTML = "";
  // The sidebar search: whoever it matched in this replay gets a red pulse so the eye lands on them.
  const found = new Set(
    playerHits(
      state.filters.player,
      match.teams.flatMap((t) => t.players.filter((p) => !p.isYou).map((p) => p.name))
    ).names
  );
  for (const team of sortTeams(match.teams, state.sort)) {
    const visiblePlayers = team.players.filter((p) => {
      return !(state.hideBots && p.isBot);
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
      if (found.has(p.name) && !p.isYou) row.classList.add("found");
      const linking = linkingMarkup(p);
      row.innerHTML = `
        <div>
          <div class="pname-row">
            <button type="button" class="reset-btn pname">${escapeHtml(p.name)}</button>
          </div>
          <div class="badges">
            ${platformMarkup(p.platform)}
            ${trackerButton(p.name)}
            ${nameFnButton(p.epicId)}
            ${linking.button}
            ${p.isYou ? `<span class="badge you">you</span>` : ""}
            ${p.isBot ? `<span class="badge">bot</span>` : ""}
            ${p.fill === "fill" ? `<span class="badge fill">fill</span>` : ""}
          </div>
          ${linking.slot}
        </div>
        <div class="kills">${p.kills}</div>
      `;
      bindLinkEdit(row, p);
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
  if (match !== scrolledToFound) {
    scrolledToFound = match;
    roster.querySelector(".found")?.scrollIntoView({ behavior: scrollBehavior(), block: "center" });
  }

  $("you-filters").hidden = state.tab !== "you";
  $("team-filters").hidden = state.tab !== "team";
  if (state.tab === "team") {
    if (state.focusTeam == null) selectSquad(yourTeam(match), youId(match));
    renderTeamPlayerFilters(match);
  }
  renderTimeline();
}

export function renderTimeline() {
  const match = state.match;
  const ol = $("timeline");
  ol.innerHTML = "";
  if (!match) return;
  if (state.tab === "team" && state.focusTeam == null) {
    selectSquad(yourTeam(match), youId(match));
  }
  const view = readView();
  const death = state.tab === "you" ? latestYouDeath(match) : undefined;
  const rows = filterEvents(match, view);
  const storm = match.safeZonesStartTime;
  let stormDone = storm == null;

  if (rows.length === 0) {
    // An empty list with no explanation reads as a broken page, so say why it is empty.
    const empty = document.createElement("li");
    empty.className = "empty";
    empty.textContent = emptyTimelineMessage(match, view);
    ol.appendChild(empty);
    return;
  }

  /** @param {number} seconds */
  const stormMarker = (seconds) => {
    const marker = document.createElement("li");
    marker.className = "storm";
    marker.textContent = `${formatTime(seconds)} Storm start`;
    ol.appendChild(marker);
  };

  for (const e of rows) {
    if (!stormDone && storm != null && e.timeSeconds >= storm) {
      stormMarker(storm);
      stormDone = true;
    }
    const li = document.createElement("li");
    const side = eventSide(match, e, view);
    if (side) li.classList.add(`side-${side}`);
    li.innerHTML = `
      <time>${formatTime(e.timeSeconds)}</time>
      ${kindMarkup(e.kind, side)}
      <span class="names">
        ${nameButton(e.actorName, e.actorTeam, e.actorId, "actor")}
        ${arrowMarkup()}
        ${nameButton(e.targetName, e.targetTeam, e.targetId, "target")}
        ${death && e === death && e.actorTeam != null ? `<button type="button" class="their-team" data-team="${e.actorTeam}" data-id="${escapeHtml(e.actorId || "")}">Their team</button>` : ""}
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
  if (!stormDone && storm != null) stormMarker(storm);
}
