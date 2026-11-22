// @ts-check
// Pure logic of the owner-only streamer linking: no DOM, no fetch, no page state. The shapes are
// the LinkingXxx interfaces in types/match.d.ts (the contract's section B, relayed by LinkingRoutes).

/** A F9 lookup is sent while the match runs; allow for clock skew and the lobby either side. */
const WINDOW_SLACK_SECONDS = 10 * 60;

const PLATFORMS = /** @type {Record<string, string>} */ ({ twitch: "Twitch", tiktok: "TikTok" });
const SOURCES = /** @type {Record<string, string>} */ ({
  clipper_manual: "manual",
  tracker_claimed: "tracker",
});
const VARIANTS = /** @type {Record<string, string>} */ ({
  tag_kept: "tag kept",
  stripped: "stripped",
  handle: "handle",
  nickname: "nickname",
  fuzzy: "fuzzy",
});
const STATUSES = /** @type {Record<string, string>} */ ({
  live: "live",
  offline: "offline",
  not_found: "not found",
  error: "error",
});

/**
 * The server returns lowercase ids without hyphens; the replay may not.
 * @param {string | null | undefined} id
 * @returns {string}
 */
export function normId(id) {
  return String(id ?? "")
    .replaceAll("-", "")
    .toLowerCase();
}

/** @param {string} platform */
export const platformName = (platform) => PLATFORMS[platform] ?? platform;

/** @param {string} source */
export const sourceName = (source) => SOURCES[source] ?? source;

const HANDLE = /^[A-Za-z0-9_.]{1,40}$/;

/**
 * @param {string} platform
 * @param {string} handle
 * @returns {string | null} the channel/profile page, or null when the handle is not safe to link
 */
export function profileUrl(platform, handle) {
  if (!HANDLE.test(handle)) return null;
  if (platform === "twitch") return `https://www.twitch.tv/${handle}`;
  if (platform === "tiktok") return `https://www.tiktok.com/@${handle}`;
  return null;
}

/**
 * @param {LinkingLink} link
 * @returns {string} how the link came about, e.g. "Twitch: nova (manual)"
 */
export function linkTooltip(link) {
  const how =
    link.source === "clipper_manual"
      ? "manual"
      : link.source === "tracker_claimed"
        ? "claimed on Tracker"
        : "automatic";
  return `${platformName(link.platform)}: ${link.handle} (${how})`;
}

/**
 * The sidebar only has the lookup list, so it guesses by time: the F9 roster is posted while the
 * match runs. Opening the replay replaces the guess with the server's own match.
 * @param {DemoListItem} demo
 * @param {LinkingLookup[]} lookups
 * @returns {LinkingFlag | null} the flag of the latest lookup inside the replay's window
 */
export function flagForDemo(demo, lookups) {
  if (!demo.recordedAt) return null;
  const start = Date.parse(demo.recordedAt) / 1000;
  if (Number.isNaN(start)) return null;
  const end = start + (demo.durationSeconds ?? 0);
  /** @type {LinkingLookup | null} */
  let best = null;
  for (const lookup of lookups) {
    const at = lookup.received_at;
    if (at < start - WINDOW_SLACK_SECONDS || at > end + WINDOW_SLACK_SECONDS) continue;
    if (!best || at > best.received_at) best = lookup;
  }
  return best ? best.flag : null;
}

/**
 * @param {string | null | undefined} flag
 * @returns {string} tooltip for a flagged replay, "" for one that needs no mark
 */
export function flagText(flag) {
  if (flag === "none_found") return "F9 found no streamer in this match";
  if (flag === "weak_only") return "F9 only found weak streamer guesses in this match";
  return "";
}

/**
 * @param {MatchDocument} match
 * @returns {{ game_session_id: string | null, epic_ids: string[], played_at: number | null }}
 */
export function replayRequest(match) {
  const ids = new Set();
  for (const team of match.teams) {
    for (const player of team.players) {
      if (player.epicId && !player.isBot) ids.add(player.epicId);
    }
  }
  const played = match.recordedAt ? Math.floor(Date.parse(match.recordedAt) / 1000) : NaN;
  return {
    game_session_id: match.gameSessionId ?? null,
    epic_ids: [...ids],
    played_at: Number.isNaN(played) ? null : played,
  };
}

/**
 * @param {LinkingPlayer[] | undefined} players
 * @returns {Map<string, LinkingPlayer>}
 */
export function indexPlayers(players) {
  const map = new Map();
  for (const player of players ?? []) {
    map.set(normId(player.epic_id), {
      ...player,
      links: player.links ?? [],
      attempts: player.attempts ?? [],
    });
  }
  return map;
}

/**
 * Search pages in a new tab: a plain link, so the app itself makes no request.
 * @param {"twitch" | "tiktok"} platform
 * @param {string} name
 * @returns {string}
 */
export function searchUrl(platform, name) {
  const q = encodeURIComponent(name);
  return platform === "twitch"
    ? `https://www.twitch.tv/search?term=${q}`
    : `https://www.tiktok.com/search/user?q=${q}`;
}

/**
 * @param {LinkingAttempt} attempt
 * @returns {string}
 */
export function attemptText(attempt) {
  const bits = [
    `${platformName(attempt.platform)} ${attempt.query}`,
    VARIANTS[attempt.variant] ?? attempt.variant,
    attempt.match,
    STATUSES[attempt.status] ?? attempt.status,
  ];
  if (attempt.weak) bits.push("weak");
  return bits.join(" · ");
}

/**
 * Adds or replaces the player's link for that platform; there is one link per platform.
 * @param {Map<string, LinkingPlayer>} players
 * @param {LinkingLink & { epic_id?: string }} link
 * @param {string} epicId
 */
export function upsertLink(players, link, epicId) {
  const key = normId(epicId);
  const entry = players.get(key) ?? { epic_id: key, name: null, links: [], attempts: [] };
  entry.links = [...entry.links.filter((l) => l.platform !== link.platform), link];
  players.set(key, entry);
}

/**
 * @param {Map<string, LinkingPlayer>} players
 * @param {string} epicId
 * @param {string} platform
 */
export function removeLink(players, epicId, platform) {
  const entry = players.get(normId(epicId));
  if (entry) entry.links = entry.links.filter((l) => l.platform !== platform);
}
