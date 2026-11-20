// @ts-check
// HTML string builders for the small pieces repeated across the roster, timeline and demo list.
// Every dynamic value goes through escapeHtml.
import { epicUuid, escapeHtml, formatAccuracy, trackerUrl } from "./format.js";
import { isEnvironmentName } from "./model.js";
import { state } from "./state.js";

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
 * @param {string} file
 * @param {string} title
 * @param {string} [extraClass]
 * @returns {string}
 */
export function themedIcon(file, title, extraClass) {
  // The URL is set by the .icon-<name> rule in app.css. An inline style attribute would be
  // blocked by the page's Content-Security-Policy and the icon would render as a solid block.
  const name = file.replace(/\.svg$/, "");
  return `<span class="icon themed icon-${name} ${extraClass || ""}" title="${escapeHtml(title)}"></span>`;
}

/**
 * @param {string | null | undefined} platform
 * @returns {string}
 */
export function platformMarkup(platform) {
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
export function kindMarkup(kind, side) {
  const sideClass = side ? ` ${side}` : "";
  if (kind === "knock") return themedIcon("knock.svg", "Knock", `event knock${sideClass}`);
  if (kind === "finish" || kind === "selfElim") {
    return themedIcon("kill.svg", kind === "selfElim" ? "Self elim" : "Elim", `event kill${sideClass}`);
  }
  return `<span class="kind">${escapeHtml(kind)}</span>`;
}

/** @returns {string} */
export function arrowMarkup() {
  return themedIcon("arrow.svg", "", "arrow");
}

/**
 * @param {string | null | undefined} name
 * @returns {string}
 */
export function trackerButton(name) {
  if (!name) return "";
  return `<a class="tracker" aria-label="Fortnite Tracker" href="${escapeHtml(trackerUrl(name))}" target="_blank" rel="noopener noreferrer" title="Fortnite Tracker">${themedIcon("external.svg", "Fortnite Tracker")}</a>`;
}

/**
 * @param {string | null | undefined} epicId
 * @returns {string}
 */
export function nameFnButton(epicId) {
  if (!state.nameFnPublicUrl || !epicId) return "";
  const q = epicUuid(epicId);
  if (!q) return "";
  const url = state.nameFnPublicUrl + "/search?q=" + encodeURIComponent(q);
  return `<a class="tracker namefn" aria-label="NameFN" href="${escapeHtml(url)}" target="_blank" rel="noopener noreferrer" title="NameFN">${themedIcon("external.svg", "NameFN")}</a>`;
}

/**
 * @param {OwnerStats | null | undefined} stats
 * @returns {string}
 */
export function ownerStatChips(stats) {
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
 * @param {string | null | undefined} label
 * @param {number | null | undefined} team
 * @param {string | null | undefined} id
 * @param {string} role
 * @returns {string}
 */
export function nameButton(label, team, id, role) {
  const text = label || "—";
  if (text === "—") return `<span class="name-missing">${escapeHtml(text)}</span>`;
  if (!id && isEnvironmentName(text)) {
    return `<span class="name-env ${role}">${escapeHtml(text)}</span>`;
  }
  const teamAttr = team == null ? "" : String(team);
  return `<button type="button" class="name-link ${role}" data-team="${escapeHtml(teamAttr)}" data-id="${escapeHtml(id || "")}">${escapeHtml(text)}</button>`;
}
