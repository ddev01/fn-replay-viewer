using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using FortniteReplayReader;
using FortniteReplayReader.Models;
using Unreal.Core;
using Unreal.Core.Contracts;
using Unreal.Core.Models;
using Unreal.Core.Models.Enums;
using ReplayReader = FortniteReplayReader.ReplayReader;

namespace ReplayViewer.Core;

/// <summary>
/// Recovers player names the parser drops. It normally takes a name from a separate "external
/// data" block; some Creative replays have none, so every player came back unnamed although the
/// file holds the name in plain text. Each player has a <c>FNEPlayerComponent</c> bunch holding
/// their epic id (<c>UniquePlayerId</c>) and <c>PlayerName</c>; this reads those bunches and names
/// any player the parser left unnamed, so the parser's own name still wins whenever it has one.
/// </summary>
internal sealed class NamedReplayReader : ReplayReader
{
    private const string ComponentPath = "/Script/FortniteGame.FNEPlayerComponent";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private readonly List<(byte[] Id, string Name)> _components = [];

    public NamedReplayReader(ParseMode parseMode)
        : base(parseMode: parseMode) { }

    public override bool ReceiveProperties(
        FBitArchive archive,
        NetFieldExportGroup group,
        uint channelIndex,
        [NotNullWhen(true)] out INetFieldExportGroup? exportGroup,
        bool enablePropertyChecksum = true,
        bool netDeltaUpdate = false
    )
    {
        // The parser has no model for this group, so it would skip the bunch; read it first.
        if (group.PathName == ComponentPath)
        {
            Capture(archive, group, enablePropertyChecksum);
        }

        return base.ReceiveProperties(archive, group, channelIndex, out exportGroup, enablePropertyChecksum, netDeltaUpdate);
    }

    /// <summary>Names every player the parser left unnamed. Call after the replay was read.</summary>
    public void ApplyNames()
    {
        if (_components.Count == 0)
        {
            return;
        }

        try
        {
            // The parser keeps its players on a builder it makes for each replay it reads.
            var builder = Field(GetType(), "Builder")?.GetValue(this);
            var players = builder is null ? null : Field(builder.GetType(), "_players")?.GetValue(builder) as IDictionary;
            if (players is null)
            {
                return;
            }

            foreach (var value in players.Values)
            {
                if (value is PlayerData { PlayerName: null or "" } player && EpicBytes(player) is { } id)
                {
                    var match = _components.FirstOrDefault(c => ContainsAtAnyBit(c.Id, id));
                    if (match.Name is not null)
                    {
                        player.PlayerName = match.Name;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidCastException or AmbiguousMatchException)
        {
            // shortcut: reflects into the parser's private player table; if an update moves it the
            // names stay empty as before. Upgrade when the parser exposes this component itself.
        }
    }

    private void Capture(FBitArchive archive, NetFieldExportGroup group, bool checksum)
    {
        var start = archive.Position;
        try
        {
            if (checksum)
            {
                archive.ReadBit();
            }

            byte[]? id = null;
            string? name = null;
            while (!archive.AtEnd())
            {
                var handle = archive.ReadIntPacked();
                if (handle == 0 || !group.IsValidIndex(--handle))
                {
                    break;
                }

                var numBits = archive.ReadIntPacked();
                if (numBits == 0)
                {
                    continue;
                }

                switch (group.NetFieldExports[handle]?.Name)
                {
                    case "UniquePlayerId":
                        id = archive.ReadBits(numBits).ToArray();
                        break;
                    case "PlayerName":
                        name = ReadString(archive.ReadBits(numBits));
                        break;
                    default:
                        archive.SkipBits(numBits);
                        break;
                }
            }

            if (id is not null && !string.IsNullOrWhiteSpace(name))
            {
                _components.Add((id, name.Trim()));
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException or InvalidOperationException)
        {
            // A bunch that does not parse just contributes no name.
        }
        finally
        {
            // The parser reads the same bits next.
            typeof(FArchive).GetProperty(nameof(FArchive.Position))!.GetSetMethod(true)!.Invoke(archive, [start]);
        }
    }

    // An Unreal FString: int32 length including the terminator, negative for UTF-16.
    internal static string? ReadString(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
        {
            return null;
        }

        var length = BitConverter.ToInt32(data);
        if (length > 1 && data.Length >= 4 + length)
        {
            return Encoding.UTF8.GetString(data.Slice(4, length - 1));
        }

        if (length < -1 && data.Length >= 4 + (-length * 2))
        {
            return Encoding.Unicode.GetString(data.Slice(4, (-length - 1) * 2));
        }

        return null;
    }

    private static byte[]? EpicBytes(PlayerData player)
    {
        var epic = string.IsNullOrWhiteSpace(player.EpicId) ? player.PlayerId : player.EpicId;
        return epic is { Length: 32 } && Convert.FromHexString(epic) is var bytes ? bytes : null;
    }

    // The id sits at an arbitrary bit offset inside the property payload.
    internal static bool ContainsAtAnyBit(byte[] payload, byte[] id)
    {
        for (var shift = 0; shift < 8; shift++)
        {
            var shifted = new byte[payload.Length];
            for (var i = 0; i < shifted.Length; i++)
            {
                var next = i + 1 < payload.Length ? payload[i + 1] : 0;
                shifted[i] = (byte)((payload[i] >> shift) | (next << (8 - shift)));
            }

            if (shifted.AsSpan().IndexOf(id) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static FieldInfo? Field(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(name, Any | BindingFlags.DeclaredOnly);
            if (field is not null)
            {
                return field;
            }
        }

        return null;
    }
}
