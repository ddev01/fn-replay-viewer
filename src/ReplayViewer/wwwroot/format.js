// @ts-check
// Pure string helpers: no DOM, no page state.

/**
 * @param {unknown} value
 * @returns {string}
 */
export function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

/**
 * @param {string | null | undefined} a
 * @param {string | null | undefined} b
 * @returns {boolean}
 */
export function idsEqual(a, b) {
  return Boolean(a && b && String(a).toLowerCase() === String(b).toLowerCase());
}

/**
 * @param {number | null | undefined} seconds
 * @returns {string}
 */
export function formatTime(seconds) {
  const s = Math.max(0, Math.floor(seconds || 0));
  const m = Math.floor(s / 60);
  const r = String(s % 60).padStart(2, "0");
  return `${m}:${r}`;
}

/**
 * @param {number | null | undefined} value
 * @returns {string | null}
 */
export function formatAccuracy(value) {
  if (value == null || Number.isNaN(Number(value))) return null;
  const n = Number(value);
  const pct = n <= 1 ? n * 100 : n;
  return `${pct.toFixed(0)}%`;
}

/**
 * @param {string | undefined} id
 * @returns {string}
 */
export function prettyPlaylist(id) {
  if (!id) return "Unknown playlist";
  return id.replace(/^Playlist_/, "").replaceAll("_", " ");
}

/**
 * @param {string | null | undefined} id
 * @returns {string | null}
 */
export function epicUuid(id) {
  const h = String(id).replaceAll("-", "").toLowerCase();
  if (!/^[0-9a-f]{32}$/.test(h)) return null;
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`;
}

/**
 * @param {string} name
 * @returns {string}
 */
export function trackerUrl(name) {
  return "https://fortnitetracker.com/profile/search?q=" + encodeURIComponent(name).replaceAll("%20", "+");
}

/**
 * @param {string} value
 * @returns {string}
 */
export function cssEscape(value) {
  if (globalThis.CSS && CSS.escape) return CSS.escape(value);
  return String(value).replace(/"/g, '\\"');
}
