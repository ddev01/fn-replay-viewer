namespace ReplayViewer.Core;

/// <summary>
/// Fills in the replay owner when the file does not identify it. FortniteReplayReader only marks
/// the owner when the recorder's player state is mapped early enough, which fails in some
/// replays (Creative ones especially), so the id the app has already learned is used instead.
/// </summary>
public static class OwnerResolver
{
    public static MatchDocument Apply(MatchDocument match, IReadOnlySet<string> ownerIds)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(ownerIds);

        if (match.Teams.Any(team => team.Players.Any(player => player.IsYou)))
        {
            return match;
        }

        var you = match
            .Teams.SelectMany(team => team.Players)
            .FirstOrDefault(player => player.EpicId is not null && ownerIds.Contains(player.EpicId));
        if (you is null)
        {
            return match;
        }

        var team = match.Teams.FirstOrDefault(t => t.Players.Any(p => IsSamePlayer(p, you)));
        var teams = match
            .Teams.Select(t =>
                t == team
                    ? t with
                    {
                        Players = t.Players.Select(p => IsSamePlayer(p, you) ? p with { IsYou = true } : p)
                            .ToArray(),
                    }
                    : t
            )
            .ToArray();

        return match with
        {
            Teams = teams,
            YourPlacement = you.Placement ?? team?.Placement ?? match.YourPlacement,
        };
    }

    private static bool IsSamePlayer(RosterPlayer a, RosterPlayer b) =>
        a.EpicId is not null
        && b.EpicId is not null
        && string.Equals(a.EpicId, b.EpicId, StringComparison.OrdinalIgnoreCase);
}
