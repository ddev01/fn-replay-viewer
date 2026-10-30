namespace ReplayViewer.Core;

public sealed class DemosFolder
{
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FortniteGame",
            "Saved",
            "Demos"
        );

    public static IReadOnlyList<string> EnumerateReplayFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(directory, "*.replay", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static (long Length, DateTime LastWriteUtc) Snapshot(string path)
    {
        var info = new FileInfo(path);
        info.Refresh();
        return (
            info.Exists ? info.Length : 0,
            info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue
        );
    }
}
