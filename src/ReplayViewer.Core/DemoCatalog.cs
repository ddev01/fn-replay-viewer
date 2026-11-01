namespace ReplayViewer.Core;

public static class DemoCatalog
{
    public static IReadOnlyList<DemoListItem> ListRecent(string directory, int take = 20)
    {
        if (take < 1)
        {
            return [];
        }

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
            .Take(take)
            .Select(info => new DemoListItem(
                info.Name,
                info.FullName,
                info.Length,
                new DateTimeOffset(DateTime.SpecifyKind(info.LastWriteTimeUtc, DateTimeKind.Utc))
            ))
            .ToArray();
    }
}
