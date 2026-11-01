namespace ReplayViewer.Tests;

public class ReplayPathsTests
{
    [Fact]
    public void Accepts_a_replay_inside_the_folder()
    {
        var dir = CreateDir();
        var path = Path.Combine(dir, "match.replay");
        File.WriteAllBytes(path, [1]);

        var resolved = ReplayPaths.ResolveInside(dir, path);

        Assert.Equal(Path.GetFullPath(path), resolved);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Rejects_a_path_outside_the_folder()
    {
        var dir = CreateDir();
        var outsider = Path.Combine(Path.GetTempPath(), "ReplayViewerTests", Guid.NewGuid().ToString("N") + ".replay");
        File.WriteAllBytes(outsider, [1]);

        Assert.Throws<UnauthorizedAccessException>(() => ReplayPaths.ResolveInside(dir, outsider));

        File.Delete(outsider);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Rejects_non_replay_extension()
    {
        var dir = CreateDir();
        var path = Path.Combine(dir, "notes.txt");
        File.WriteAllBytes(path, [1]);

        Assert.Throws<UnauthorizedAccessException>(() => ReplayPaths.ResolveInside(dir, path));

        Directory.Delete(dir, recursive: true);
    }

    private static string CreateDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ReplayViewerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
