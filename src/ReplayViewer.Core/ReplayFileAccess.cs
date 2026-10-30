namespace ReplayViewer.Core;

public static class ReplayFileAccess
{
    public static string CopyWithReadWriteShare(string sourcePath, string? destinationPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        destinationPath ??= Path.Combine(
            Path.GetTempPath(),
            "ReplayViewer",
            $"{Path.GetFileNameWithoutExtension(sourcePath)}-{Guid.NewGuid():N}.replay"
        );

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var src = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );
        using var dst = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read
        );
        src.CopyTo(dst);
        return destinationPath;
    }
}
