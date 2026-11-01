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

    [Fact]
    public void Missing_directory_has_no_pick()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ReplayViewerTests", Guid.NewGuid().ToString("N"));

        var pick = LatestStableReplay.Pick(dir);

        Assert.False(pick.StillWriting);
        Assert.Null(pick.Path);
    }

    [Fact]
    public void Same_length_touch_while_waiting_is_still_writing()
    {
        var dir = CreateDir();
        var path = Path.Combine(dir, "live.replay");
        File.WriteAllBytes(path, [1, 2, 3]);
        WriteTime(path, DateTime.UtcNow.AddSeconds(-1));

        var pick = LatestStableReplay.Pick(
            dir,
            TimeSpan.FromSeconds(3),
            DateTimeOffset.UtcNow,
            _ => WriteTime(path, DateTime.UtcNow)
        );

        Assert.True(pick.StillWriting);
        Assert.Null(pick.Path);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void File_removed_while_waiting_is_still_writing()
    {
        var dir = CreateDir();
        var path = Path.Combine(dir, "live.replay");
        File.WriteAllBytes(path, [1, 2, 3]);
        WriteTime(path, DateTime.UtcNow);

        var pick = LatestStableReplay.Pick(
            dir,
            TimeSpan.FromSeconds(3),
            DateTimeOffset.UtcNow,
            _ => File.Delete(path)
        );

        Assert.True(pick.StillWriting);
        Assert.Null(pick.Path);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Stable_after_the_wait_returns_the_file()
    {
        var dir = CreateDir();
        var path = Path.Combine(dir, "live.replay");
        File.WriteAllBytes(path, [1, 2, 3]);
        WriteTime(path, DateTime.UtcNow);

        var pick = LatestStableReplay.Pick(
            dir,
            TimeSpan.FromSeconds(3),
            DateTimeOffset.UtcNow,
            _ => WriteTime(path, DateTime.UtcNow.AddSeconds(-10))
        );

        Assert.False(pick.StillWriting);
        Assert.Equal(Path.GetFullPath(path), pick.Path);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Newest_of_several_old_files_wins()
    {
        var dir = CreateDir();
        var older = Path.Combine(dir, "older.replay");
        var newest = Path.Combine(dir, "newest.replay");
        File.WriteAllBytes(older, [1]);
        File.WriteAllBytes(newest, [2]);
        WriteTime(older, DateTime.UtcNow.AddMinutes(-10));
        WriteTime(newest, DateTime.UtcNow.AddMinutes(-2));

        var pick = LatestStableReplay.Pick(dir, delay: _ => { });

        Assert.Equal(Path.GetFullPath(newest), pick.Path);
        Assert.False(pick.StillWriting);
        Directory.Delete(dir, recursive: true);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Non_positive_window_falls_back_to_the_default(int seconds)
    {
        var dir = CreateDir();
        var path = Path.Combine(dir, "old.replay");
        File.WriteAllBytes(path, [1]);
        WriteTime(path, DateTime.UtcNow.AddMinutes(-1));

        var pick = LatestStableReplay.Pick(dir, TimeSpan.FromSeconds(seconds), delay: _ => { });

        Assert.Equal(Path.GetFullPath(path), pick.Path);
        Assert.False(pick.StillWriting);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Real_clock_still_reports_a_just_written_file()
    {
        var dir = CreateDir();
        var path = Path.Combine(dir, "live.replay");
        File.WriteAllBytes(path, [1, 2, 3]);

        var pick = LatestStableReplay.Pick(dir, TimeSpan.FromHours(1), delay: _ => { });

        Assert.True(pick.StillWriting);
        Assert.Null(pick.Path);
        Directory.Delete(dir, recursive: true);
    }

    private static void WriteTime(string path, DateTime utc) =>
        File.SetLastWriteTimeUtc(path, DateTime.SpecifyKind(utc, DateTimeKind.Utc));

    private static string CreateDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ReplayViewerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
