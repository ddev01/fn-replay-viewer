namespace ReplayViewer.Tests;

public class FortniteReplayProbeTests
{
    [SkippableFact]
    public async Task Parses_a_local_demo_when_present()
    {
        var file = LatestFinishedReplay();
        Skip.If(file is null);
        var copy = ReplayFileAccess.CopyWithReadWriteShare(file);
        try
        {
            var probe = new FortniteReplayProbe();
            var snapshot = await probe.ReadAsync(copy, CancellationToken.None);
            Assert.NotEmpty(snapshot.Players);
        }
        finally
        {
            File.Delete(copy);
        }
    }

    [SkippableFact]
    public async Task Owner_timeline_does_not_repeat_the_same_elim()
    {
        var file = LatestFinishedReplay();
        Skip.If(file is null);
        var copy = ReplayFileAccess.CopyWithReadWriteShare(file);
        try
        {
            var snapshot = await new FortniteReplayProbe().ReadAsync(copy, CancellationToken.None);
            var you = snapshot.Players.First(p => p.IsReplayOwner);
            var rows = snapshot
                .Events.Where(e =>
                    string.Equals(e.ActorId, you.EpicId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(e.TargetId, you.EpicId, StringComparison.OrdinalIgnoreCase)
                )
                .OrderBy(e => e.TimeSeconds)
                .ToArray();

            var repeats = rows.GroupBy(e =>
                    (
                        e.Kind,
                        Actor: e.ActorId ?? e.ActorName,
                        Target: e.TargetId ?? e.TargetName,
                        Bucket: Math.Floor(e.TimeSeconds)
                    )
                )
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToArray();
            Assert.Empty(repeats);
        }
        finally
        {
            File.Delete(copy);
        }
    }

    private static string? LatestFinishedReplay()
    {
        var demos = DemosFolder.DefaultPath;
        if (!Directory.Exists(demos))
        {
            return null;
        }

        foreach (
            var info in Directory
                .EnumerateFiles(demos, "*.replay", SearchOption.TopDirectoryOnly)
                .Select(p => new FileInfo(p))
                .Where(i => i.Exists && i.Length > 1024)
                .OrderByDescending(i => i.LastWriteTimeUtc)
        )
        {
            if (ReplayHeader.TryInspect(info.FullName, out var header) && !header.IsLive)
            {
                return info.FullName;
            }
        }

        return null;
    }
}
