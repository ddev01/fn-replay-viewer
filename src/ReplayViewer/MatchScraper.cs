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

        var copy = ReplayFileAccess.CopyWithReadWriteShare(path);
        try
        {
            var snapshot = await _probe.ReadAsync(copy, cancellationToken).ConfigureAwait(false);
            snapshot = snapshot with { SourceFileName = Path.GetFileName(path) };
            return MatchAssembler.Build(snapshot, DateTimeOffset.UtcNow);
        }
        finally
        {
            try
            {
                File.Delete(copy);
            }
            catch (IOException)
            {
            }
        }
    }
}
