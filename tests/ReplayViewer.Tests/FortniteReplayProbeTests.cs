using System.Globalization;
using FortniteReplayReader;
using Unreal.Core.Models.Enums;
using Xunit.Abstractions;

namespace ReplayViewer.Tests;

public class FortniteReplayProbeTests(ITestOutputHelper output)
{
    [SkippableFact]
    public async Task Parses_a_local_demo_when_present()
    {
        var file = LatestFinishedReplay();
        Skip.If(file is null);
        var copy = ReplayFileAccess.CopyWithReadWriteShare(file);
        try
        {
            var probe = new FortniteReplayProbe();
            var snapshot = await probe.ReadAsync(copy, CancellationToken.None);
            Assert.NotEmpty(snapshot.Players);
        }
        finally
        {
            File.Delete(copy);
        }
    }

    [SkippableFact]
    public async Task Owner_timeline_does_not_repeat_the_same_elim()
    {
        var file = LatestFinishedReplay();
        Skip.If(file is null);
        var copy = ReplayFileAccess.CopyWithReadWriteShare(file);
        try
        {
            var snapshot = await new FortniteReplayProbe().ReadAsync(copy, CancellationToken.None);
            var you = snapshot.Players.First(p => p.IsReplayOwner);
            var rows = snapshot
                .Events.Where(e =>
                    string.Equals(e.ActorId, you.EpicId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(e.TargetId, you.EpicId, StringComparison.OrdinalIgnoreCase)
                )
                .OrderBy(e => e.TimeSeconds)
                .ToArray();
            foreach (var e in rows)
            {
                output.WriteLine(
                    "{0:0} {1} {2} -> {3}",
                    e.TimeSeconds,
                    e.Kind,
                    e.ActorName,
                    e.TargetName
                );
            }

            var repeats = rows.GroupBy(e =>
                    (
                        e.Kind,
                        Actor: e.ActorId ?? e.ActorName,
                        Target: e.TargetId ?? e.TargetName,
                        Bucket: Math.Floor(e.TimeSeconds)
                    )
                )
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToArray();
            Assert.Empty(repeats);
        }
        finally
        {
            File.Delete(copy);
        }
    }

    [SkippableFact]
    public void Dump_owner_kills_vs_feed()
    {
        var file = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FortniteGame",
            "Saved",
            "Demos",
            "UnsavedReplay-2026.09.29-16.44.49.replay"
        );
        Skip.If(!File.Exists(file));
        var copy = ReplayFileAccess.CopyWithReadWriteShare(file);
        try
        {
            var replay = new ReplayReader(parseMode: ParseMode.Normal).ReadReplay(copy);
            var owner = replay.PlayerData!.First(p => p.IsReplayOwner);
            output.WriteLine(
                "statsElims={0} owner={1} id={2} epic={3}",
                replay.Stats?.Eliminations,
                owner.PlayerName,
                owner.Id,
                owner.EpicId
            );

            var names = replay
                .PlayerData.Where(p => p.EpicId is not null)
                .GroupBy(p => p.EpicId!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last().PlayerName, StringComparer.OrdinalIgnoreCase);

            string Name(string? id) =>
                id is not null && names.TryGetValue(id, out var n) ? n ?? id : id ?? "?";

            output.WriteLine("--- elims by owner (finish) ---");
            foreach (var elim in replay.Eliminations ?? [])
            {
                if (
                    !string.Equals(elim.Eliminator, owner.EpicId, StringComparison.OrdinalIgnoreCase)
                    || elim.Knocked
                )
                {
                    continue;
                }

                output.WriteLine(
                    "t={0} self={1} gun={2} victim={3} ({4})",
                    elim.Time,
                    elim.IsSelfElimination,
                    elim.GunType,
                    Name(elim.Eliminated),
                    elim.Eliminated
                );
            }

            var victims = (replay.Eliminations ?? [])
                .Where(e =>
                    !e.Knocked
                    && string.Equals(e.Eliminator, owner.EpicId, StringComparison.OrdinalIgnoreCase)
                )
                .Select(e => e.Eliminated)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)!;

            output.WriteLine("--- victim bot flags ---");
            foreach (var id in victims)
            {
                var p = replay.PlayerData.FirstOrDefault(x =>
                    string.Equals(x.EpicId, id, StringComparison.OrdinalIgnoreCase)
                );
                output.WriteLine(
                    "{0} isBot={1} botId={2} name={3}",
                    id,
                    p?.IsBot,
                    p?.BotId,
                    p?.PlayerName
                );
            }

            foreach (var row in replay.KillFeed ?? [])
            {
                var vicEpic = row.PlayerName;
                if (vicEpic is null || !victims.Contains(vicEpic))
                {
                    continue;
                }

                output.WriteLine(
                    "{0:0.0} down={1} act={2}/{3} vic={4}/{5} cause={6}",
                    row.ReplicatedWorldTimeSecondsDouble ?? row.ReplicatedWorldTimeSeconds,
                    row.IsDowned,
                    row.FinisherOrDowner,
                    Name(row.FinisherOrDownerName),
                    row.PlayerId,
                    Name(row.PlayerName),
                    row.DeathCause
                );
            }

            var snapshot = new FortniteReplayProbe().Read(copy, CancellationToken.None);
            output.WriteLine("--- mapped owner as actor ---");
            foreach (
                var e in snapshot.Events.Where(e =>
                    string.Equals(e.ActorId, owner.EpicId, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                output.WriteLine("{0:0} {1} -> {2} cause={3}", e.TimeSeconds, e.Kind, e.TargetName, e.Cause);
            }

            var bots = snapshot
                .Players.Where(p => p.IsBot && p.EpicId is not null)
                .Select(p => p.EpicId!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var humanKills = snapshot.Events.Count(e =>
                e.Kind == EventKind.Finish
                && string.Equals(e.ActorId, owner.EpicId, StringComparison.OrdinalIgnoreCase)
                && e.TargetId is not null
                && !bots.Contains(e.TargetId)
            );
            var mappedKills = snapshot.Events.Count(e =>
                e.Kind == EventKind.Finish
                && string.Equals(e.ActorId, owner.EpicId, StringComparison.OrdinalIgnoreCase)
            );
            var elimKills = (replay.Eliminations ?? []).Count(e =>
                !e.Knocked
                && string.Equals(e.Eliminator, owner.EpicId, StringComparison.OrdinalIgnoreCase)
            );
            output.WriteLine(
                "mappedKills={0} elimKills={1} humanKills={2} stats={3}",
                mappedKills,
                elimKills,
                humanKills,
                replay.Stats?.Eliminations
            );
            Assert.Equal(elimKills, mappedKills);
        }
        finally
        {
            File.Delete(copy);
        }
    }

    [SkippableFact]
    public void Dump_owner_timeline_sources()
    {
        var demos = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FortniteGame",
            "Saved",
            "Demos"
        );
        Skip.If(!Directory.Exists(demos));
        string? file = null;
        foreach (
            var info in Directory
                .EnumerateFiles(demos, "*.replay", SearchOption.TopDirectoryOnly)
                .Select(p => new FileInfo(p))
                .OrderByDescending(i => i.LastWriteTimeUtc)
        )
        {
            if (ReplayHeader.TryInspect(info.FullName, out var header) && !header.IsLive)
            {
                file = info.FullName;
                output.WriteLine("file={0} live={1}", info.Name, header.IsLive);
                break;
            }
        }

        Skip.If(file is null);
        var replay = new ReplayReader(parseMode: ParseMode.Normal).ReadReplay(file);
        var owner = replay.PlayerData?.FirstOrDefault(p => p.IsReplayOwner);
        output.WriteLine(
            "owner={0} epic={1} id={2} kf={3} elims={4}",
            owner?.PlayerName,
            owner?.EpicId,
            owner?.Id,
            replay.KillFeed?.Count,
            replay.Eliminations?.Count
        );

        var causes = (replay.KillFeed ?? [])
            .GroupBy(r => r.DeathCause?.ToString(CultureInfo.InvariantCulture) ?? "null")
            .Select(g => $"{g.Key}:{g.Count()}");
        output.WriteLine("causes {0}", string.Join(" ", causes));

        var ownerKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (owner?.EpicId is { } epic)
        {
            ownerKeys.Add(epic);
        }

        if (owner?.PlayerId is { } pid)
        {
            ownerKeys.Add(pid);
        }

        if (owner?.Id is int nid)
        {
            ownerKeys.Add(nid.ToString(CultureInfo.InvariantCulture));
        }

        output.WriteLine("--- killfeed owner ---");
        foreach (var row in replay.KillFeed ?? [])
        {
            var involves =
                (row.PlayerId is int v && v == owner?.Id)
                || (row.FinisherOrDowner is int a && a == owner?.Id)
                || ownerKeys.Contains(row.PlayerName ?? "")
                || ownerKeys.Contains(row.FinisherOrDownerName ?? "");
            if (!involves)
            {
                continue;
            }

            var tags = row.DeathTags is null ? "" : string.Join(",", row.DeathTags.Take(6));
            output.WriteLine(
                "{0:0.0} down={1} rev={2} vic={3}/{4} act={5}/{6} cause={7} dist={8} tags={9}",
                row.ReplicatedWorldTimeSecondsDouble ?? row.ReplicatedWorldTimeSeconds,
                row.IsDowned,
                row.IsRevived,
                row.PlayerId,
                row.PlayerName,
                row.FinisherOrDowner,
                row.FinisherOrDownerName,
                row.DeathCause,
                row.Distance,
                tags
            );
        }

        output.WriteLine("--- elims owner ---");
        foreach (var elim in replay.Eliminations ?? [])
        {
            var involves =
                ownerKeys.Contains(elim.Eliminated ?? "")
                || ownerKeys.Contains(elim.Eliminator ?? "")
                || ownerKeys.Contains(elim.EliminatedInfo?.Id ?? "")
                || ownerKeys.Contains(elim.EliminatorInfo?.Id ?? "");
            if (!involves)
            {
                continue;
            }

            output.WriteLine(
                "{0} knocked={1} self={2} gun={3} dist={4} actor={5} victim={6}",
                elim.Time,
                elim.Knocked,
                elim.IsSelfElimination,
                elim.GunType,
                elim.Distance,
                elim.Eliminator,
                elim.Eliminated
            );
        }
    }

    [SkippableFact]
    public void Dump_killfeed_missing_actors()
    {
        var file = NewestReplay();
        Skip.If(file is null);
        var replay = new ReplayReader(parseMode: ParseMode.Normal).ReadReplay(file);
        output.WriteLine("killfeed={0} elims={1}", replay.KillFeed?.Count, replay.Eliminations?.Count);
        var sample = replay.KillFeed?.FirstOrDefault();
        if (sample is not null)
        {
            foreach (var prop in sample.GetType().GetProperties())
            {
                output.WriteLine("kf.{0}={1}", prop.Name, prop.GetValue(sample));
            }
        }

        var missing = 0;
        foreach (var row in replay.KillFeed ?? [])
        {
            if (row.IsRevived || row.IsDowned)
            {
                continue;
            }

            if (row.FinisherOrDowner is null or 0 || string.IsNullOrWhiteSpace(row.FinisherOrDownerName))
            {
                output.WriteLine(
                    "t={0} down={1} victim={2}/{3} finisher={4}/{5} cause={6} dist={7}",
                    row.ReplicatedWorldTimeSecondsDouble ?? row.ReplicatedWorldTimeSeconds,
                    row.IsDowned,
                    row.PlayerId,
                    row.PlayerName,
                    row.FinisherOrDowner,
                    row.FinisherOrDownerName,
                    row.DeathCause,
                    row.Distance
                );
                if (++missing >= 12)
                {
                    break;
                }
            }
        }

        foreach (var elim in (replay.Eliminations ?? []).Take(5))
        {
            output.WriteLine(
                "elim t={0} knocked={1} self={2} elimator={3}/{4} victim={5}/{6}",
                elim.Time,
                elim.Knocked,
                elim.IsSelfElimination,
                elim.Eliminator,
                elim.EliminatorInfo?.Id,
                elim.Eliminated,
                elim.EliminatedInfo?.Id
            );
        }
    }

    [SkippableFact]
    public void Dump_cosmetic_fields()
    {
        var file = NewestReplay();
        Skip.If(file is null);
        var replay = new ReplayReader(parseMode: ParseMode.Full).ReadReplay(file);
        var n = 0;
        foreach (var p in replay.PlayerData ?? [])
        {
            if (p.IsBot) continue;
            var c = p.Cosmetics;
            if (c is null)
            {
                output.WriteLine("{0} cosmetics=null", p.PlayerName);
            }
            else
            {
                foreach (var prop in c.GetType().GetProperties())
                {
                    var val = prop.GetValue(c);
                    if (val is null || val is string s && string.IsNullOrWhiteSpace(s))
                    {
                        continue;
                    }

                    output.WriteLine("{0} {1}={2}", p.PlayerName, prop.Name, val);
                }
            }

            if (++n >= 6)
            {
                break;
            }
        }
    }

    private static string? LatestFinishedReplay()
    {
        var demos = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FortniteGame",
            "Saved",
            "Demos"
        );
        if (!Directory.Exists(demos))
        {
            return null;
        }

        foreach (
            var info in Directory
                .EnumerateFiles(demos, "*.replay", SearchOption.TopDirectoryOnly)
                .Select(p => new FileInfo(p))
                .Where(i => i.Exists && i.Length > 1024)
                .OrderByDescending(i => i.LastWriteTimeUtc)
        )
        {
            if (ReplayHeader.TryInspect(info.FullName, out var header) && !header.IsLive)
            {
                return info.FullName;
            }
        }

        return null;
    }

    private static string? NewestReplay()
    {
        var demos = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FortniteGame",
            "Saved",
            "Demos"
        );
        if (!Directory.Exists(demos))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(demos, "*.replay", SearchOption.TopDirectoryOnly)
            .Select(p => new FileInfo(p))
            .Where(i => i.Exists && i.Length > 1024)
            .OrderByDescending(i => i.LastWriteTimeUtc)
            .Select(i => i.FullName)
            .FirstOrDefault();
    }
}
