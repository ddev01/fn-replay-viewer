namespace ReplayViewer.Tests;

public sealed class MatchCacheIndexTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "ReplayViewerTests",
        Guid.NewGuid().ToString("N")
    );

    [Fact]
    public void Saved_match_metadata_round_trips()
    {
        var replay = Replay();
        new MatchCacheIndex(_directory).Save(replay, "key1", Match());

        var reopened = new MatchCacheIndex(_directory);
        var entry = reopened.Get(replay, "key1");

        Assert.NotNull(entry);
        Assert.Equal("Reload - Springfield", entry.MapName);
        Assert.Equal(500, entry.DurationSeconds);
        Assert.Equal(3, entry.Placement);
        Assert.Equal(6, entry.Elims);
        Assert.Equal(1, entry.TotalPlayers);
        Assert.False(entry.Failed);
    }

    [Fact]
    public void Cache_key_survives_the_replay_file()
    {
        var index = new MatchCacheIndex(_directory);
        index.Save(Replay(), "key1", Match());

        Assert.Equal("key1", new MatchCacheIndex(_directory).CacheKeyFor("A.REPLAY"));
    }

    [Fact]
    public void The_roster_kills_win_over_the_event_stat()
    {
        var index = new MatchCacheIndex(_directory);
        index.Save(Replay(), "key1", Match() with { OwnerStats = new OwnerStatsSnapshot(1, null, null, null, null, null) });

        // The page shows the roster count, so the sidebar must agree with it.
        Assert.Equal(6, new MatchCacheIndex(_directory).Get(Replay(), "key1")?.Elims);
    }

    [Fact]
    public void Saved_entries_carry_the_current_schema()
    {
        var index = new MatchCacheIndex(_directory);
        index.Save(Replay(), "key1", Match());

        Assert.Equal(
            MatchCacheIndex.CurrentSchema,
            new MatchCacheIndex(_directory).Get(Replay(), "key1")?.Schema
        );
    }

    [Fact]
    public void Changed_replay_file_is_stale()
    {
        var replay = Replay();
        var index = new MatchCacheIndex(_directory);
        index.Save(replay, "key1", Match());

        Assert.Null(index.Get(replay with { Length = replay.Length + 1 }, "key1"));
    }

    [Fact]
    public void Changed_cache_key_is_stale()
    {
        var replay = Replay();
        var index = new MatchCacheIndex(_directory);
        index.Save(replay, "key1", Match());

        Assert.Null(index.Get(replay, "key2"));
    }

    [Fact]
    public void Failed_parse_is_remembered()
    {
        var replay = Replay();
        var index = new MatchCacheIndex(_directory);
        index.SaveFailure(replay, "key1");

        Assert.True(index.Get(replay, "key1")?.Failed);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    private static DemoListItem Replay() =>
        new(
            "a.replay",
            Path.Combine(Path.GetTempPath(), "a.replay"),
            100,
            DateTimeOffset.UnixEpoch
        );

    private static MatchDocument Match() =>
        MatchAssembler.Build(
            new ParsedReplaySnapshot(
                "a.replay",
                "Playlist_Habanero_NoBuild_RopeSmile_Duos",
                2,
                DateTimeOffset.UnixEpoch,
                null,
                new OwnerStatsSnapshot(6, null, null, null, null, null),
                [
                    new PlayerSnapshot(
                        "a",
                        "A",
                        "WIN",
                        IsReplayOwner: true,
                        IsBot: false,
                        Disconnected: false,
                        Anonymous: false,
                        TeamIndex: 1,
                        Placement: 3,
                        PartyOwnerId: null,
                        Kills: 6
                    ),
                ],
                [new TeamSnapshot(1, 3)],
                [],
                500
            ),
            DateTimeOffset.UnixEpoch
        );
}
