using System.Collections;
using System.Reflection;
using FortniteReplayReader.Models;

namespace ReplayViewer.Core;

/// <summary>
/// Names the player who recorded the replay. The file's game state holds the recorder's
/// PlayerState actor id in <see cref="GameData.RecorderId"/>, and the parser maps that id to the
/// player it belongs to — but it never exposes the map and its own IsReplayOwner flag misses
/// whenever that reference resolves late, which is most of the time (50 of 50 labelled replays
/// agree with this lookup, while IsReplayOwner was set in 42).
/// </summary>
public static class RecorderResolver
{
    private const string BuilderField = "Builder";
    private const string PlayerActors = "_actorToChannel";
    private const string Players = "_players";

    /// <summary>
    /// Reads the recorder's epic id out of the parser's own actor→channel→player map. Returns
    /// null when the replay has no game state (Creative) or the map is not where it is expected.
    /// </summary>
    public static string? EpicIdOf(object reader, FortniteReplay replay)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(replay);

        var recorded = replay.GameData?.RecorderId;
        if (recorded is not uint recorderId)
        {
            return null;
        }

        try
        {
            var builder = Field(reader.GetType(), BuilderField)?.GetValue(reader);
            if (builder is null)
            {
                return null;
            }

            var byActor = Field(builder.GetType(), PlayerActors)?.GetValue(builder) as IDictionary;
            var players = Field(builder.GetType(), Players)?.GetValue(builder) as IDictionary;
            if (byActor is null || players is null)
            {
                return null;
            }

            // The recorder id is the player state's net guid; the maps key players by channel.
            var key = byActor[recorderId] ?? recorderId;
            return players[key] is PlayerData player ? EpicId(player) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidCastException or AmbiguousMatchException)
        {
            // A parser update can move these fields; the caller falls back to learned ids.
            return null;
        }
    }

    /// <summary>Flags the recorder as you unless the replay already names an owner.</summary>
    public static PlayerSnapshot[] ApplyRecorder(
        IReadOnlyList<PlayerSnapshot> players,
        string? recorderEpicId
    )
    {
        ArgumentNullException.ThrowIfNull(players);

        var result = players.ToArray();
        if (string.IsNullOrWhiteSpace(recorderEpicId) || result.Any(player => player.IsReplayOwner))
        {
            return result;
        }

        var index = Array.FindIndex(
            result,
            player =>
                string.Equals(player.EpicId, recorderEpicId, StringComparison.OrdinalIgnoreCase)
        );
        if (index >= 0)
        {
            result[index] = result[index] with { IsReplayOwner = true };
        }

        return result;
    }

    private static string? EpicId(PlayerData player)
    {
        var epic = string.IsNullOrWhiteSpace(player.EpicId) ? player.PlayerId : player.EpicId;
        return string.IsNullOrWhiteSpace(epic) ? null : epic.Trim();
    }

    private static FieldInfo? Field(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
            );
            if (field is not null)
            {
                return field;
            }
        }

        return null;
    }
}

