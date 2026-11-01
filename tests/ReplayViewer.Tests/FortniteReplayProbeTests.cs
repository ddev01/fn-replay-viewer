namespace ReplayViewer.Tests;

public class FortniteReplayProbeTests
{
    // These two need a real .replay in the Demos folder, so they skip on CI. The copy they
    // depend on is covered deterministically by ReplayFileAccessTests.
    [Fact]
    public async Task Parses_a_local_demo_when_present()
    {
        var file = LatestFinishedReplay();
        Assert.SkipWhen(file is null, "No finished replay in the Demos folder.");
        var copy = ReplayFileAccess.CopyWithReadWriteShare(file!);
        try
        {
            var probe = new FortniteReplayProbe();
            var snapshot = await probe.ReadAsync(copy, TestContext.Current.CancellationToken);
            Assert.NotEmpty(snapshot.Players);
        }
        finally
        {
            File.Delete(copy);
        }
    }

    [Fact]
    public async Task Owner_timeline_does_not_repeat_the_same_elim()
    {
        var file = LatestFinishedReplay();
        Assert.SkipWhen(file is null, "No finished replay in the Demos folder.");
        var copy = ReplayFileAccess.CopyWithReadWriteShare(file!);
        try
        {
            var snapshot = await new FortniteReplayProbe().ReadAsync(
                copy,
                TestContext.Current.CancellationToken
            );
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

    [Fact]
    public void Cancelled_read_stops_before_parsing()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

#pragma warning disable xUnit1051 // A cancelled token is the point of this test.
        Assert.ThrowsAny<OperationCanceledException>(() =>
            new FortniteReplayProbe().Read("missing.replay", cancelled.Token)
        );
#pragma warning restore xUnit1051
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
