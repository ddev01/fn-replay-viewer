namespace ReplayViewer.Core;

/// <summary>
/// Copies a published payload over an existing install directory and removes files the payload
/// no longer contains. Preserved top-level directories hold user data and survive the sync.
/// The replacement is all-or-nothing: every file is first copied next to its target as ".new",
/// then swapped in with the old file kept as ".old"; if any step fails everything is put back,
/// so a failed update cannot leave a mix of old and new files.
/// </summary>
public static class InstallPayload
{
    private const int CopyAttempts = 20;
    private static readonly TimeSpan CopyRetryDelay = TimeSpan.FromMilliseconds(500);
    private const string StagedSuffix = ".new";
    private const string BackupSuffix = ".old";

    public static void Sync(
        string sourceDirectory,
        string destinationDirectory,
        IReadOnlyCollection<string> preservedDirectoryNames,
        Action<string>? log = null
    ) =>
        Sync(
            sourceDirectory,
            destinationDirectory,
            preservedDirectoryNames,
            log,
            CopyAttempts,
            CopyRetryDelay
        );

    internal static void Sync(
        string sourceDirectory,
        string destinationDirectory,
        IReadOnlyCollection<string> preservedDirectoryNames,
        Action<string>? log,
        int attempts,
        TimeSpan retryDelay
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
        var files = Directory
            .EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(file => !file.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .Select(file => (Source: file, Dest: Path.Combine(destRoot, Path.GetRelativePath(sourceRoot, file))))
            .ToArray();
        Replace(files, attempts, retryDelay);

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

            // A preserved name protects a user-owned top-level file just as it does a directory.
            if (preserved.Contains(name))
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

    private static void Replace((string Source, string Dest)[] files, int attempts, TimeSpan delay)
    {
        // Stage: nothing in the install is touched yet, so a failure here changes nothing.
        var staged = new List<(string Dest, string Temp)>();
        try
        {
            foreach (var (source, dest) in files)
            {
                var destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                var temp = dest + StagedSuffix;
                Retry(() => File.Copy(source, temp, overwrite: true), attempts, delay);
                staged.Add((dest, temp));
            }
        }
        catch
        {
            DeleteQuietly(staged.Select(s => s.Temp));
            throw;
        }

        // Swap: keep each old file until every new one is in place.
        var swapped = new List<(string Dest, string? Backup)>();
        try
        {
            foreach (var (dest, temp) in staged)
            {
                string? backup = null;
                if (File.Exists(dest))
                {
                    var target = dest + BackupSuffix;
                    Retry(() => File.Move(dest, target, overwrite: true), attempts, delay);
                    backup = target;
                }

                swapped.Add((dest, backup));
                Retry(() => File.Move(temp, dest, overwrite: true), attempts, delay);
            }
        }
        catch
        {
            Rollback(swapped);
            DeleteQuietly(staged.Select(s => s.Temp));
            throw;
        }

        DeleteQuietly(swapped.Where(s => s.Backup is not null).Select(s => s.Backup!));
    }

    private static void Rollback(List<(string Dest, string? Backup)> swapped)
    {
        for (var i = swapped.Count - 1; i >= 0; i--)
        {
            var (dest, backup) = swapped[i];
            try
            {
                if (backup is not null)
                {
                    File.Move(backup, dest, overwrite: true);
                }
                else if (File.Exists(dest))
                {
                    File.Delete(dest);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort: report the original failure, not this one.
            }
        }
    }

    private static void DeleteQuietly(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    // The previous version may still be exiting, so keep retrying instead of failing the update.
    private static void Retry(Action action, int attempts, TimeSpan delay)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (IOException) when (attempt < attempts)
            {
                Thread.Sleep(delay);
            }
        }
    }
}
