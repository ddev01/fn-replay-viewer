using ReplayViewer.Core;

namespace ReplayViewer.Tests;

public sealed class MatchCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "ReplayViewerTests",
        Guid.NewGuid().ToString("N")
    );

    public MatchCacheTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static MatchDocument Sample(string source = "demo.replay") =>
        new(
            source,
            DateTimeOffset.UnixEpoch,
            "Playlist_ShowdownAlt_Solo",
            1,
            DateTimeOffset.UnixEpoch,
            1,
            1,
            null,
            new OwnerStatsSnapshot(5, 812, 0.34, 1, 1, 455, 120, 60, 30),
            [
                new TeamRoster(
                    0,
                    1,
                    [
                        new RosterPlayer(
                            "abc",
                            "Nova",
                            "PC",
                            5,
                            true,
                            false,
                            false,
                            false,
                            FillStatus.None
                        ),
                    ]
                ),
            ],
            [new MatchEvent(12.5, EventKind.Finish, "abc", "Nova", "def", "Bolt", "Rifle", 30, null, 0, 1)]
        );

    [Fact]
    public void A_written_match_reads_back()
    {
        MatchCache.Write(_root, "key1", Sample());

        Assert.True(MatchCache.TryRead(_root, "key1", out var match));
        Assert.NotNull(match);
        Assert.Equal("demo.replay", match.SourceFile);
        Assert.Single(match.Teams);
        Assert.Equal("Nova", match.Teams[0].Players[0].Name);
        Assert.Single(match.Events);
        Assert.Equal(EventKind.Finish, match.Events[0].Kind);
        Assert.Equal(5, match.OwnerStats?.Eliminations);
    }

    [Fact]
    public void An_empty_cache_is_a_miss()
    {
        Assert.False(MatchCache.TryRead(_root, "missing", out var match));
        Assert.Null(match);
    }

    [Fact]
    public void A_corrupt_entry_is_a_miss_rather_than_a_throw()
    {
        File.WriteAllBytes(Path.Combine(_root, "broken.json.br"), [1, 2, 3, 4, 5]);

        Assert.False(MatchCache.TryRead(_root, "broken", out var match));
        Assert.Null(match);
    }

    [Fact]
    public void The_key_changes_when_the_replay_file_changes()
    {
        var replay = Path.Combine(_root, "demo.replay");
        File.WriteAllText(replay, "one");
        var first = MatchCache.KeyFor(replay);

        File.WriteAllText(replay, "one and more");
        var second = MatchCache.KeyFor(replay);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void The_key_is_stable_for_an_unchanged_file()
    {
        var replay = Path.Combine(_root, "demo.replay");
        File.WriteAllText(replay, "one");

        Assert.Equal(MatchCache.KeyFor(replay), MatchCache.KeyFor(replay));
    }

    [Fact]
    public void A_missing_replay_has_no_key()
    {
        Assert.Null(MatchCache.KeyFor(Path.Combine(_root, "gone.replay")));
    }

    [Fact]
    public void Prune_keeps_the_newest_entries()
    {
        var now = DateTime.UtcNow;
        for (var i = 0; i < 5; i++)
        {
            var path = Path.Combine(_root, $"k{i}.json.br");
            File.WriteAllText(path, "x");
            File.SetLastWriteTimeUtc(path, now.AddMinutes(-i));
        }

        MatchCache.Prune(_root, keep: 2);

        var remaining = new DirectoryInfo(_root)
            .GetFiles("*.json.br")
            .Select(file => file.Name)
            .OrderBy(name => name)
            .ToArray();
        Assert.Equal(2, remaining.Length);
        Assert.Contains("k0.json.br", remaining);
        Assert.Contains("k1.json.br", remaining);
    }
}
