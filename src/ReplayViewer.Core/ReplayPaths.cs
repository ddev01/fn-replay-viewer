namespace ReplayViewer.Core;

public static class ReplayPaths
{
    public static string ResolveInside(string directory, string replayPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(replayPath);

        var root = Path.GetFullPath(directory);
        // A bare file name is what the page keeps in the URL; an absolute path still wins over
        // the folder, and the check below is what keeps both inside it.
        var full = Path.GetFullPath(Path.Combine(root, replayPath));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (
            !full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(full, root, StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new UnauthorizedAccessException("Replay path is not in the Demos folder.");
        }

        if (!full.EndsWith(".replay", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Only .replay files can be opened.");
        }

        return full;
    }
}
