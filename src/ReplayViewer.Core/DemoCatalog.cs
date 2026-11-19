namespace ReplayViewer.Core;

public static class DemoCatalog
{
    public static IReadOnlyList<DemoListItem> ListAll(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(directory, "*.replay", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Where(info => info.Exists)
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .ThenByDescending(info => info.Name, StringComparer.OrdinalIgnoreCase)
            .Select(info => new DemoListItem(
                info.Name,
                info.FullName,
                info.Length,
                new DateTimeOffset(DateTime.SpecifyKind(info.LastWriteTimeUtc, DateTimeKind.Utc))
            ))
            .ToArray();
    }
}
