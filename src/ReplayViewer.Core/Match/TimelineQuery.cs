namespace ReplayViewer.Core;

public static class TimelineQuery
{
    public static string? YouId(MatchDocument match) =>
        match.Teams.SelectMany(t => t.Players).FirstOrDefault(p => p.IsYou)?.EpicId;

    public static IReadOnlyList<MatchEvent> Game(MatchDocument match) =>
        match.Events.Where(e => e.Kind is EventKind.Knock or EventKind.Finish).ToArray();

    public static IReadOnlyList<MatchEvent> Involving(MatchDocument match, string? playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return [];
        }

        return match.Events.Where(e => IsSide(e, playerId)).ToArray();
    }

    public static IReadOnlyList<MatchEvent> You(MatchDocument match) => Involving(match, YouId(match));

    public static IReadOnlyList<MatchEvent> YouKills(MatchDocument match)
    {
        var you = YouId(match);
        return match
            .Events.Where(e =>
                e.Kind == EventKind.Finish && IdsEqual(e.ActorId, you)
            )
            .ToArray();
    }

    public static IReadOnlyList<MatchEvent> YouKnocks(MatchDocument match)
    {
        var you = YouId(match);
        return match
            .Events.Where(e =>
                e.Kind == EventKind.Knock && IdsEqual(e.ActorId, you)
            )
            .ToArray();
    }

    public static IReadOnlyList<MatchEvent> YouDeaths(MatchDocument match)
    {
        var you = YouId(match);
        return match
            .Events.Where(e =>
                e.Kind is EventKind.Finish or EventKind.SelfElim && IdsEqual(e.TargetId, you)
            )
            .ToArray();
    }

    public static MatchEvent? LatestYouDeath(MatchDocument match) =>
        YouDeaths(match).OrderByDescending(e => e.TimeSeconds).FirstOrDefault();

    public static IReadOnlyList<MatchEvent> Player(MatchDocument match, string? playerId) =>
        Involving(match, playerId);

    public static IReadOnlyList<MatchEvent> Team(
        MatchDocument match,
        int teamIndex,
        IReadOnlySet<string>? hiddenPlayerIds = null
    )
    {
        var hidden = hiddenPlayerIds is null
            ? null
            : new HashSet<string>(hiddenPlayerIds, StringComparer.OrdinalIgnoreCase);

        var result = new List<MatchEvent>();
        foreach (var e in match.Events)
        {
            var actorOnTeam = e.ActorTeam == teamIndex;
            var targetOnTeam = e.TargetTeam == teamIndex;
            if (!actorOnTeam && !targetOnTeam)
            {
                continue;
            }

            if (hidden is { Count: > 0 })
            {
                var actorVisible = actorOnTeam && !IsHidden(hidden, e.ActorId);
                var targetVisible = targetOnTeam && !IsHidden(hidden, e.TargetId);
                if (!actorVisible && !targetVisible)
                {
                    continue;
                }
            }

            result.Add(e);
        }

        return result;
    }

    public static IReadOnlyList<MatchEvent> HideBots(
        MatchDocument match,
        IEnumerable<MatchEvent> events
    )
    {
        return events.Where(e => IsHuman(match, e.ActorId) || IsHuman(match, e.TargetId)).ToArray();
    }

    public static IReadOnlyList<MatchEvent> OnlyYou(
        MatchDocument match,
        IEnumerable<MatchEvent> events
    )
    {
        var you = YouId(match);
        return events.Where(e => IsSide(e, you)).ToArray();
    }

    private static bool IsHuman(MatchDocument match, string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        foreach (var player in match.Teams.SelectMany(t => t.Players))
        {
            if (IdsEqual(player.EpicId, id))
            {
                return !player.IsBot;
            }
        }

        return false;
    }

    private static bool IsSide(MatchEvent e, string? playerId) =>
        IdsEqual(e.ActorId, playerId) || IdsEqual(e.TargetId, playerId);

    private static bool IsHidden(HashSet<string> hidden, string? id) =>
        !string.IsNullOrWhiteSpace(id) && hidden.Contains(id);

    private static bool IdsEqual(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a)
        && !string.IsNullOrWhiteSpace(b)
        && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
