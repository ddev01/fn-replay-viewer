// Canned responses for the /api/linking/* routes, written from the lobby-streamers contract
// (section B and its "Stubs" part). The C# side relays them unchanged.
import demos from "./demos.mjs";

const NOVA = "8a1f2b3c4d5e6f708192a3b4c5d6e7f8";
const BOLT = "1b2c3d4e5f60718293a4b5c6d7e8f901";
const unix = (iso) => Math.floor(Date.parse(iso) / 1000);

const counts = { candidates: 0, live: 0, recorded: 0, weak: 0 };
const lookup = (id, receivedAt, flag) => ({
  lookup_id: id,
  session_id: `gep-${id}`,
  game_session_id: null,
  received_at: unix(receivedAt),
  roster_size: 96,
  flag,
  counts,
});

// Two replays with a time of their own: the first is the open match, the second is older.
export const linkedDemos = [
  demos[0],
  {
    ...demos[1],
    recordedAt: "2026-10-30T19:02:11+00:00",
    durationSeconds: 800,
    elims: 1,
    placement: 30,
    cached: true,
    totalPlayers: 40,
  },
];

// GET /api/linking/lookups: one lookup of each flag. The `none_found` one sits in the older
// replay's window; the `weak_only` one in the open match's (the replay call confirms it).
export const lookups = {
  lookups: [
    lookup(1, "2026-10-30T19:10:00Z", "none_found"),
    lookup(2, "2026-10-31T20:20:00Z", "weak_only"),
    lookup(3, "2026-10-29T00:00:00Z", "ok"),
  ],
};

// POST /api/linking/replay: Nova has a manual Twitch link and no attempts; Bolt has two attempts.
export const replay = {
  lookup: lookups.lookups[1],
  match_basis: "session_id",
  players: [
    {
      epic_id: NOVA,
      name: "Nova",
      links: [{ platform: "twitch", handle: "nova", platform_user_id: null, source: "clipper_manual" }],
      attempts: [],
    },
    {
      epic_id: BOLT,
      name: "Bolt",
      links: [],
      attempts: [
        {
          platform: "twitch",
          query: "ttvbolt",
          variant: "tag_kept",
          match: "exact",
          status: "not_found",
          weak: false,
          reason: null,
        },
        {
          platform: "twitch",
          query: "bolt",
          variant: "stripped",
          match: "exact",
          status: "live",
          weak: true,
          reason: null,
        },
      ],
    },
  ],
};

// POST /api/linking/links: the parsed handle, stored.
export const savedLink = {
  link: {
    epic_id: BOLT,
    platform: "twitch",
    handle: "bolt",
    platform_user_id: null,
    source: "clipper_manual",
  },
};
