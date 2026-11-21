namespace ReplayViewer.Tests;

public sealed class MatchScraperTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "ReplayViewerTests",
        Guid.NewGuid().ToString("N")
    );

    private readonly string _demos;
    private readonly string _cache;
    private readonly MatchCacheIndex _index;
    private int _parses;
    private Func<Task<MatchDocument>> _parse;

    public MatchScraperTests()
    {
        _demos = Path.Combine(_root, "demos");
        _cache = Path.Combine(_root, "cache");
        Directory.CreateDirectory(_demos);
        _index = new MatchCacheIndex(Path.Combine(_root, "index"));
        _parse = () => Task.FromResult(Match());
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task A_parsed_match_is_cached_and_the_second_open_skips_the_parse()
    {
        var path = WriteReplay("a.replay");
        var scraper = NewScraper();

        var first = await scraper.ScrapeAsync(path, TestContext.Current.CancellationToken);
        var second = await scraper.ScrapeAsync(path, TestContext.Current.CancellationToken);

        Assert.False(first.Cached);
        Assert.True(second.Cached);
        Assert.Equal(1, _parses);
    }

    [Fact]
    public async Task Deleting_a_replay_removes_the_file_its_cache_and_its_listing()
    {
        var path = WriteReplay("gone.replay");
        var other = WriteReplay("kept.replay");
        var scraper = NewScraper();
        await scraper.ScrapeAsync(path, TestContext.Current.CancellationToken);

        var key = _index.CacheKeyFor("gone.replay")!;
        Assert.True(MatchCache.Exists(_cache, key));

        var result = scraper.DeleteDemos(["gone.replay", "../escape.replay"]);

        Assert.Equal(["gone.replay"], result.Deleted);
        Assert.Equal(["../escape.replay"], result.Failed);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(other));
        Assert.False(MatchCache.Exists(_cache, key));
        Assert.DoesNotContain(scraper.ListDemos(), d => d.Name == "gone.replay");
    }

    [Fact]
    public async Task A_replay_the_parser_stalls_on_is_refused_without_parsing_again()
    {
        var path = WriteReplay("stall.replay");
        _parse = () => throw new UnreadableReplayException("stalls") { IsPermanent = true };
        var scraper = NewScraper();

        await Assert.ThrowsAsync<UnreadableReplayException>(() =>
            scraper.ScrapeAsync(path, TestContext.Current.CancellationToken)
        );
        await Assert.ThrowsAsync<UnreadableReplayException>(() =>
            scraper.ScrapeAsync(path, TestContext.Current.CancellationToken)
        );

        Assert.Equal(1, _parses);
    }

    [Fact]
    public async Task A_transient_failure_is_not_remembered_so_the_next_open_parses_again()
    {
        var path = WriteReplay("locked.replay");
        _parse = () => throw new UnreadableReplayException("file in use");
        var scraper = NewScraper();

        await Assert.ThrowsAsync<UnreadableReplayException>(() =>
            scraper.ScrapeAsync(path, TestContext.Current.CancellationToken)
        );
        _parse = () => Task.FromResult(Match());
        var retry = await scraper.ScrapeAsync(path, TestContext.Current.CancellationToken);

        Assert.False(retry.Cached);
        Assert.Equal(2, _parses);
        Assert.DoesNotContain(scraper.ListDemos(), d => d.Unreadable);
    }

    [Fact]
    public async Task Backfill_does_not_brand_a_replay_broken_after_a_transient_failure()
    {
        WriteReplay("busy.replay", old: true);
        _parse = () => throw new IOException("sharing violation");
        var scraper = NewScraper();

        await RunBackfillAsync(scraper);
        await RunBackfillAsync(scraper);

        Assert.DoesNotContain(scraper.ListDemos(), d => d.Unreadable);
        // Retried later, not on every pass: each retry costs a process start.
        Assert.Equal(1, _parses);
    }

    [Fact]
    public async Task Backfill_marks_a_replay_broken_after_a_timeout()
    {
        WriteReplay("slow.replay", old: true);
        _parse = () => throw new TimeoutException("slow");
        var scraper = NewScraper();

        await RunBackfillAsync(scraper);

        Assert.Contains(scraper.ListDemos(), d => d.Unreadable);
    }

    [Fact]
    public async Task A_bad_path_is_an_invalid_path_not_a_server_error()
    {
        var scraper = NewScraper();

        await Assert.ThrowsAsync<InvalidReplayPathException>(() =>
            scraper.ScrapeAsync(Path.Combine(_root, "outside.replay"), TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public void The_index_survives_a_write_failure()
    {
        // The directory is a file, so the index cannot be written.
        var blocker = Path.Combine(_root, "blocker");
        File.WriteAllText(blocker, "x");
        var index = new MatchCacheIndex(blocker);
        var replay = new DemoListItem("a.replay", "a.replay", 10, DateTimeOffset.UtcNow);

        index.Save(replay, "key", Match());

        Assert.NotNull(index.Get(replay, "key"));
    }

    private MatchScraper NewScraper() =>
        new(
            _demos,
            _cache,
            _index,
            (_, _, _) =>
            {
                _parses++;
                return _parse();
            }
        );

    private string WriteReplay(string name, bool old = false)
    {
        var path = Path.Combine(_demos, name);
        File.WriteAllBytes(path, new byte[2048]);
        // Backfill leaves anything written in the last ten minutes alone.
        File.SetLastWriteTimeUtc(path, old ? DateTime.UtcNow.AddHours(-2) : DateTime.UtcNow.AddMinutes(-30));
        return path;
    }

    private static async Task RunBackfillAsync(MatchScraper scraper)
    {
        scraper.StartBackfill();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (scraper.BackfillRunning && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.False(scraper.BackfillRunning);
    }

    private static MatchDocument Match() =>
        new("x.replay", DateTimeOffset.UtcNow, null, null, null, null, null, null, null, [], []);
}
