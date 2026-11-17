using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReplayViewer.Core;

public sealed record CachedMatch(
    string Name,
    long Length,
    long LastWriteTicks,
    string? CacheKey,
    string? MapName,
    string? Playlist,
    DateTimeOffset? RecordedAt,
    double? DurationSeconds,
    int? Placement,
    int? Elims,
    bool Failed = false
);

/// <summary>
/// Sidebar metadata for matches the release cache already stores in compressed form. This index
/// only holds the small summary fields, so listing recent replays never decompresses a match.
/// </summary>
/// shortcut: entries for replays deleted from Demos stay in the index until rewritten.
public sealed class MatchCacheIndex
{
    private const string IndexFileName = "index.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly object _gate = new();
    private readonly string _directory;
    private Dictionary<string, CachedMatch>? _entries;

    public MatchCacheIndex(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    public static MatchCacheIndex Default { get; } =
        new(Path.Combine(AppPaths.Default.RootDirectory, "cache-index"));

    public CachedMatch? Get(DemoListItem replay, string? cacheKey)
    {
        ArgumentNullException.ThrowIfNull(replay);

        lock (_gate)
        {
            if (
                Entries.TryGetValue(replay.Name, out var entry)
                && entry.Length == replay.Length
                && entry.LastWriteTicks == replay.LastWriteUtc.UtcTicks
                && (cacheKey is null || entry.CacheKey == cacheKey)
            )
            {
                return entry;
            }

            return null;
        }
    }

    public void Save(DemoListItem replay, string? cacheKey, MatchDocument match)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(match);

        lock (_gate)
        {
            var you = match.Teams.SelectMany(t => t.Players).FirstOrDefault(p => p.IsYou);
            Entries[replay.Name] = new CachedMatch(
                replay.Name,
                replay.Length,
                replay.LastWriteUtc.UtcTicks,
                cacheKey,
                match.MapName ?? MapNames.ForPlaylist(match.Playlist),
                match.Playlist,
                match.RecordedAt,
                match.DurationSeconds,
                match.YourPlacement ?? match.TeamPlacement,
                match.OwnerStats?.Eliminations ?? you?.Kills
            );
            WriteIndex();
        }
    }

    /// <summary>Remembers a parse failure so backfill does not retry the same file.</summary>
    public void SaveFailure(DemoListItem replay, string? cacheKey)
    {
        ArgumentNullException.ThrowIfNull(replay);

        lock (_gate)
        {
            Entries[replay.Name] = new CachedMatch(
                replay.Name,
                replay.Length,
                replay.LastWriteUtc.UtcTicks,
                cacheKey,
                MapName: null,
                Playlist: null,
                RecordedAt: null,
                DurationSeconds: null,
                Placement: null,
                Elims: null,
                Failed: true
            );
            WriteIndex();
        }
    }

    private void WriteIndex()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, IndexFileName);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Entries.Values.ToArray(), Json));
        File.Move(temp, path, overwrite: true);
    }

    private Dictionary<string, CachedMatch> Entries
    {
        get
        {
            if (_entries is not null)
            {
                return _entries;
            }

            CachedMatch[] loaded;
            try
            {
                var path = Path.Combine(_directory, IndexFileName);
                loaded = File.Exists(path)
                    ? JsonSerializer.Deserialize<CachedMatch[]>(File.ReadAllText(path), Json) ?? []
                    : [];
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                loaded = [];
            }

            _entries = loaded
                .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
            return _entries;
        }
    }
}
