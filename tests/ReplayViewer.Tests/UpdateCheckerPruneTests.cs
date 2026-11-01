namespace ReplayViewer.Tests;

public sealed class UpdateCheckerPruneTests
{
    [Fact]
    public void Prune_removes_every_update_folder_except_the_current_one()
    {
        var root = TempDir();
        var current = Path.Combine(root, Guid.NewGuid().ToString("N"));
        var stale = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(current);
        Directory.CreateDirectory(stale);
        File.WriteAllText(Path.Combine(stale, "update.zip"), "old");

        UpdateChecker.PruneOldUpdates(root, keep: current);

        Assert.True(Directory.Exists(current));
        Assert.False(Directory.Exists(stale));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Prune_tolerates_a_missing_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "ReplayViewerTests", Guid.NewGuid().ToString("N"));

        UpdateChecker.PruneOldUpdates(root, keep: Path.Combine(root, "current"));
    }

    private static string TempDir()
    {
        var dir = Path.Combine(
            Path.GetTempPath(),
            "ReplayViewerTests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(dir);
        return dir;
    }
}
