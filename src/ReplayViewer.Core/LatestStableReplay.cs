namespace ReplayViewer.Core;

public readonly record struct LatestStablePick(string? Path, bool StillWriting);

public static class LatestStableReplay
{
    public static readonly TimeSpan DefaultStableFor = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(5);

    public static LatestStablePick Pick(
        string directory,
        TimeSpan? stableFor = null,
        DateTimeOffset? now = null,
        Action<TimeSpan>? delay = null
    )
    {
        var wait = stableFor is { } requested && requested > TimeSpan.Zero
            ? requested
            : DefaultStableFor;
        var utcNow = now ?? DateTimeOffset.UtcNow;
        delay ??= static span => Thread.Sleep(span);

        if (!Directory.Exists(directory))
        {
            return new LatestStablePick(null, StillWriting: false);
        }

        var newest = Directory
            .EnumerateFiles(directory, "*.replay", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Where(info => info.Exists)
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .ThenByDescending(info => info.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (newest is null)
        {
            return new LatestStablePick(null, StillWriting: false);
        }

        newest.Refresh();
        var lastWrite = DateTime.SpecifyKind(newest.LastWriteTimeUtc, DateTimeKind.Utc);
        if (utcNow - lastWrite >= wait)
        {
            return new LatestStablePick(newest.FullName, StillWriting: false);
        }

        var remaining = wait - (utcNow - lastWrite);
        if (remaining > MaxWait)
        {
            remaining = MaxWait;
        }

        var firstLength = newest.Length;
        delay(remaining);
        newest.Refresh();
        if (!newest.Exists || newest.Length != firstLength)
        {
            return new LatestStablePick(null, StillWriting: true);
        }

        var after = now is null ? DateTimeOffset.UtcNow : utcNow + remaining;
        lastWrite = DateTime.SpecifyKind(newest.LastWriteTimeUtc, DateTimeKind.Utc);
        if (after - lastWrite >= wait)
        {
            return new LatestStablePick(newest.FullName, StillWriting: false);
        }

        return new LatestStablePick(null, StillWriting: true);
    }
}
