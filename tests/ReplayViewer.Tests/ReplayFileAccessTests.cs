namespace ReplayViewer.Tests;

public sealed class ReplayFileAccessTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "ReplayViewerTests",
        Guid.NewGuid().ToString("N")
    );

    public ReplayFileAccessTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Open_succeeds_while_the_game_still_holds_the_source_open_for_writing()
    {
        // The whole point of the sharing mode: the game is still writing the demo when we read.
        var source = WriteSource("still being written");
        using var held = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete
        );

        using var stream = ReplayFileAccess.OpenSharedRead(source);
        using var reader = new StreamReader(stream);

        Assert.Equal("still being written", reader.ReadToEnd());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Open_rejects_a_blank_source(string sourcePath)
    {
        Assert.ThrowsAny<ArgumentException>(() => ReplayFileAccess.OpenSharedRead(sourcePath));
    }

    [Fact]
    public void Sweep_removes_old_leftover_copies_and_keeps_recent_ones()
    {
        var old = WriteSource("old", "old.replay");
        var recent = WriteSource("recent", "recent.replay");
        var other = WriteSource("other", "notes.txt");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-3));
        File.SetLastWriteTimeUtc(other, DateTime.UtcNow.AddHours(-3));

        ReplayFileAccess.SweepLegacyCopies(_dir, TimeSpan.FromHours(1));

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(other));
    }

    [Fact]
    public void Sweep_ignores_a_missing_directory()
    {
        ReplayFileAccess.SweepLegacyCopies(Path.Combine(_dir, "absent"), TimeSpan.FromHours(1));
    }

    private string WriteSource(string content, string name = "demo.replay")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }
}
