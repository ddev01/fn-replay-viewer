namespace ReplayViewer.Tests;

public class FortniteReplayProbeTests
{
    // These two need a real .replay in the Demos folder, so they skip on CI. The copy they
    // depend on is covered deterministically by ReplayFileAccessTests.
    [Fact]
    public async Task Parses_a_local_demo_when_present()
    {
        var snapshot = await ReadNewestFinishedAsync();
        Assert.SkipWhen(snapshot is null, "No finished replay in the Demos folder.");
        Assert.NotEmpty(snapshot!.Players);
    }

    [Fact]
    public async Task Owner_timeline_does_not_repeat_the_same_elim()
    {
        var snapshot = await ReadNewestFinishedAsync();
        Assert.SkipWhen(snapshot is null, "No finished replay in the Demos folder.");

        var you = snapshot!.Players.First(p => p.IsReplayOwner);
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

    /// <summary>
    /// Newest replay that actually parses. While Fortnite is recording, the newest file is
    /// half written and is rejected, so this walks down the list rather than taking the top.
    /// </summary>
    private static async Task<ParsedReplaySnapshot?> ReadNewestFinishedAsync()
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
            var copy = ReplayFileAccess.CopyWithReadWriteShare(info.FullName);
            try
            {
                var snapshot = await new FortniteReplayProbe().ReadAsync(
                    copy,
                    TestContext.Current.CancellationToken
                );
                // Some finished replays never record an owner, and the Owner_timeline test
                // needs one, so keep looking rather than taking the newest blindly.
                if (snapshot.Players.Any(p => p.IsReplayOwner))
                {
                    return snapshot;
                }
            }
            catch (Exception ex) when (ex is ReplayStillWritingException or UnreadableReplayException)
            {
                // Truncated or unreadable; try the next one down.
            }
            finally
            {
                File.Delete(copy);
            }
        }

        return null;
    }
}
