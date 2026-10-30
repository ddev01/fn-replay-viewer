namespace ReplayViewer.Core;

public static class MatchAssembler
{
    public static MatchDocument Build(ParsedReplaySnapshot snapshot, DateTimeOffset scrapedAt)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var teamPlacementByIndex = snapshot.Teams
            .GroupBy(t => t.TeamIndex)
            .ToDictionary(g => g.Key, g => g.Last().Placement);

        var grouped = snapshot.Players
            .GroupBy(p => p.TeamIndex ?? -1)
            .OrderBy(g => g.Key)
            .ToArray();

        var you = snapshot.Players.FirstOrDefault(p => p.IsReplayOwner);
        var youTeamIndex = you?.TeamIndex ?? -1;
        var youId = you?.EpicId;

        var teams = new List<TeamRoster>(grouped.Length);
        foreach (var group in grouped)
        {
            var mates = group.ToArray();
            teamPlacementByIndex.TryGetValue(group.Key, out var groupPlacement);
            if (groupPlacement is null)
            {
                var fromPlayers = mates
                    .Where(p => p.Placement is not null)
                    .Select(p => p.Placement!.Value)
                    .ToArray();
                if (fromPlayers.Length > 0)
                {
                    groupPlacement = fromPlayers.Min();
                }
            }

            var players = mates
                .Select(p =>
                    ToRosterPlayer(
                        p,
                        mates,
                        snapshot,
                        isYou: you is not null && IsSameYou(p, you, youId)
                    )
                )
                .OrderByDescending(p => p.Kills)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            teams.Add(new TeamRoster(group.Key, groupPlacement, players));
        }

        var idToTeam = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var team in teams)
        {
            foreach (var player in team.Players)
            {
                if (!string.IsNullOrWhiteSpace(player.EpicId))
                {
                    idToTeam[player.EpicId] = team.TeamIndex;
                }
            }
        }

        var events = snapshot.Events
            .Select(e => new MatchEvent(
                e.TimeSeconds,
                e.Kind,
                e.ActorId,
                e.ActorName,
                e.TargetId,
                e.TargetName,
                e.Weapon,
                e.Distance,
                e.Cause,
                LookupTeam(idToTeam, e.ActorId),
                LookupTeam(idToTeam, e.TargetId)
            ))
            .ToArray();

        int? yourPlacement = null;
        int? teamPlacement = null;
        if (you is not null)
        {
            teamPlacementByIndex.TryGetValue(youTeamIndex, out teamPlacement);
            yourPlacement = you.Placement ?? teamPlacement;
        }

        return new MatchDocument(
            snapshot.SourceFileName,
            scrapedAt,
            snapshot.Playlist,
            snapshot.TeamSize,
            snapshot.RecordedAt,
            yourPlacement,
            teamPlacement,
            snapshot.SafeZonesStartTime,
            snapshot.OwnerStats,
            teams,
            events
        );
    }

    private static bool IsSameYou(PlayerSnapshot player, PlayerSnapshot you, string? youId)
    {
        if (!string.IsNullOrWhiteSpace(youId))
        {
            return string.Equals(player.EpicId, youId, StringComparison.OrdinalIgnoreCase);
        }

        return ReferenceEquals(player, you);
    }

    private static RosterPlayer ToRosterPlayer(
        PlayerSnapshot player,
        IReadOnlyList<PlayerSnapshot> teamMates,
        ParsedReplaySnapshot snapshot,
        bool isYou
    )
    {
        var name = DisplayName(player);
        var kills = player.Kills
            ?? snapshot.Events.Count(e =>
                e.Kind == EventKind.Finish
                && !string.IsNullOrWhiteSpace(player.EpicId)
                && string.Equals(e.ActorId, player.EpicId, StringComparison.OrdinalIgnoreCase)
            );

        return new RosterPlayer(
            player.EpicId,
            name,
            PlatformMapper.ToDisplay(player.RawPlatform),
            player.SkinCid,
            kills,
            isYou,
            player.IsBot,
            player.Disconnected,
            player.Anonymous,
            ResolveFill(player, teamMates, snapshot.TeamSize),
            player.PlayerNumber
        );
    }

    private static string DisplayName(PlayerSnapshot player)
    {
        if (!string.IsNullOrWhiteSpace(player.DisplayName))
        {
            return player.DisplayName.Trim();
        }

        if (player.Anonymous && player.PlayerNumber is int n)
        {
            return "Anonymous " + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return player.Anonymous ? "Anonymous" : "Unknown";
    }

    private static FillStatus ResolveFill(
        PlayerSnapshot player,
        IReadOnlyList<PlayerSnapshot> teamMates,
        int? teamSize
    )
    {
        if (teamSize is null)
        {
            return FillStatus.Unknown;
        }

        if (string.IsNullOrWhiteSpace(player.PartyOwnerId))
        {
            return FillStatus.Unknown;
        }

        var knownParties = teamMates
            .Select(p => p.PartyOwnerId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (knownParties.Length <= 1)
        {
            return FillStatus.None;
        }

        var sizes = teamMates
            .Where(p => !string.IsNullOrWhiteSpace(p.PartyOwnerId))
            .GroupBy(p => p.PartyOwnerId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Count())
            .ToArray();
        var largest = sizes.Max();
        var partyCount = teamMates.Count(p =>
            string.Equals(p.PartyOwnerId, player.PartyOwnerId, StringComparison.OrdinalIgnoreCase)
        );
        return partyCount < largest ? FillStatus.Fill : FillStatus.None;
    }

    private static int? LookupTeam(Dictionary<string, int> idToTeam, string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return idToTeam.TryGetValue(id, out var team) ? team : null;
    }
}
