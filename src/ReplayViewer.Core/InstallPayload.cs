namespace ReplayViewer.Core;

/// <summary>
/// Copies a published payload over an existing install directory and removes files the payload
/// no longer contains. Preserved top-level directories hold user data and survive the sync.
/// </summary>
public static class InstallPayload
{
    private const int CopyAttempts = 20;
    private static readonly TimeSpan CopyRetryDelay = TimeSpan.FromMilliseconds(500);

    public static void Sync(
        string sourceDirectory,
        string destinationDirectory,
        IReadOnlyCollection<string> preservedDirectoryNames,
        Action<string>? log = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        ArgumentNullException.ThrowIfNull(preservedDirectoryNames);

        var sourceRoot = Path.GetFullPath(sourceDirectory);
        var destRoot = Path.GetFullPath(destinationDirectory);
        if (string.Equals(sourceRoot, destRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Directory.CreateDirectory(destRoot);
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var dest = Path.Combine(destRoot, Path.GetRelativePath(sourceRoot, file));
            var destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            CopyWithRetry(file, dest);
        }

        var preserved = new HashSet<string>(
            preservedDirectoryNames,
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var dir in Directory.EnumerateDirectories(destRoot).ToArray())
        {
            var name = Path.GetFileName(dir);
            if (preserved.Contains(name))
            {
                continue;
            }

            PruneDirectory(dir, Path.Combine(sourceRoot, name), log);
        }

        foreach (var file in Directory.EnumerateFiles(destRoot).ToArray())
        {
            var name = Path.GetFileName(file);
            // unins* belongs to the installer's uninstaller, not to the payload.
            if (name.StartsWith("unins", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (File.Exists(Path.Combine(sourceRoot, name)))
            {
                continue;
            }

            TryDelete(file, log);
        }
    }

    private static void PruneDirectory(string destDir, string sourceDir, Action<string>? log)
    {
        foreach (var file in Directory.EnumerateFiles(destDir).ToArray())
        {
            if (File.Exists(Path.Combine(sourceDir, Path.GetFileName(file))))
            {
                continue;
            }

            TryDelete(file, log);
        }

        foreach (var dir in Directory.EnumerateDirectories(destDir).ToArray())
        {
            PruneDirectory(dir, Path.Combine(sourceDir, Path.GetFileName(dir)), log);
        }

        if (!Directory.EnumerateFileSystemEntries(destDir).Any())
        {
            try
            {
                Directory.Delete(destDir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log?.Invoke($"could not remove stale directory {destDir}: {ex.Message}");
            }
        }
    }

    private static void TryDelete(string path, Action<string>? log)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"could not remove stale file {path}: {ex.Message}");
        }
    }

    // The previous version may still be exiting, so keep retrying instead of failing the update.
    private static void CopyWithRetry(string source, string destination)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Copy(source, destination, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < CopyAttempts)
            {
                Thread.Sleep(CopyRetryDelay);
            }
        }
    }
}
