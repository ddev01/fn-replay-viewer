namespace ReplayViewer.Tests;

public sealed class AppPathsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "ReplayViewerTests",
        Guid.NewGuid().ToString("N")
    );

    [Fact]
    public void Paths_hang_off_the_root()
    {
        var paths = new AppPaths(_root);

        Assert.Equal(Path.Combine(_root, "logs"), paths.LogsDirectory);
        Assert.Equal(Path.Combine(_root, "ReplayViewer.exe"), paths.InstalledExePath);
        Assert.Equal(_root, paths.RootDirectory);
    }

    [Fact]
    public void EnsureDirectories_creates_root_and_logs()
    {
        new AppPaths(_root).EnsureDirectories();

        Assert.True(Directory.Exists(_root));
        Assert.True(Directory.Exists(Path.Combine(_root, "logs")));
    }

    [Fact]
    public void Blank_root_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new AppPaths(null!));
        Assert.Throws<ArgumentException>(() => new AppPaths("  "));
    }

    [Fact]
    public void Default_install_is_replay_viewer_under_local_app_data()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ReplayViewer"
        );

        Assert.Equal(expected, AppPaths.Default.RootDirectory);
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
