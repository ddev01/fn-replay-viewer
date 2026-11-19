using System.Text.Json;

namespace ReplayViewer.Core;

/// <summary>
/// Epic ids this machine has recorded as the replay owner. They are learned from replays that do
/// name the owner, so a replay that does not can still be recognised through its roster.
/// </summary>
/// shortcut: the set grows with every account played on this machine and is never pruned; it
/// stays tiny (one id per account).
public sealed class OwnerIdentities
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _gate = new();
    private readonly string _path;
    private HashSet<string>? _ids;

    public OwnerIdentities(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    public static OwnerIdentities Default { get; } =
        new(Path.Combine(AppPaths.Default.RootDirectory, "owners.json"));

    public IReadOnlySet<string> Ids
    {
        get
        {
            lock (_gate)
            {
                return Load();
            }
        }
    }

    /// <summary>Remembers the owner of a match that names one.</summary>
    public void Learn(MatchDocument match)
    {
        ArgumentNullException.ThrowIfNull(match);
        var you = match
            .Teams.SelectMany(team => team.Players)
            .FirstOrDefault(player => player.IsYou && player.EpicId is not null);
        if (you?.EpicId is not null)
        {
            Add(you.EpicId);
        }
    }

    /// <summary>Remembers an id picked by hand for an account no replay has named yet.</summary>
    public void Add(string epicId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(epicId);

        lock (_gate)
        {
            if (!Load().Add(epicId.Trim()))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(Load().ToArray(), Json));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Remembering is an optimisation; a failed write must not fail the scrape.
            }
        }
    }

    private HashSet<string> Load()
    {
        if (_ids is not null)
        {
            return _ids;
        }

        string[] loaded;
        try
        {
            loaded = File.Exists(_path)
                ? JsonSerializer.Deserialize<string[]>(File.ReadAllText(_path), Json) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            loaded = [];
        }

        _ids = new HashSet<string>(loaded, StringComparer.OrdinalIgnoreCase);
        return _ids;
    }
}
