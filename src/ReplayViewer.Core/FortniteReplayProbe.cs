using System.Globalization;
using FortniteReplayReader;
using FortniteReplayReader.Models;
using FortniteReplayReader.Models.Events;
using Unreal.Core.Models.Enums;

namespace ReplayViewer.Core;

public sealed class FortniteReplayProbe
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    private static readonly ParseMode[] Modes = [ParseMode.Full, ParseMode.Minimal];

    public TimeSpan Timeout { get; init; } = DefaultTimeout;

    /// <summary>
    /// The parser loops forever on a corrupt replay and cannot be cancelled, so the child process
    /// that owns it is ended instead. This only runs in that child.
    /// </summary>
    public Action OnStall { get; init; } = () => Environment.Exit(3);

    /// <summary>
    /// A healthy replay moves to the next data chunk every few milliseconds; two seconds without
    /// one means the parser is in a loop, not working.
    /// </summary>
    public TimeSpan StallWindow { get; init; } = TimeSpan.FromSeconds(2);

    public ParsedReplaySnapshot Read(string copiedReplayPath, CancellationToken cancellationToken)
    {
        return ReadAsync(copiedReplayPath, cancellationToken).GetAwaiter().GetResult();
    }

    public async Task<ParsedReplaySnapshot> ReadAsync(
        string copiedReplayPath,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(copiedReplayPath);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            return await Task.Run(() => Parse(copiedReplayPath), timeout.Token)
                .WaitAsync(timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Replay parse timed out after {Timeout.TotalSeconds:0}s."
            );
        }
    }

    private ParsedReplaySnapshot Parse(string path)
    {
        FortniteReplay? chosen = null;
        ReplayReader? chosenReader = null;
        Exception? failure = null;
        foreach (var mode in Modes)
        {
            try
            {
                var reader = new ReplayReader(parseMode: mode);
                var replay = Read(reader, path);
                if (HasPlayersAndEvents(replay))
                {
                    chosen = replay;
                    chosenReader = reader;
                    break;
                }

                if (chosen is null)
                {
                    chosen = replay;
                    chosenReader = reader;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A replay that is still recording ends mid-structure and throws; try the next
                // mode rather than failing the whole scrape.
                failure = ex;
            }
        }

        if (chosen is null)
        {
            throw new UnreadableReplayException(failure);
        }

        return Map(chosen, Path.GetFileName(path), chosenReader);
    }

    /// <summary>
    /// Parses on a worker thread while this one watches the reader's own chunk counter. The
    /// library reads a replay's data in chunks, so a counter that stops is a stuck parser.
    /// </summary>
    private FortniteReplay Read(ReplayReader reader, string path)
    {
        var progress = ProgressField(reader.GetType());
        if (progress is null)
        {
            return reader.ReadReplay(path);
        }

        FortniteReplay? replay = null;
        Exception? failure = null;
        var finished = 0;
        var worker = new Thread(() =>
        {
            try
            {
                replay = reader.ReadReplay(path);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                Volatile.Write(ref finished, 1);
            }
        })
        {
            IsBackground = true,
            Name = "replay parse",
        };
        worker.Start();

        var last = -1;
        var stable = System.Diagnostics.Stopwatch.StartNew();
        while (Volatile.Read(ref finished) == 0)
        {
            Thread.Sleep(100);
            if (Volatile.Read(ref finished) != 0)
            {
                break;
            }

            var chunk = progress.GetValue(reader) is int value ? value : -1;
            if (chunk != last)
            {
                last = chunk;
                stable.Restart();
            }
            else if (stable.Elapsed >= StallWindow)
            {
                OnStall();
                return replay!;
            }
        }

        worker.Join();
        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return replay!;
    }

    private static System.Reflection.FieldInfo? ProgressField(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(
                "replayDataIndex",
                System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Public
            );
            if (field is not null)
            {
                return field;
            }
        }

        return null;
    }

    private static bool HasPlayersAndEvents(FortniteReplay replay)
    {
        var players = replay.PlayerData?.Any() == true;
        var events =
            replay.Eliminations?.Count > 0 || replay.KillFeed?.Count > 0;
        return players && events;
    }

    private static ParsedReplaySnapshot Map(
        FortniteReplay replay,
        string sourceFileName,
        ReplayReader? reader
    )
    {
        var players = (replay.PlayerData ?? []).Select(MapPlayer).ToArray();
        var idByNumeric = players
            .Select((p, i) => (p, numeric: replay.PlayerData!.ElementAt(i).Id))
            .Where(x => x.numeric is not null)
            .GroupBy(x => x.numeric!.Value)
            .ToDictionary(g => g.Key, g => g.Last().p);

        var playersByNumeric = (replay.PlayerData ?? []).ToArray();
        for (var i = 0; i < players.Length; i++)
        {
            var source = playersByNumeric[i];
            var partyOwner = ResolvePartyOwner(source, playersByNumeric, replay.TeamData);
            if (partyOwner is not null)
            {
                var current = players[i];
                players[i] = current with { PartyOwnerId = partyOwner };
            }
        }

        MarkKillFeedBots(replay, players, playersByNumeric);

        // The game state names the recorder's player state; the parser's own owner flag misses it
        // whenever that reference resolves late, so resolve it from the same map the parser built.
        if (reader is not null)
        {
            players = RecorderResolver.ApplyRecorder(players, RecorderResolver.EpicIdOf(reader, replay));
        }

        var teams = (replay.TeamData ?? [])
            .Where(t => t.TeamIndex is not null)
            .Select(t => new TeamSnapshot(t.TeamIndex!.Value, t.Placement))
            .ToArray();

        var events = MapEvents(replay, idByNumeric);

        DateTimeOffset? recorded = replay.GameData?.UtcTimeStartedMatch is DateTime utc
            ? new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc))
            : replay.Info?.Timestamp is DateTime ts
                ? new DateTimeOffset(DateTime.SpecifyKind(ts, DateTimeKind.Utc))
                : null;

        OwnerStatsSnapshot? ownerStats = null;
        if (replay.Stats is not null)
        {
            ownerStats = new OwnerStatsSnapshot(
                (int)replay.Stats.Eliminations,
                replay.Stats.DamageToPlayers,
                replay.Stats.Accuracy,
                (int)replay.Stats.Assists,
                (int)replay.Stats.Revives,
                (int)replay.Stats.DamageTaken,
                Wood: null,
                Stone: null,
                Metal: null
            );
        }

        return new ParsedReplaySnapshot(
            sourceFileName,
            replay.GameData?.CurrentPlaylist,
            replay.GameData?.TeamSize,
            recorded,
            replay.GameData?.SafeZonesStartTime,
            ownerStats,
            players,
            teams,
            events,
            replay.Info?.LengthInMs is uint ms ? ms / 1000.0 : null,
            IsCreative(replay)
        );
    }

    /// <summary>
    /// Creative and UEFN matches name themselves in the header's Verse URI; the creative game
    /// mode carries no playlist or map data at all.
    /// </summary>
    private static bool IsCreative(FortniteReplay replay)
    {
        var data = replay.Header?.GameSpecificData;
        return data is not null
            && data.Any(line =>
                line.Contains("VKPlayMinimumRoot", StringComparison.OrdinalIgnoreCase)
                || line.Contains("UEFNMetaData", StringComparison.OrdinalIgnoreCase)
            );
    }

    private static PlayerSnapshot MapPlayer(PlayerData player)
    {
        var epic = string.IsNullOrWhiteSpace(player.EpicId) ? player.PlayerId : player.EpicId;
        if (string.IsNullOrWhiteSpace(epic))
        {
            epic = player.BotId;
        }

        return new PlayerSnapshot(
            string.IsNullOrWhiteSpace(epic) ? null : epic.Trim(),
            string.IsNullOrWhiteSpace(player.PlayerName) ? null : player.PlayerName.Trim(),
            string.IsNullOrWhiteSpace(player.Platform) ? null : player.Platform.Trim(),
            player.IsReplayOwner,
            player.IsBot,
            player.Disconnected == true,
            player.IsUsingAnonymousMode == true,
            player.TeamIndex,
            player.Placement,
            PartyOwnerId: null,
            player.Kills is uint k ? (int)k : null,
            player.PlayerNumber
        );
    }

    private static void MarkKillFeedBots(
        FortniteReplay replay,
        PlayerSnapshot[] players,
        PlayerData[] source
    )
    {
        var botNumeric = new HashSet<int>();
        foreach (var row in replay.KillFeed ?? [])
        {
            if (row.PlayerIsBot && row.PlayerId is int victim)
            {
                botNumeric.Add(victim);
            }

            if (row.FinisherOrDownerIsBot && row.FinisherOrDowner is int actor)
            {
                botNumeric.Add(actor);
            }
        }

        for (var i = 0; i < players.Length; i++)
        {
            if (source[i].Id is int id && botNumeric.Contains(id) && !players[i].IsBot)
            {
                players[i] = players[i] with { IsBot = true };
            }
        }
    }

    private static string? ResolvePartyOwner(
        PlayerData player,
        IReadOnlyList<PlayerData> all,
        IEnumerable<TeamData>? teams
    )
    {
        if (player.IsPartyLeader)
        {
            var id = string.IsNullOrWhiteSpace(player.EpicId) ? player.PlayerId : player.EpicId;
            if (!string.IsNullOrWhiteSpace(id))
            {
                return id.Trim();
            }
        }

        var team = teams?.FirstOrDefault(t =>
            t.TeamIndex is not null && t.TeamIndex == player.TeamIndex
        );
        if (team?.PartyOwnerId is int ownerNumeric)
        {
            var owner = all.FirstOrDefault(p => p.Id == ownerNumeric);
            if (owner is not null)
            {
                var id = string.IsNullOrWhiteSpace(owner.EpicId) ? owner.PlayerId : owner.EpicId;
                return string.IsNullOrWhiteSpace(id) ? null : id.Trim();
            }
        }

        return null;
    }

    private static List<EventSnapshot> MapEvents(
        FortniteReplay replay,
        Dictionary<int, PlayerSnapshot> idByNumeric
    )
    {
        var byEpic = PlayersByEpic(idByNumeric);
        List<EventSnapshot> mapped;
        if (replay.KillFeed is { Count: > 0 })
        {
            mapped = new List<EventSnapshot>(replay.KillFeed.Count);
            foreach (var row in replay.KillFeed)
            {
                var kind = KindFromFeed(row);
                if (kind is null)
                {
                    continue;
                }

                var target =
                    Lookup(idByNumeric, row.PlayerId) ?? LookupEpic(byEpic, row.PlayerName);
                var actor =
                    Lookup(idByNumeric, row.FinisherOrDowner)
                    ?? LookupEpic(byEpic, row.FinisherOrDownerName);
                var time = row.ReplicatedWorldTimeSecondsDouble ?? row.ReplicatedWorldTimeSeconds ?? 0;
                mapped.Add(
                    new EventSnapshot(
                        time,
                        kind.Value,
                        actor?.EpicId,
                        FirstName(actor?.DisplayName, ReadableName(row.FinisherOrDownerName)),
                        target?.EpicId,
                        FirstName(target?.DisplayName, ReadableName(row.PlayerName)),
                        Weapon: null,
                        row.Distance,
                        row.DeathCause?.ToString(CultureInfo.InvariantCulture)
                    )
                );
            }
        }
        else if (replay.Eliminations is { Count: > 0 })
        {
            mapped = new List<EventSnapshot>(replay.Eliminations.Count);
            foreach (var elim in replay.Eliminations)
            {
                var kind = elim.IsSelfElimination
                    ? EventKind.SelfElim
                    : elim.Knocked
                        ? EventKind.Knock
                        : EventKind.Finish;
                var actor = LookupEpic(byEpic, elim.Eliminator);
                var target = LookupEpic(byEpic, elim.Eliminated);
                mapped.Add(
                    new EventSnapshot(
                        ParseTime(elim.Time),
                        kind,
                        actor?.EpicId ?? elim.Eliminator,
                        FirstName(actor?.DisplayName, ReadableName(elim.EliminatorInfo?.Id)),
                        target?.EpicId ?? elim.Eliminated,
                        FirstName(target?.DisplayName, ReadableName(elim.EliminatedInfo?.Id)),
                        elim.GunType.ToString(CultureInfo.InvariantCulture),
                        elim.Distance,
                        Cause: null
                    )
                );
            }
        }
        else
        {
            return [];
        }

        return EventFixup.Clean(mapped).Select(e => ResolveNames(e, byEpic)).ToList();
    }

    private static Dictionary<string, PlayerSnapshot> PlayersByEpic(
        Dictionary<int, PlayerSnapshot> idByNumeric
    ) =>
        idByNumeric
            .Values.Where(p => !string.IsNullOrWhiteSpace(p.EpicId))
            .GroupBy(p => p.EpicId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

    private static EventSnapshot ResolveNames(
        EventSnapshot row,
        Dictionary<string, PlayerSnapshot> byEpic
    )
    {
        var actor = LookupEpic(byEpic, row.ActorId);
        var target = LookupEpic(byEpic, row.TargetId);
        var actorName = actor?.DisplayName;
        if (string.IsNullOrWhiteSpace(actorName) && row.ActorName is "Storm" or "Fall" or "Environment")
        {
            actorName = row.ActorName;
        }

        return row with
        {
            ActorName = FirstName(actorName, ReadableName(row.ActorName)),
            TargetName = FirstName(target?.DisplayName, ReadableName(row.TargetName)),
        };
    }

    private static PlayerSnapshot? LookupEpic(
        Dictionary<string, PlayerSnapshot> byEpic,
        string? epic
    ) =>
        !string.IsNullOrWhiteSpace(epic) && byEpic.TryGetValue(epic, out var player)
            ? player
            : null;

    private static string? ReadableName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || LooksLikeEpicId(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static bool LooksLikeEpicId(string value) =>
        value.Length is 32 && value.All(Uri.IsHexDigit);

    private static EventKind? KindFromFeed(KillFeedEntry row)
    {
        if (row.IsRevived)
        {
            return EventKind.Revive;
        }

        if (row.IsDowned)
        {
            return EventKind.Knock;
        }

        if (row.PlayerId is int victim && row.FinisherOrDowner is int actor && victim == actor)
        {
            return EventKind.SelfElim;
        }

        return EventKind.Finish;
    }

    private static PlayerSnapshot? Lookup(Dictionary<int, PlayerSnapshot> map, int? id) =>
        id is int key && map.TryGetValue(key, out var player) ? player : null;

    private static string? FirstName(string? preferred, string? fallback) =>
        !string.IsNullOrWhiteSpace(preferred) ? preferred : fallback;

    private static double ParseTime(string? time)
    {
        if (
            double.TryParse(
                time,
                System.Globalization.NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var seconds
            )
        )
        {
            return seconds;
        }

        return 0;
    }

}
