namespace ReplayViewer.Tests;

public sealed class DemoCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "ReplayViewerTests",
        Guid.NewGuid().ToString("N")
    );

    public DemoCatalogTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Lists_replays_newest_first_and_skips_other_files()
    {
        Write("old.replay", 3, DateTime.UtcNow.AddMinutes(-10));
        Write("newest.replay", 1, DateTime.UtcNow.AddMinutes(-1));
        Write("notes.txt", 5, DateTime.UtcNow.AddMinutes(-1));
        Write(Path.Combine("nested", "deep.replay"), 2, DateTime.UtcNow.AddMinutes(-1));

        var items = DemoCatalog.ListRecent(_root);

        Assert.Equal(["newest.replay", "old.replay"], items.Select(i => i.Name).ToArray());
    }

    [Fact]
    public void Ties_break_by_name_descending()
    {
        var sameTime = DateTime.UtcNow.AddMinutes(-5);
        Write("alpha.replay", 1, sameTime);
        Write("bravo.replay", 1, sameTime);

        var items = DemoCatalog.ListRecent(_root);

        Assert.Equal(["bravo.replay", "alpha.replay"], items.Select(i => i.Name).ToArray());
    }

    [Fact]
    public void Take_limits_the_result()
    {
        Write("a.replay", 1, DateTime.UtcNow.AddMinutes(-3));
        Write("b.replay", 1, DateTime.UtcNow.AddMinutes(-2));
        Write("c.replay", 1, DateTime.UtcNow.AddMinutes(-1));

        var items = DemoCatalog.ListRecent(_root, take: 2);

        Assert.Equal(["c.replay", "b.replay"], items.Select(i => i.Name).ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_take_returns_nothing(int take)
    {
        Write("a.replay", 1, DateTime.UtcNow);

        Assert.Empty(DemoCatalog.ListRecent(_root, take));
    }

    [Fact]
    public void Missing_directory_returns_nothing()
    {
        var missing = Path.Combine(_root, "does-not-exist");

        Assert.Empty(DemoCatalog.ListRecent(missing));
    }

    [Fact]
    public void Reports_size_path_and_utc_time()
    {
        var written = DateTime.UtcNow.AddMinutes(-2);
        var file = Write("match.replay", 4, written);

        var item = Assert.Single(DemoCatalog.ListRecent(_root));

        Assert.Equal("match.replay", item.Name);
        Assert.Equal(Path.Combine(_root, "match.replay"), item.Path);
        Assert.Equal(4, item.Length);
        Assert.Equal(TimeSpan.Zero, item.LastWriteUtc.Offset);
        Assert.Equal(
            new DateTimeOffset(DateTime.SpecifyKind(written, DateTimeKind.Utc)),
            item.LastWriteUtc,
            TimeSpan.FromSeconds(2)
        );
        Assert.Equal(file.LastWriteTimeUtc, item.LastWriteUtc.UtcDateTime, TimeSpan.FromSeconds(1));
    }

    private FileInfo Write(string relativePath, int length, DateTime lastWriteUtc)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[length]);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return new FileInfo(path);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
