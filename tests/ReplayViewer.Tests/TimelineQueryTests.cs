namespace ReplayViewer.Tests;

public class TimelineQueryTests
{
    private static readonly MatchDocument Match = Build();

    [Fact]
    public void Game_is_knocks_and_finishes()
    {
        var rows = TimelineQuery.Game(Match);
        Assert.Equal(5, rows.Count);
        Assert.DoesNotContain(rows, e => e.Kind == EventKind.Revive);
        Assert.DoesNotContain(rows, e => e.Kind == EventKind.SelfElim);
    }

    [Fact]
    public void Knock_and_finish_stay_two_rows()
    {
        var rows = TimelineQuery.You(Match);
        Assert.Contains(rows, e => e.Kind == EventKind.Knock && e.TimeSeconds == 10);
        Assert.Contains(rows, e => e.Kind == EventKind.Finish && e.TimeSeconds == 12);
    }

    [Fact]
    public void You_filters_kills_knocks_deaths()
    {
        Assert.Single(TimelineQuery.YouKills(Match));
        Assert.Single(TimelineQuery.YouKnocks(Match));
        Assert.Equal(3, TimelineQuery.YouDeaths(Match).Count);
    }

    [Fact]
    public void Latest_death_carries_their_team()
    {
        var death = TimelineQuery.LatestYouDeath(Match);
        Assert.NotNull(death);
        Assert.Equal(90, death!.TimeSeconds);
        Assert.Equal(8, death.ActorTeam);
    }

    [Fact]
    public void Team_hides_only_when_hidden_player_is_the_only_side()
    {
        var hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "you" };
        var rows = TimelineQuery.Team(Match, 1, hidden);
        Assert.DoesNotContain(rows, e => e.TimeSeconds == 10);
        Assert.Contains(rows, e => e.TimeSeconds == 40);
    }

    [Fact]
    public void Hide_bots_keeps_human_side()
    {
        var hiddenBots = TimelineQuery.HideBots(Match, Match.Events);
        Assert.Contains(hiddenBots, e => e.TimeSeconds == 10);
        Assert.DoesNotContain(hiddenBots, e => e.TimeSeconds == 70);
    }

    [Fact]
    public void Only_you_keeps_your_fights()
    {
        var rows = TimelineQuery.OnlyYou(Match, Match.Events);
        Assert.All(rows, e => Assert.True(e.ActorId == "you" || e.TargetId == "you"));
    }

    private static MatchDocument Build()
    {
        var you = new RosterPlayer("you", "You", "PC", null, 1, true, false, false, false, FillStatus.None);
        var mate = new RosterPlayer("mate", "Mate", "PC", null, 0, false, false, false, false, FillStatus.None);
        var foe = new RosterPlayer("foe", "Foe", "PC", null, 1, false, false, false, false, FillStatus.None);
        var bot = new RosterPlayer("bot", "Bot", null, null, 0, false, true, false, false, FillStatus.Unknown);
        var bot2 = new RosterPlayer("bot2", "Bot2", null, null, 0, false, true, false, false, FillStatus.Unknown);

        return new MatchDocument(
            "t.replay",
            DateTimeOffset.UnixEpoch,
            "Playlist",
            2,
            DateTimeOffset.UnixEpoch,
            5,
            5,
            30,
            null,
            [
                new TeamRoster(1, 5, [you, mate]),
                new TeamRoster(8, 2, [foe]),
                new TeamRoster(9, 99, [bot, bot2]),
            ],
            [
                new MatchEvent(10, EventKind.Knock, "you", "You", "foe", "Foe", "AR", 12, null, 1, 8),
                new MatchEvent(12, EventKind.Finish, "you", "You", "foe", "Foe", "AR", 12, null, 1, 8),
                new MatchEvent(40, EventKind.Finish, "mate", "Mate", "you", "You", null, null, null, 1, 1),
                new MatchEvent(70, EventKind.Finish, "bot", "Bot", "bot2", "Bot2", null, null, null, 9, 9),
                new MatchEvent(80, EventKind.Revive, "mate", "Mate", "you", "You", null, null, null, 1, 1),
                new MatchEvent(90, EventKind.Finish, "foe", "Foe", "you", "You", "SMG", 8, null, 8, 1),
                new MatchEvent(20, EventKind.SelfElim, "you", "You", "you", "You", null, null, null, 1, 1),
            ]
        );
    }
}
