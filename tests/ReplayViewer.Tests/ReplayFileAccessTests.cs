namespace ReplayViewer.Tests;

/// <summary>
/// Deterministic coverage for the copy the scrape path uses. The replay-dependent tests in
/// <see cref="FortniteReplayProbeTests"/> skip on machines without a demo in the Demos folder,
/// which is why this class exists: none of it needs a real replay.
/// </summary>
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
    public void Copy_writes_the_destination_and_returns_it()
    {
        var source = WriteSource("content");
        var destination = Path.Combine(_dir, "copy.replay");

        var result = ReplayFileAccess.CopyWithReadWriteShare(source, destination);

        Assert.Equal(destination, result);
        Assert.Equal("content", File.ReadAllText(destination));
    }

    [Fact]
    public void Copy_succeeds_while_the_game_still_holds_the_source_open_for_writing()
    {
        // The whole point of the sharing mode: the game is still writing the demo when we copy.
        var source = WriteSource("still being written");
        using var held = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete
        );
        var destination = Path.Combine(_dir, "copy.replay");

        ReplayFileAccess.CopyWithReadWriteShare(source, destination);

        Assert.Equal("still being written", File.ReadAllText(destination));
    }

    [Fact]
    public void Copy_defaults_the_destination_into_the_temp_folder()
    {
        var source = WriteSource("content");

        var result = ReplayFileAccess.CopyWithReadWriteShare(source);

        try
        {
            Assert.True(File.Exists(result));
            Assert.StartsWith(
                Path.Combine(Path.GetTempPath(), "ReplayViewer"),
                result,
                StringComparison.OrdinalIgnoreCase
            );
            Assert.Equal("content", File.ReadAllText(result));
        }
        finally
        {
            File.Delete(result);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Copy_rejects_a_blank_source(string sourcePath)
    {
        Assert.ThrowsAny<ArgumentException>(() => ReplayFileAccess.CopyWithReadWriteShare(sourcePath));
    }

    private string WriteSource(string content)
    {
        var path = Path.Combine(_dir, "demo.replay");
        File.WriteAllText(path, content);
        return path;
    }
}
