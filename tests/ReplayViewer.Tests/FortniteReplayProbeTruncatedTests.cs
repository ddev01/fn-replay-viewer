namespace ReplayViewer.Tests;

public sealed class FortniteReplayProbeTruncatedTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        "ReplayViewerTests",
        Guid.NewGuid().ToString("N") + ".replay"
    );

    public FortniteReplayProbeTruncatedTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        // Half a header: this is what a replay looks like while Fortnite is still recording it.
        File.WriteAllBytes(_path, new byte[512]);
    }

    public void Dispose() => File.Delete(_path);

    [Fact]
    public async Task A_truncated_replay_is_reported_as_unreadable()
    {
        var probe = new FortniteReplayProbe();

        await Assert.ThrowsAsync<UnreadableReplayException>(() =>
            probe.ReadAsync(_path, TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task The_unreadable_message_says_it_may_still_be_recording()
    {
        var probe = new FortniteReplayProbe();

        var ex = await Assert.ThrowsAsync<UnreadableReplayException>(() =>
            probe.ReadAsync(_path, TestContext.Current.CancellationToken)
        );

        Assert.Contains("still be recording", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
