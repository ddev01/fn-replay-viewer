using System.Runtime;
using ReplayViewer.Core;

namespace ReplayViewer;

internal sealed class MatchScraper : IMatchScraper
{
    private readonly FortniteReplayProbe _probe = new();

    public IReadOnlyList<DemoListItem> ListRecent(int take = 20) =>
        DemoCatalog.ListRecent(DemosFolder.DefaultPath, take);

    public async Task<MatchDocument> ScrapeAsync(
        string? replayPath,
        CancellationToken cancellationToken
    )
    {
        var path = replayPath;
        if (string.IsNullOrWhiteSpace(path))
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
            path = ReplayPaths.ResolveInside(DemosFolder.DefaultPath, path);
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Replay file was not found.", path);
        }

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
            return cached;
        }

        var copy = ReplayFileAccess.CopyWithReadWriteShare(path);
        try
        {
            var snapshot = await _probe.ReadAsync(copy, cancellationToken).ConfigureAwait(false);
            snapshot = snapshot with { SourceFileName = Path.GetFileName(path) };
            var match = MatchAssembler.Build(snapshot, DateTimeOffset.UtcNow);
            if (cacheKey is not null)
            {
                MatchCache.Write(MatchCache.DefaultRoot, cacheKey, match);
            }

            // Parsing a 17-21 MB replay allocates hundreds of MB of transient buffers, most of
            // it on the large object heap, and the runtime keeps that committed afterwards. This
            // app then idles in the tray while the game needs the RAM, so hand it back now.
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);

            return match;
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
}
