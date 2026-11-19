using System.Runtime;
using ReplayViewer.Core;

namespace ReplayViewer;

internal sealed class MatchScraper : IMatchScraper
{
    // MatchCache keeps 200 entries, so parsing that many fills the sidebar once and then never
    // again: a fresh replay is skipped, and only new replays are parsed.
    private const int BackfillTake = 200;

    // Long enough for the game to finish writing and unlock the file after a match.
    private static readonly TimeSpan RecentWrite = TimeSpan.FromMinutes(10);

    // A replay that stops moving through its data chunks means a stuck parser: the child notices
    // that in about two seconds. This is only the backstop for a child that wedges some other way.
    private static readonly TimeSpan ParseTimeout = TimeSpan.FromSeconds(60);

    private readonly MatchCacheIndex _index = MatchCacheIndex.Default;
    private readonly OwnerIdentities _owners = OwnerIdentities.Default;
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

    public IReadOnlyList<DemoListItem> ListDemos()
    {
        var replays = DemoCatalog
            .ListAll(DemosFolder.DefaultPath)
            .Select(WithIndex)
            .ToList();
        var onDisk = replays
            .Select(replay => replay.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A replay Fortnite deleted is still openable while its match is cached.
        // shortcut: the key cannot be re-derived without the file, so this can serve a match
        // cached by an older version; drop entries when the cache format changes.
        foreach (var entry in _index.All())
        {
            if (
                entry.Failed
                || entry.CacheKey is null
                || onDisk.Contains(entry.Name)
                || !MatchCache.Exists(MatchCache.DefaultRoot, entry.CacheKey)
            )
            {
                continue;
            }

            replays.Add(
                new DemoListItem(
                    entry.Name,
                    Path.Combine(DemosFolder.DefaultPath, entry.Name),
                    entry.Length,
                    new DateTimeOffset(entry.LastWriteTicks, TimeSpan.Zero),
                    entry.MapName,
                    entry.Playlist,
                    entry.RecordedAt,
                    entry.DurationSeconds,
                    entry.Placement,
                    entry.Elims,
                    Cached: true,
                    TotalPlayers: entry.TotalPlayers
                )
            );
        }

        return replays.OrderByDescending(replay => replay.LastWriteUtc).ToArray();
    }

    public async Task<ScrapeResult> ScrapeAsync(
        string? replayPath,
        CancellationToken cancellationToken
    )
    {
        if (!string.IsNullOrWhiteSpace(replayPath) && TryCachedOnly(replayPath, out var cachedOnly))
        {
            var resolved = WithOwner(cachedOnly!);
            StartBackfill();
            return new ScrapeResult(resolved, Cached: true);
        }

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
            cached = WithOwner(cached);
            _index.Save(replay, cacheKey, cached);
            // Opening a cached match is still a user action: catch up any games played since
            // the last backfill.
            StartBackfill();
            return new ScrapeResult(cached, Cached: true);
        }

        // A replay the parser stalls on cannot be read by any mode, so refuse it instead of
        // making every click wait for the timeout again.
        if (_index.Get(replay, cacheKey) is { Failed: true })
        {
            throw new UnreadableReplayException(
                "The replay parser stops part-way through this file, so it cannot be opened."
            );
        }

        MatchDocument parsed;
        try
        {
            parsed = await ReplayParseProcess
                .ParseAsync(path, ParseTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _index.SaveFailure(replay, cacheKey);
            throw;
        }

        var match = WithOwner(parsed);
        SaveIfUnchanged(replay, cacheKey, match);
        CompactAfterParse();
        StartBackfill();
        return new ScrapeResult(match, Cached: false);
    }

    /// <summary>
    /// Names the owner when the file does not, using the ids learned from replays that did.
    /// </summary>
    private MatchDocument WithOwner(MatchDocument match)
    {
        var resolved = OwnerResolver.Apply(match, _owners.Ids);
        _owners.Learn(resolved);
        return resolved;
    }

    public void RememberOwner(string epicId)
    {
        _owners.Add(epicId);
        // The id arrives after the sidebar was built, so every cached match that could not name
        // its owner is re-checked now rather than waiting for a restart.
        RefreshOwners();
    }

    private void RefreshOwners()
    {
        foreach (var replay in DemoCatalog.ListAll(DemosFolder.DefaultPath).Take(BackfillTake))
        {
            var cacheKey = MatchCache.KeyFor(replay.Path);
            if (
                cacheKey is null
                || !MatchCache.TryRead(MatchCache.DefaultRoot, cacheKey, out var cached)
                || cached is null
                || cached.Teams.Any(team => team.Players.Any(player => player.IsYou))
            )
            {
                continue;
            }

            var applied = WithOwner(cached);
            if (applied.Teams.Any(team => team.Players.Any(player => player.IsYou)))
            {
                _index.Save(replay, cacheKey, applied);
            }
        }
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
        var replays = DemoCatalog.ListAll(DemosFolder.DefaultPath).Take(BackfillTake).ToArray();
        if (replays.Length == 0)
        {
            return;
        }

        // Learn who you are from every match already cached, before parsing anything: a replay
        // whose file never names the owner is resolved from these ids, and the parse order
        // cannot be relied on to have seen a labelled match first.
        LearnFromCache(replays);

        var parsed = 0;
        foreach (var replay in replays)
        {
            var cacheKey = MatchCache.KeyFor(replay.Path);
            try
            {
                if (IsFresh(replay) && RefreshSummary(replay, cacheKey))
                {
                    continue;
                }

                // A replay the game is still recording is locked: reading its header can block
                // on the locked range and never return. Leave recent writes for a later pass.
                if (DateTimeOffset.UtcNow - replay.LastWriteUtc < RecentWrite)
                {
                    continue;
                }

                if (replay.Length < 1024)
                {
                    continue;
                }

                if (ReplayHeader.TryInspect(replay.Path, out var header) && header.IsLive)
                {
                    continue;
                }

                var match = WithOwner(
                    await ReplayParseProcess
                        .ParseAsync(replay.Path, ParseTimeout, CancellationToken.None)
                        .ConfigureAwait(false)
                );
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

    /// <summary>
    /// Rebuilds a summary written by an older build from the cached match: the score row reads
    /// the roster, and the owner may have been learned since. Returns false when only the replay
    /// file can supply what is missing.
    /// </summary>
    private bool RefreshSummary(DemoListItem replay, string? cacheKey)
    {
        var entry = _index.Get(replay, cacheKey);
        if (entry is null || entry.Schema >= MatchCacheIndex.CurrentSchema || cacheKey is null)
        {
            return true;
        }

        if (!MatchCache.TryRead(MatchCache.DefaultRoot, cacheKey, out var cached) || cached is null)
        {
            return false;
        }

        var applied = WithOwner(cached);

        // A Creative match has no playlist; only the replay header says a match is Creative.
        if (applied.Playlist is null && applied.MapName is null)
        {
            return false;
        }

        // No placement in a normal match means the owner is still unknown: the cached summary
        // does not keep the per-player placement that re-parsing would use.
        if (
            applied.Playlist is not null
            && applied.YourPlacement is null
            && applied.TeamPlacement is null
        )
        {
            return false;
        }

        _index.Save(replay, cacheKey, applied);
        return true;
    }

    /// <summary>Learns the owner from every match already cached, before any file is parsed.</summary>
    private void LearnFromCache(IReadOnlyList<DemoListItem> replays)
    {
        foreach (var replay in replays)
        {
            var cacheKey = MatchCache.KeyFor(replay.Path);
            if (
                cacheKey is not null
                && MatchCache.TryRead(MatchCache.DefaultRoot, cacheKey, out var cached)
                && cached is not null
            )
            {
                _owners.Learn(cached);
            }
        }
    }

    /// <summary>
    /// Reads the cache for a replay whose .replay file is already gone. Returns false when the
    /// file is still there (the normal path handles it) or nothing is cached for it.
    /// </summary>
    private bool TryCachedOnly(string replayPath, out MatchDocument? match)
    {
        match = null;
        var path = ReplayPaths.ResolveInside(DemosFolder.DefaultPath, replayPath);
        if (File.Exists(path))
        {
            return false;
        }

        var key = _index.CacheKeyFor(Path.GetFileName(path));
        return key is not null
            && MatchCache.TryRead(MatchCache.DefaultRoot, key, out match)
            && match is not null;
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
        // A file the parser stalls on has no map or scores; saying so beats a bare file name.
        if (entry is { Failed: true })
        {
            return replay with { Unreadable = true };
        }

        if (entry is null || key is null || !MatchCache.Exists(MatchCache.DefaultRoot, key))
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
            TotalPlayers = entry.TotalPlayers,
        };
    }

    private bool IsFresh(DemoListItem replay)
    {
        var key = MatchCache.KeyFor(replay.Path);
        var entry = _index.Get(replay, key);
        return entry is not null
            && (
                entry.Failed
                || (
                    key is not null
                    && entry.Schema >= MatchCacheIndex.CurrentSchema
                    && MatchCache.Exists(MatchCache.DefaultRoot, key)
                )
            );
    }
}
