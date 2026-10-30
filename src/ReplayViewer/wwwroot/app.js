const PLATFORM_ICONS = {
  PC: { file: "epic.svg", themed: false, label: "PC" },
  PlayStation: { file: "playstation.svg", themed: false, label: "PlayStation" },
  Xbox: { file: "xbox.svg", themed: false, label: "Xbox" },
  Switch: { file: "nintendo.svg", themed: false, label: "Switch" },
  iOS: { file: "apple.svg", themed: true, label: "iOS" },
  Android: { file: "android.svg", themed: false, label: "Android" },
};

const state = {
  match: null,
  tab: "you",
  focusTeam: null,
  focusPlayerId: null,
  teamSelected: new Set(),
  sort: "placement",
  demosOpen: false,
  nameFnPublicUrl: null,
};

const $ = (id) => document.getElementById(id);

function idsEqual(a, b) {
  return a && b && String(a).toLowerCase() === String(b).toLowerCase();
}

function youId(match) {
  for (const team of match.teams) {
    for (const p of team.players) {
      if (p.isYou) return p.epicId;
    }
  }
  return null;
}

function findPlayer(match, id) {
  for (const team of match.teams) {
    for (const p of team.players) {
      if (idsEqual(p.epicId, id)) return { team, player: p };
    }
  }
  return null;
}

function yourTeam(match) {
  return match.teams.find((t) => t.players.some((p) => p.isYou)) ?? null;
}

function teamByIndex(match, index) {
  return match.teams.find((t) => t.teamIndex === Number(index)) ?? null;
}

function selectSquad(team, playerId) {
  if (!team) return;
  state.focusTeam = team.teamIndex;
  state.focusPlayerId = playerId || null;
  state.teamSelected = new Set(
    team.players.filter((p) => p.epicId).map((p) => String(p.epicId).toLowerCase())
  );
}

function selectOnlyPlayer(team, playerId) {
  if (!team || !playerId) return;
  state.focusTeam = team.teamIndex;
  state.focusPlayerId = playerId;
  state.teamSelected = new Set([String(playerId).toLowerCase()]);
}

function openTeamTimeline() {
  state.tab = "team";
  document.querySelectorAll(".tabs button").forEach((b) => {
    b.classList.toggle("active", b.dataset.tab === "team");
  });
  render();
  requestAnimationFrame(() => {
    document.getElementById("timeline-section")?.scrollIntoView({
      behavior: "smooth",
      block: "start",
    });
  });
}

function isHuman(match, id) {
  const found = findPlayer(match, id);
  return Boolean(found && !found.player.isBot);
}

function teamKills(team) {
  return (team.players || []).reduce((sum, p) => sum + (p.kills || 0), 0);
}

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

function youEvents(match) {
  const you = youId(match);
  return match.events.filter((e) => idsEqual(e.actorId, you) || idsEqual(e.targetId, you));
}

function latestYouDeath(match) {
  const you = youId(match);
  return youEvents(match)
    .filter(
      (e) =>
        (e.kind === "finish" || e.kind === "selfElim") && idsEqual(e.targetId, you)
    )
    .sort((a, b) => b.timeSeconds - a.timeSeconds)[0];
}

function filterEvents(match) {
  const you = youId(match);
  let rows;
  if (state.tab === "game") {
    rows = match.events.filter((e) => e.kind === "knock" || e.kind === "finish");
  } else if (state.tab === "you") {
    rows = youEvents(match);
    const allowKills = $("flt-kills").checked;
    const allowKnocks = $("flt-knocks").checked;
    const allowDeaths = $("flt-deaths").checked;
    rows = rows.filter((e) => {
      if (e.kind === "finish" && idsEqual(e.actorId, you)) return allowKills;
      if (
        e.kind === "knock" &&
        (idsEqual(e.actorId, you) || idsEqual(e.targetId, you))
      ) {
        return allowKnocks;
      }
      if (
        (e.kind === "finish" || e.kind === "selfElim") &&
        idsEqual(e.targetId, you)
      ) {
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
    const allowKills = $("team-kills").checked;
    const allowKnocks = $("team-knocks").checked;
    const allowDeaths = $("team-deaths").checked;
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

  if ($("hide-bots").checked) {
    rows = rows.filter((e) => keepWithHideBots(match, e, you));
  }
  if ($("only-you").checked) {
    rows = rows.filter((e) => idsEqual(e.actorId, you) || idsEqual(e.targetId, you));
  }
  return rows.slice().sort((a, b) => a.timeSeconds - b.timeSeconds);
}

function formatTime(seconds) {
  const s = Math.max(0, Math.floor(seconds || 0));
  const m = Math.floor(s / 60);
  const r = String(s % 60).padStart(2, "0");
  return `${m}:${r}`;
}

function showStatus(text) {
  const el = $("status");
  el.hidden = !text;
  el.textContent = text || "";
}

function setDemosOpen(open) {
  state.demosOpen = open;
  $("demos-panel").hidden = !open;
  $("demos-backdrop").hidden = !open;
  $("toggle-demos").setAttribute("aria-expanded", String(open));
}

function anonymousLabel(player) {
  if (!player.anonymous) return "";
  if (player.playerNumber != null) return `anonymous ${player.playerNumber}`;
  return "anonymous";
}

function themedIcon(file, title, extraClass) {
  return `<span class="icon themed ${extraClass || ""}" style="--icon:url('/icons/${file}')" title="${escapeHtml(title)}"></span>`;
}

function platformMarkup(platform) {
  const spec = PLATFORM_ICONS[platform];
  if (!spec) return "";
  if (spec.themed) {
    return `<span class="plat">${themedIcon(spec.file, spec.label)}</span>`;
  }
  return `<span class="plat"><img class="icon" src="/icons/${spec.file}" alt="${escapeHtml(spec.label)}" title="${escapeHtml(spec.label)}" /></span>`;
}

function kindMarkup(kind, side) {
  const sideClass = side ? ` ${side}` : "";
  if (kind === "knock") return themedIcon("knock.svg", "Knock", `event knock${sideClass}`);
  if (kind === "finish" || kind === "selfElim") {
    return themedIcon("kill.svg", kind === "selfElim" ? "Self elim" : "Elim", `event kill${sideClass}`);
  }
  return `<span class="kind">${escapeHtml(kind)}</span>`;
}

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
    const actorOn =
      e.actorTeam === team && state.teamSelected.has(String(e.actorId || "").toLowerCase());
    const targetOn =
      e.targetTeam === team && state.teamSelected.has(String(e.targetId || "").toLowerCase());
    if (targetOn && !actorOn) return "taken";
    if (actorOn && !targetOn) return "dealt";
    if (targetOn) return "taken";
  }
  return "";
}

function arrowMarkup() {
  return themedIcon("arrow.svg", "", "arrow");
}

function trackerUrl(name) {
  return (
    "https://fortnitetracker.com/profile/search?q=" +
    encodeURIComponent(name).replaceAll("%20", "+")
  );
}

function trackerButton(name) {
  if (!name) return "";
  return `<a class="tracker" href="${escapeHtml(trackerUrl(name))}" target="_blank" rel="noopener noreferrer" title="Fortnite Tracker">${themedIcon("external.svg", "Fortnite Tracker")}</a>`;
}

function epicUuid(id) {
  const h = String(id).replaceAll("-", "").toLowerCase();
  if (!/^[0-9a-f]{32}$/.test(h)) return null;
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`;
}

function nameFnButton(epicId) {
  if (!state.nameFnPublicUrl || !epicId) return "";
  const q = epicUuid(epicId);
  if (!q) return "";
  const url = state.nameFnPublicUrl + "/search?q=" + encodeURIComponent(q);
  return `<a class="tracker namefn" href="${escapeHtml(url)}" target="_blank" rel="noopener noreferrer" title="NameFN">${themedIcon("external.svg", "NameFN")}</a>`;
}

function formatAccuracy(value) {
  if (value == null || Number.isNaN(Number(value))) return null;
  const n = Number(value);
  const pct = n <= 1 ? n * 100 : n;
  return `${pct.toFixed(0)}%`;
}

function ownerStatChips(stats) {
  if (!stats) return "";
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

function highlightPlayer(teamIndex, playerId) {
  state.focusPlayerId = playerId || null;
  if (teamIndex != null && teamIndex !== "") {
    const el = document.getElementById(`team-${teamIndex}`);
    if (el) {
      el.scrollIntoView({ behavior: "smooth", block: "center" });
      el.classList.add("flash");
      setTimeout(() => el.classList.remove("flash"), 1400);
    }
  }
  document.querySelectorAll(".player.highlight").forEach((n) => n.classList.remove("highlight"));
  if (playerId) {
    const row = document.querySelector(`[data-player="${cssEscape(String(playerId).toLowerCase())}"]`);
    if (row) {
      row.classList.add("highlight");
      row.scrollIntoView({ behavior: "smooth", block: "nearest" });
    }
  }
}

function cssEscape(value) {
  if (window.CSS && CSS.escape) return CSS.escape(value);
  return String(value).replace(/"/g, '\\"');
}

async function loadDemos() {
  const res = await fetch("/api/demos");
  const demos = await res.json();
  const ul = $("demos");
  ul.innerHTML = "";
  for (const demo of demos) {
    const li = document.createElement("li");
    li.textContent = demo.name.replace(/\.replay$/i, "");
    li.title = demo.path;
    li.addEventListener("click", () => {
      setDemosOpen(false);
      scrape(demo.path);
    });
    ul.appendChild(li);
  }
}

async function scrape(path) {
  showStatus("Scraping…");
  const res = await fetch("/api/scrape", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ path: path || "" }),
  });
  const data = await res.json().catch(() => ({}));
  if (!res.ok) {
    showStatus(data.error || `Scrape failed (${res.status})`);
    return;
  }
  showStatus("");
  state.match = data;
  state.focusTeam = null;
  state.focusPlayerId = null;
  state.teamSelected = new Set();
  $("export").disabled = false;
  render();
}

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
      <span class="chip">${escapeHtml(prettyPlaylist(match.playlist))}</span>
      <span class="chip">Team size ${match.teamSize ?? "—"}</span>
      <span class="chip">${escapeHtml(recorded)}</span>
      <span class="chip">Your place ${match.yourPlacement ?? "—"}</span>
      <span class="chip">Team place ${match.teamPlacement ?? "—"}</span>
    </div>
    ${youStats ? `<div class="owner-stats">${youStats}</div>` : ""}
  `;

  $("sort-placement").classList.toggle("active", state.sort === "placement");
  $("sort-kills").classList.toggle("active", state.sort === "kills");

  const q = $("search").value.trim().toLowerCase();
  const showBots = $("show-bots").checked;
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
    const place = team.placement != null ? `#${team.placement}` : "unplaced";
    card.innerHTML = `
      <h3 class="squad-head"><span>Squad</span><span class="place">${place}</span></h3>
      <div class="team-meta">${teamKills(team)} elims</div>
    `;
    card.querySelector(".squad-head").addEventListener("click", () => {
      selectSquad(team);
      openTeamTimeline();
    });
    const sorted = visiblePlayers.slice().sort((a, b) => (b.kills || 0) - (a.kills || 0));
    for (const p of sorted) {
      const row = document.createElement("div");
      row.className = "player";
      if (p.epicId) row.dataset.player = p.epicId.toLowerCase();
      if (idsEqual(p.epicId, state.focusPlayerId)) row.classList.add("highlight");
      const anon = anonymousLabel(p);
      row.innerHTML = `
        <div>
          <div class="pname-row">
            <span class="pname">${escapeHtml(p.name)}</span>
            ${trackerButton(p.name)}
            ${nameFnButton(p.epicId)}
          </div>
          <div class="badges">
            ${platformMarkup(p.platform)}
            ${p.isYou ? `<span class="badge you">you</span>` : ""}
            ${p.isBot ? `<span class="badge">bot</span>` : ""}
            ${anon ? `<span class="badge">${escapeHtml(anon)}</span>` : ""}
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

function prettyPlaylist(id) {
  if (!id) return "Unknown playlist";
  return id.replace(/^Playlist_/, "").replaceAll("_", " ");
}

function isEnvironmentName(label) {
  return label === "Storm" || label === "Fall" || label === "Environment" || label === "Bleed out";
}

function keepWithHideBots(match, e, you) {
  const actorBot = findPlayer(match, e.actorId)?.player?.isBot === true;
  const targetBot = findPlayer(match, e.targetId)?.player?.isBot === true;
  if (idsEqual(e.actorId, you)) return !targetBot;
  if (idsEqual(e.targetId, you)) return !actorBot;
  if (!e.actorId && isEnvironmentName(e.actorName)) return !targetBot;
  return !actorBot && !targetBot;
}

function nameButton(label, team, id, role) {
  const text = label || "—";
  if (text === "—") return `<span class="name-missing">${escapeHtml(text)}</span>`;
  if (!id && isEnvironmentName(text)) {
    return `<span class="name-env ${role}">${escapeHtml(text)}</span>`;
  }
  const teamAttr = team == null ? "" : String(team);
  return `<button type="button" class="name-link ${role}" data-team="${escapeHtml(teamAttr)}" data-id="${escapeHtml(id || "")}">${escapeHtml(text)}</button>`;
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
    li.querySelectorAll(".name-link, .their-team").forEach((btn) => {
      btn.addEventListener("click", () => {
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

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

$("scrape").addEventListener("click", () => scrape(""));
$("export").addEventListener("click", () => {
  window.location.href = "/api/match.json";
});
$("search").addEventListener("input", render);
$("show-bots").addEventListener("change", render);
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
["hide-bots", "only-you", "flt-kills", "flt-knocks", "flt-deaths", "team-kills", "team-knocks", "team-deaths"].forEach(
  (id) => $(id).addEventListener("change", renderTimeline)
);
document.querySelectorAll(".tabs button").forEach((btn) => {
  btn.addEventListener("click", () => {
    state.tab = btn.dataset.tab;
    document.querySelectorAll(".tabs button").forEach((b) => {
      b.classList.toggle("active", b === btn);
    });
    render();
  });
});

fetch("/api/config")
  .then((res) => (res.ok ? res.json() : null))
  .then((cfg) => {
    if (cfg && cfg.nameFnPublicUrl) state.nameFnPublicUrl = String(cfg.nameFnPublicUrl).replace(/\/$/, "");
  })
  .catch(() => {});
loadDemos().catch((err) => showStatus(String(err)));
fetch("/api/match")
  .then((res) => (res.ok ? res.json() : null))
  .then((match) => {
    if (!match) return;
    state.match = match;
    $("export").disabled = false;
    render();
  })
  .catch(() => {});
