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
    int? TotalPlayers = null,
    bool Failed = false,
    int Schema = 0,
    string? AccountId = null,
    string? AccountName = null,
    IReadOnlyList<string>? Players = null
);

/// <summary>
/// Sidebar metadata for matches the release cache already stores in compressed form. This index
/// only holds the small summary fields, so listing recent replays never decompresses a match.
/// </summary>
/// shortcut: entries for replays deleted from Demos stay in the index until rewritten.
public sealed class MatchCacheIndex
{
    /// <summary>
    /// Bumped when a saved summary field changes meaning, so entries written by an older build
    /// are rebuilt from the cached match instead of showing the old value.
    /// </summary>
    public const int CurrentSchema = 8;

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
            var players = match.Teams.Sum(t => t.Players.Count);
            var account = AccountOf(match);
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
                // The roster is what the page shows, so it wins over the event-chunk stats,
                // which are wrong or missing in some replays.
                you?.Kills ?? match.OwnerStats?.Eliminations,
                players > 0 ? players : null,
                Schema: CurrentSchema,
                AccountId: account.Id,
                AccountName: account.Name,
                Players: PlayersOf(match)
            );
            WriteIndex();
        }
    }

    /// <summary>
    /// Who the sidebar's player search looks through: every named player but the recorder and bots.
    /// "Anonymous" players still have a name in the replay, so they stay searchable.
    /// </summary>
    public static IReadOnlyList<string> PlayersOf(MatchDocument match)
    {
        ArgumentNullException.ThrowIfNull(match);

        return match
            .Teams.SelectMany(t => t.Players)
            .Where(p => !p.IsYou && !p.IsBot && !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => p.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// The recorder's account: what the sidebar filters on. The id is stable across a rename; the
    /// name is only a fallback and the label. Null when the replay does not say who recorded it.
    /// </summary>
    public static (string? Id, string? Name) AccountOf(MatchDocument match)
    {
        ArgumentNullException.ThrowIfNull(match);

        var you = match.Teams.SelectMany(t => t.Players).FirstOrDefault(p => p.IsYou);
        return you is null ? (null, null) : ((you.EpicId ?? you.Name).ToLowerInvariant(), you.Name);
    }

    /// <summary>
    /// Adds the account to an entry written before accounts were stored, without touching the
    /// summary fields: a re-save from the cached match would lose what only the replay file knew.
    /// </summary>
    public void UpdateAccount(
        string name,
        (string? Id, string? Name) account,
        IReadOnlyList<string>? players = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (_gate)
        {
            if (Entries.TryGetValue(name, out var entry))
            {
                Entries[name] = entry with
                {
                    AccountId = account.Id,
                    AccountName = account.Name,
                    Players = players ?? entry.Players,
                    Schema = CurrentSchema,
                };
                WriteIndex();
            }
        }
    }

    /// <summary>
    /// The key last saved for a replay file name, whether or not the file still exists. Opening
    /// a cached replay whose .replay file Fortnite already deleted needs this.
    /// </summary>
    public string? CacheKeyFor(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (_gate)
        {
            return Entries.TryGetValue(name, out var entry) && !entry.Failed ? entry.CacheKey : null;
        }
    }

    /// <summary>Every remembered entry; the caller decides which replays still exist.</summary>
    public IReadOnlyList<CachedMatch> All()
    {
        lock (_gate)
        {
            return Entries.Values.ToArray();
        }
    }

    /// <summary>Forgets a replay, e.g. after the user deleted it.</summary>
    public void Remove(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (_gate)
        {
            if (Entries.Remove(name))
            {
                WriteIndex();
            }
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
                Failed: true,
                Schema: CurrentSchema
            );
            WriteIndex();
        }
    }

    private void WriteIndex()
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, IndexFileName);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Entries.Values.ToArray(), Json));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The sidebar summary is a convenience: a locked or full disk must not turn a good
            // parse into an error. The entry stays in memory and the next save retries the write.
        }
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
