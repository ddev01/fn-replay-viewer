namespace ReplayViewer.Tests;

public class LatestStableReplayTests
{
    [Fact]
    public void Picks_old_file_immediately()
    {
        var dir = CreateDir();
        var path = Path.Combine(dir, "old.replay");
        File.WriteAllBytes(path, [1, 2, 3]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-5));

        var pick = LatestStableReplay.Pick(dir, TimeSpan.FromSeconds(3), DateTimeOffset.UtcNow, _ => { });

        Assert.False(pick.StillWriting);
        Assert.Equal(Path.GetFullPath(path), pick.Path);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Newest_length_change_is_still_writing()
    {
        var dir = CreateDir();
        var path = Path.Combine(dir, "live.replay");
        File.WriteAllBytes(path, [1]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);

        var pick = LatestStableReplay.Pick(
            dir,
            TimeSpan.FromSeconds(3),
            DateTimeOffset.UtcNow,
            _ => File.WriteAllBytes(path, [1, 2, 3, 4])
        );

        Assert.True(pick.StillWriting);
        Assert.Null(pick.Path);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Empty_directory_has_no_pick()
    {
        var dir = CreateDir();
        var pick = LatestStableReplay.Pick(dir, now: DateTimeOffset.UtcNow, delay: _ => { });
        Assert.False(pick.StillWriting);
        Assert.Null(pick.Path);
        Directory.Delete(dir, recursive: true);
    }

    private static string CreateDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ReplayViewerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
