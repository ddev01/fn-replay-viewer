namespace ReplayViewer.Core;

public static class ReplayFileAccess
{
    /// <summary>
    /// Real replays are 17-21 MB. Anything this large is not one, and parsing it would only burn
    /// time and memory, so it is refused before the parser sees it.
    /// </summary>
    public const long MaxReplayBytes = 512L * 1024 * 1024;

    /// <summary>
    /// Opens a replay for reading while Fortnite may still hold it open for writing. Parsing
    /// straight from this stream means there is no temp copy to leak when the parser is killed.
    /// </summary>
    public static FileStream OpenSharedRead(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    }

    /// <summary>
    /// Older builds copied each replay to temp before parsing and leaked the copy whenever the
    /// parser was killed. Removes what is left over; best effort.
    /// </summary>
    public static void SweepLegacyCopies(string directory, TimeSpan olderThan)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            var cutoff = DateTime.UtcNow - olderThan;
            foreach (var file in Directory.EnumerateFiles(directory, "*.replay"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
