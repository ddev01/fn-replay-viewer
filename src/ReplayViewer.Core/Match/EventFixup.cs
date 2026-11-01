namespace ReplayViewer.Core;

public static class EventFixup
{
    private const double DuplicateWindowSeconds = 1.5;

    public static IReadOnlyList<EventSnapshot> Clean(IReadOnlyList<EventSnapshot> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        var ordered = events.OrderBy(e => e.TimeSeconds).ToList();
        var deduped = new List<EventSnapshot>(ordered.Count);
        foreach (var row in ordered)
        {
            if (IsNearDuplicate(deduped, row))
            {
                continue;
            }

            deduped.Add(row);
        }

        var lastKnock = new Dictionary<string, EventSnapshot>(StringComparer.OrdinalIgnoreCase);
        var finishedThisLife = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var result = new List<EventSnapshot>(deduped.Count);

        foreach (var row in deduped)
        {
            var victim = VictimKey(row);
            if (row.Kind == EventKind.Knock)
            {
                if (victim is not null)
                {
                    lastKnock[victim] = row;
                    finishedThisLife[victim] = false;
                }

                result.Add(row);
                continue;
            }

            if (row.Kind == EventKind.Revive)
            {
                if (victim is not null)
                {
                    lastKnock.Remove(victim);
                    finishedThisLife[victim] = false;
                }

                result.Add(row);
                continue;
            }

            if (row.Kind is not (EventKind.Finish or EventKind.SelfElim))
            {
                result.Add(row);
                continue;
            }

            var hasPlayer = HasPlayerActor(row);
            if (victim is not null && finishedThisLife.GetValueOrDefault(victim) && !hasPlayer)
            {
                continue;
            }

            EventSnapshot next = row;
            if (!hasPlayer && IsEnvironmentalCause(row.Cause))
            {
                next = row with { ActorId = null, ActorName = EnvironmentLabel(row) };
            }
            else if (
                !hasPlayer
                && victim is not null
                && lastKnock.TryGetValue(victim, out var knock)
                && !finishedThisLife.GetValueOrDefault(victim)
            )
            {
                next = ApplyActor(row, new ActorRef(knock.ActorId, knock.ActorName));
            }
            else if (!hasPlayer)
            {
                next = row with { ActorId = null, ActorName = EnvironmentLabel(row) };
            }

            result.Add(next);
            if (victim is not null)
            {
                finishedThisLife[victim] = true;
            }
        }

        return result;
    }

    public static bool IsEnvironmentalCause(string? cause) => cause is "17" or "1";

    public static string EnvironmentLabel(EventSnapshot row) =>
        row.Cause switch
        {
            "17" => "Storm",
            "1" => "Fall",
            _ => "Environment",
        };

    private static bool HasPlayerActor(EventSnapshot row) =>
        !string.IsNullOrWhiteSpace(row.ActorId)
        || (
            !string.IsNullOrWhiteSpace(row.ActorName)
            && !IsEnvironmentName(row.ActorName)
        );

    private static bool IsEnvironmentName(string name) =>
        name is "Storm" or "Fall" or "Environment";

    private static bool IsNearDuplicate(List<EventSnapshot> kept, EventSnapshot row)
    {
        for (var i = kept.Count - 1; i >= 0; i--)
        {
            var prev = kept[i];
            if (row.TimeSeconds - prev.TimeSeconds > DuplicateWindowSeconds)
            {
                break;
            }

            if (
                prev.Kind == row.Kind
                && SamePerson(prev.ActorId, prev.ActorName, row.ActorId, row.ActorName)
                && SamePerson(prev.TargetId, prev.TargetName, row.TargetId, row.TargetName)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static EventSnapshot ApplyActor(EventSnapshot row, ActorRef actor) =>
        row with
        {
            Kind = EventKind.Finish,
            ActorId = actor.Id,
            ActorName = actor.Name,
        };

    private static string? VictimKey(EventSnapshot row) =>
        !string.IsNullOrWhiteSpace(row.TargetId) ? row.TargetId : row.TargetName;

    private static bool SamePerson(string? idA, string? nameA, string? idB, string? nameB)
    {
        if (!string.IsNullOrWhiteSpace(idA) && !string.IsNullOrWhiteSpace(idB))
        {
            return string.Equals(idA, idB, StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(nameA) && !string.IsNullOrWhiteSpace(nameB))
        {
            return string.Equals(nameA, nameB, StringComparison.OrdinalIgnoreCase);
        }

        return string.IsNullOrWhiteSpace(idA)
            && string.IsNullOrWhiteSpace(idB)
            && string.IsNullOrWhiteSpace(nameA)
            && string.IsNullOrWhiteSpace(nameB);
    }
}

public readonly record struct ActorRef(string? Id, string? Name);
