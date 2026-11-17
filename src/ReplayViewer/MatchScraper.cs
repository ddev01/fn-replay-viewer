using System.Runtime;
using ReplayViewer.Core;

namespace ReplayViewer;

internal sealed class MatchScraper : IMatchScraper
{
    // Keep backfill in step with the sidebar: older replays are not reachable in the UI.
    private const int BackfillTake = 20;

    private readonly FortniteReplayProbe _probe = new();
    private readonly FortniteReplayProbe _backgroundProbe = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
    };
    private readonly MatchCacheIndex _index = MatchCacheIndex.Default;
    private readonly object _gate = new();
    private Task? _backfill;

    public bool BackfillRunning
    {
        get
        {
            lock (_gate)
            {
                return _backfill is { IsCompleted: false };
            }
        }
    }

    public IReadOnlyList<DemoListItem> ListRecent(int take = 20) =>
        DemoCatalog
            .ListRecent(DemosFolder.DefaultPath, take)
            .Select(WithIndex)
            .ToArray();

    public async Task<ScrapeResult> ScrapeAsync(
        string? replayPath,
        CancellationToken cancellationToken
    )
    {
        var path = Resolve(replayPath);
        var replay = Describe(path);

        if (ReplayHeader.TryInspect(path, out var header) && header.IsLive)
        {
            throw new LiveReplayException();
        }

        // A cached match skips the copy and the parse, which is the whole point: 0.5-3s saved.
        var cacheKey = MatchCache.KeyFor(path);
        if (
            cacheKey is not null
            && MatchCache.TryRead(MatchCache.DefaultRoot, cacheKey, out var cached)
            && cached is not null
        )
        {
            _index.Save(replay, cacheKey, cached);
            // Opening a cached match is still a user action: catch up any games played since
            // the last backfill.
            StartBackfill();
            return new ScrapeResult(cached, Cached: true);
        }

        var match = await ParseAsync(_probe, path, cancellationToken).ConfigureAwait(false);
        SaveIfUnchanged(replay, cacheKey, match);
        CompactAfterParse();
        StartBackfill();
        return new ScrapeResult(match, Cached: false);
    }

    public void StartBackfill()
    {
        lock (_gate)
        {
            if (_backfill is { IsCompleted: false })
            {
                return;
            }

            _backfill = Task.Run(BackfillAsync);
        }
    }

    private async Task BackfillAsync()
    {
        var replays = DemoCatalog
            .ListRecent(DemosFolder.DefaultPath, BackfillTake)
            .Where(replay => !IsFresh(replay))
            .ToArray();
        if (replays.Length == 0)
        {
            return;
        }

        var parsed = 0;
        foreach (var replay in replays)
        {
            var cacheKey = MatchCache.KeyFor(replay.Path);
            try
            {
                if (replay.Length < 1024)
                {
                    continue;
                }

                if (ReplayHeader.TryInspect(replay.Path, out var header) && header.IsLive)
                {
                    continue;
                }

                var match = await ParseAsync(
                        _backgroundProbe,
                        replay.Path,
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
                if (SaveIfUnchanged(replay, cacheKey, match))
                {
                    parsed++;
                }
            }
            catch (Exception ex)
            {
                FileLog.Info($"backfill skipped {replay.Name}: {ex.Message}");
                _index.SaveFailure(replay, cacheKey);
            }
        }

        if (parsed > 0)
        {
            FileLog.Info($"backfill: parsed {parsed}");
        }
    }

    private static async Task<MatchDocument> ParseAsync(
        FortniteReplayProbe probe,
        string path,
        CancellationToken cancellationToken
    )
    {
        var copy = ReplayFileAccess.CopyWithReadWriteShare(path);
        try
        {
            var snapshot = await probe.ReadAsync(copy, cancellationToken).ConfigureAwait(false);
            snapshot = snapshot with { SourceFileName = Path.GetFileName(path) };
            return MatchAssembler.Build(snapshot, DateTimeOffset.UtcNow);
        }
        finally
        {
            try
            {
                File.Delete(copy);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A backup agent or antivirus can hold the temp file; a leftover copy must not
                // turn a successful scrape into a failure.
            }
        }
    }

    private static string Resolve(string? replayPath)
    {
        string path;
        if (string.IsNullOrWhiteSpace(replayPath))
        {
            var pick = LatestStableReplay.Pick(DemosFolder.DefaultPath);
            if (pick.StillWriting)
            {
                throw new ReplayStillWritingException();
            }

            if (string.IsNullOrWhiteSpace(pick.Path))
            {
                throw new FileNotFoundException("No completed replay found in the Demos folder.");
            }

            path = pick.Path;
        }
        else
        {
            path = ReplayPaths.ResolveInside(DemosFolder.DefaultPath, replayPath);
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Replay file was not found.", path);
        }

        return path;
    }

    private static DemoListItem Describe(string path)
    {
        var info = new FileInfo(path);
        info.Refresh();
        return new DemoListItem(
            info.Name,
            info.FullName,
            info.Length,
            new DateTimeOffset(DateTime.SpecifyKind(info.LastWriteTimeUtc, DateTimeKind.Utc))
        );
    }

    private bool SaveIfUnchanged(DemoListItem before, string? cacheKey, MatchDocument match)
    {
        if (cacheKey is null || !Unchanged(before))
        {
            return false;
        }

        MatchCache.Write(MatchCache.DefaultRoot, cacheKey, match);
        _index.Save(before, cacheKey, match);
        return true;
    }

    private static bool Unchanged(DemoListItem before)
    {
        var after = Describe(before.Path);
        return after.Length == before.Length && after.LastWriteUtc == before.LastWriteUtc;
    }

    private static void CompactAfterParse()
    {
        // Parsing a 17-21 MB replay allocates hundreds of MB of transient buffers, most of it on
        // the large object heap, and the runtime keeps that committed afterwards. This app then
        // idles in the tray while the game needs the RAM, so hand it back now.
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }

    private DemoListItem WithIndex(DemoListItem replay)
    {
        var key = MatchCache.KeyFor(replay.Path);
        var entry = _index.Get(replay, key);
        if (
            entry is null
            || entry.Failed
            || key is null
            || !MatchCache.Exists(MatchCache.DefaultRoot, key)
        )
        {
            return replay;
        }

        return replay with
        {
            Cached = true,
            MapName = entry.MapName,
            Playlist = entry.Playlist,
            RecordedAt = entry.RecordedAt,
            DurationSeconds = entry.DurationSeconds,
            Placement = entry.Placement,
            Elims = entry.Elims,
        };
    }

    private bool IsFresh(DemoListItem replay)
    {
        var key = MatchCache.KeyFor(replay.Path);
        var entry = _index.Get(replay, key);
        return entry is not null
            && (
                entry.Failed
                || (key is not null && MatchCache.Exists(MatchCache.DefaultRoot, key))
            );
    }
}
