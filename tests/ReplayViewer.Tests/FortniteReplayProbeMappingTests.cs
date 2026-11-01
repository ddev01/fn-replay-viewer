using System.Reflection;
using FortniteReplayReader.Models;
using FortniteReplayReader.Models.Events;
using FortniteReplayReader.Models.NetFieldExports;
using Unreal.Core.Models;

namespace ReplayViewer.Tests;

/// <summary>
/// Replays cannot be built through the public reader API, and FortniteReplayReader models have
/// internal setters, so these tests populate them by reflection and call the private mapper.
/// </summary>
public class FortniteReplayProbeMappingTests
{
    [Fact]
    public void Elimination_fallback_maps_kinds_times_and_weapons()
    {
        var hexId = new string('A', 32);
        var replay = Replay(
            [
                Player(1, epic: "epic-a", name: "Alice"),
                Player(2, epic: "epic-b", name: "Bob"),
            ],
            eliminations:
            [
                Elim("epic-a", "epic-b", "12.5", knocked: true, gun: 3),
                Elim("epic-a", "epic-b", "20"),
                Elim("epic-a", "epic-a", "30"),
                Elim("epic-a", "epic-b", "not-a-number", gun: 5),
                Elim(hexId, "Unknown Target", "50"),
            ]
        );

        var events = Map(replay).Events;

        Assert.Equal(
            [EventKind.Finish, EventKind.Knock, EventKind.Finish, EventKind.SelfElim, EventKind.Finish],
            events.Select(e => e.Kind)
        );
        Assert.Equal([0, 12.5, 20, 30, 50], events.Select(e => e.TimeSeconds));
        Assert.Equal("5", events[0].Weapon);
        Assert.Equal("epic-a", events[1].ActorId);
        Assert.Equal("Alice", events[1].ActorName);
        Assert.Equal("epic-b", events[1].TargetId);
        Assert.Equal("Bob", events[1].TargetName);
        Assert.Equal("3", events[1].Weapon);
        Assert.Equal("epic-a", events[3].TargetId);
        Assert.Equal("epic-a", events[3].ActorId);
        Assert.Equal(hexId, events[4].ActorId);
        Assert.Null(events[4].ActorName);
        Assert.Equal("Unknown Target", events[4].TargetId);
        Assert.Equal("Unknown Target", events[4].TargetName);
    }

    [Fact]
    public void Kill_feed_maps_every_kind_and_prefers_the_double_time()
    {
        var replay = Replay(
            [
                Player(1, epic: "epic-a", name: "A"),
                Player(2, epic: "epic-b", name: "B"),
                Player(3, epic: "epic-c", name: "C"),
            ],
            killFeed:
            [
                new KillFeedEntry
                {
                    PlayerId = 2,
                    FinisherOrDowner = 1,
                    IsDowned = true,
                    ReplicatedWorldTimeSecondsDouble = 10,
                },
                new KillFeedEntry
                {
                    PlayerId = 2,
                    FinisherOrDowner = 1,
                    ReplicatedWorldTimeSecondsDouble = 20,
                },
                new KillFeedEntry
                {
                    PlayerId = 3,
                    FinisherOrDowner = 3,
                    ReplicatedWorldTimeSeconds = 30,
                },
                new KillFeedEntry
                {
                    PlayerId = 3,
                    FinisherOrDowner = 1,
                    IsRevived = true,
                    ReplicatedWorldTimeSeconds = 40,
                },
                new KillFeedEntry
                {
                    PlayerId = 2,
                    FinisherOrDowner = 1,
                    ReplicatedWorldTimeSeconds = 99,
                    ReplicatedWorldTimeSecondsDouble = 0.5,
                    DeathCause = 7,
                },
                new KillFeedEntry { PlayerId = 1, FinisherOrDowner = 2 },
            ]
        );

        var events = Map(replay).Events;

        Assert.Equal(
            [EventKind.Finish, EventKind.Finish, EventKind.Knock, EventKind.Finish, EventKind.SelfElim, EventKind.Revive],
            events.Select(e => e.Kind)
        );
        Assert.Equal([0, 0.5, 10, 20, 30, 40], events.Select(e => e.TimeSeconds));
        Assert.Equal("7", events[1].Cause);
        Assert.Equal("epic-b", events[0].ActorId);
        Assert.Equal("epic-a", events[0].TargetId);
    }

    [Fact]
    public void Kill_feed_bot_flags_override_player_flags()
    {
        var replay = Replay(
            [Player(1, epic: "epic-a", name: "A"), Player(2, epic: "epic-b", name: "B")],
            killFeed:
            [
                new KillFeedEntry
                {
                    PlayerId = 1,
                    PlayerIsBot = true,
                    FinisherOrDowner = 2,
                    FinisherOrDownerIsBot = true,
                    ReplicatedWorldTimeSecondsDouble = 1,
                },
            ]
        );

        var players = Map(replay).Players;

        Assert.All(players, p => Assert.True(p.IsBot));
    }

    [Fact]
    public void Party_owner_comes_from_the_leader_or_the_team()
    {
        var replay = Replay(
            [
                Player(1, epic: " leader ", team: 1, leader: true),
                Player(2, epic: "mate", team: 1),
                Player(3, epic: "solo", team: 2),
                Player(4, platformId: "platform-4", team: 3),
                Player(5, bot: true, botId: "bot-5", team: 4, leader: true),
            ],
            teams:
            [
                new TeamData { TeamIndex = 1, PartyOwnerId = 1 },
                new TeamData { TeamIndex = 2, PartyOwnerId = 99 },
                new TeamData { TeamIndex = 3, PartyOwnerId = 4 },
            ]
        );

        var owners = Map(replay).Players.ToDictionary(p => p.EpicId!);

        Assert.Equal("leader", owners["leader"].PartyOwnerId);
        Assert.Equal("leader", owners["mate"].PartyOwnerId);
        Assert.Null(owners["solo"].PartyOwnerId);
        Assert.Equal("platform-4", owners["platform-4"].PartyOwnerId);
        Assert.Equal("bot-5", owners["bot-5"].PartyOwnerId);
    }

    [Fact]
    public void Player_ids_fall_back_through_epic_platform_and_bot_ids()
    {
        var replay = Replay(
            [
                Player(1, platformId: "platform-1"),
                Player(2, botId: "bot-2"),
                Player(
                    3,
                    epic: "  spaced  ",
                    name: "  Name  ",
                    platform: "  WIN  ",
                    you: true,
                    anonymous: true,
                    team: 1,
                    placement: 3,
                    kills: 4,
                    number: 42
                ),
                Player(4, epic: "   ", name: " ", platform: " "),
                Player(5, bot: true, botId: "bot-5"),
            ]
        );

        var players = Map(replay).Players;

        Assert.Equal("platform-1", players[0].EpicId);
        Assert.Equal("bot-2", players[1].EpicId);
        Assert.Equal("spaced", players[2].EpicId);
        Assert.Equal("Name", players[2].DisplayName);
        Assert.Equal("WIN", players[2].RawPlatform);
        Assert.True(players[2].IsReplayOwner);
        Assert.True(players[2].Anonymous);
        Assert.Equal(1, players[2].TeamIndex);
        Assert.Equal(3, players[2].Placement);
        Assert.Equal(4, players[2].Kills);
        Assert.Equal(42, players[2].PlayerNumber);
        Assert.Null(players[3].EpicId);
        Assert.Null(players[3].DisplayName);
        Assert.Null(players[3].RawPlatform);
        Assert.Equal("bot-5", players[4].EpicId);
    }

    [Fact]
    public void A_replay_without_events_has_an_empty_timeline()
    {
        var replay = Replay([Player(1, epic: "epic-a", name: "A")]);

        Assert.Empty(Map(replay).Events);
    }

    private static ParsedReplaySnapshot Map(FortniteReplay replay)
    {
        var method =
            typeof(FortniteReplayProbe).GetMethod(
                "Map",
                BindingFlags.NonPublic | BindingFlags.Static
            ) ?? throw new InvalidOperationException("FortniteReplayProbe.Map was renamed.");
        return (ParsedReplaySnapshot)method.Invoke(null, [replay, "test.replay"])!;
    }

    private static FortniteReplay Replay(
        PlayerData[] players,
        TeamData[]? teams = null,
        KillFeedEntry[]? killFeed = null,
        PlayerElimination[]? eliminations = null
    )
    {
        var replay = new FortniteReplay();
        SetInternal(replay, "PlayerData", players);
        SetInternal(replay, "TeamData", teams ?? []);
        SetInternal(replay, "KillFeed", killFeed ?? []);
        SetInternal(replay, "Eliminations", eliminations ?? []);
        return replay;
    }

    private static PlayerData Player(
        int id,
        string? epic = null,
        string? name = null,
        string? platform = null,
        bool you = false,
        bool bot = false,
        bool anonymous = false,
        int? team = null,
        int? placement = null,
        bool leader = false,
        uint? kills = null,
        int? number = null,
        string? botId = null,
        string? platformId = null
    )
    {
        var player = new PlayerData(new FortPlayerState())
        {
            Id = id,
            EpicId = epic,
            PlayerName = name,
            IsReplayOwner = you,
            IsBot = bot,
            IsUsingAnonymousMode = anonymous,
            TeamIndex = team,
            Placement = placement,
            IsPartyLeader = leader,
            Kills = kills,
            PlayerNumber = number,
            BotId = botId,
            PlatformUniqueNetId = platformId,
        };
        if (platform is not null)
        {
            player.Platform = platform;
        }

        return player;
    }

    private static PlayerElimination Elim(
        string eliminator,
        string eliminated,
        string time,
        bool knocked = false,
        byte gun = 0
    )
    {
        var elim = new PlayerElimination();
        SetInternal(elim, "EliminatorInfo", Info(eliminator));
        SetInternal(elim, "EliminatedInfo", Info(eliminated));
        SetInternal(elim, "Time", time);
        SetInternal(elim, "Knocked", knocked);
        SetInternal(elim, "GunType", gun);
        return elim;
    }

    private static PlayerEliminationInfo Info(string id)
    {
        var info = new PlayerEliminationInfo();
        SetInternal(info, "Id", id);
        SetInternal(info, "Location", new FVector(0, 0, 0));
        return info;
    }

    private static void SetInternal(object target, string property, object? value)
    {
        var info =
            target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                $"{target.GetType().Name}.{property} was renamed."
            );
        info.SetValue(target, value);
    }
}
