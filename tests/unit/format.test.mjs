import test from "node:test";
import assert from "node:assert/strict";
import {
  epicUuid,
  escapeHtml,
  formatAccuracy,
  formatTime,
  idsEqual,
  prettyPlaylist,
  trackerUrl,
} from "../../src/ReplayViewer/wwwroot/format.js";

test("escapeHtml neutralises markup and double quotes", () => {
  assert.equal(escapeHtml('<img src="x">&'), "&lt;img src=&quot;x&quot;&gt;&amp;");
});

test("idsEqual ignores case and treats missing ids as different", () => {
  assert.equal(idsEqual("ABC", "abc"), true);
  assert.equal(idsEqual("abc", null), false);
  assert.equal(idsEqual(null, null), false);
});

test("formatTime renders minutes and zero-padded seconds, never negative", () => {
  assert.equal(formatTime(75.9), "1:15");
  assert.equal(formatTime(5), "0:05");
  assert.equal(formatTime(-3), "0:00");
  assert.equal(formatTime(undefined), "0:00");
});

test("formatAccuracy accepts a fraction or a percentage", () => {
  assert.equal(formatAccuracy(0.34), "34%");
  assert.equal(formatAccuracy(34), "34%");
  assert.equal(formatAccuracy(null), null);
  assert.equal(formatAccuracy(Number.NaN), null);
});

test("epicUuid dashes a 32-digit id and rejects anything else", () => {
  assert.equal(epicUuid("8a1f2b3c4d5e6f708192a3b4c5d6e7f8"), "8a1f2b3c-4d5e-6f70-8192-a3b4c5d6e7f8");
  assert.equal(epicUuid("not an id"), null);
});

test("prettyPlaylist drops the prefix and underscores", () => {
  assert.equal(prettyPlaylist("Playlist_ShowdownAlt_Solo"), "ShowdownAlt Solo");
  assert.equal(prettyPlaylist(undefined), "Unknown playlist");
});

test("trackerUrl encodes the name and uses + for spaces", () => {
  assert.equal(trackerUrl("a b&c"), "https://fortnitetracker.com/profile/search?q=a+b%26c");
});
