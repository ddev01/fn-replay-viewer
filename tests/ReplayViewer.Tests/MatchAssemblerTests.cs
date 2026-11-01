namespace ReplayViewer.Tests;

public class MatchAssemblerTests
{
    [Fact]
    public void Shared_party_means_nobody_is_a_fill()
    {
        var match = MatchAssembler.Build(
            Snapshot(
                teamSize: 3,
                Players(
                    P("a", team: 1, party: "a", you: true),
                    P("b", team: 1, party: "a"),
                    P("c", team: 1, party: "a")
                )
            ),
            DateTimeOffset.UnixEpoch
        );

        Assert.All(match.Teams.Single().Players, p => Assert.Equal(FillStatus.None, p.Fill));
    }

    [Fact]
    public void Smaller_party_is_fill()
    {
        var match = MatchAssembler.Build(
            Snapshot(
                teamSize: 3,
                Players(
                    P("a", team: 1, party: "a", you: true),
                    P("b", team: 1, party: "a"),
                    P("c", team: 1, party: "c")
                )
            ),
            DateTimeOffset.UnixEpoch
        );

        var byId = match.Teams.Single().Players.ToDictionary(p => p.EpicId!);
        Assert.Equal(FillStatus.Fill, byId["c"].Fill);
        Assert.Equal(FillStatus.None, byId["a"].Fill);
        Assert.Equal(FillStatus.None, byId["b"].Fill);
    }

    [Fact]
    public void Everyone_as_their_own_party_is_not_fill()
    {
        var match = MatchAssembler.Build(
            Snapshot(
                teamSize: 3,
                Players(
                    P("a", team: 1, party: "a"),
                    P("b", team: 1, party: "b"),
                    P("c", team: 1, party: "c")
                )
            ),
            DateTimeOffset.UnixEpoch
        );

        Assert.All(match.Teams.Single().Players, p => Assert.Equal(FillStatus.None, p.Fill));
    }

    [Fact]
    public void Missing_party_id_is_unknown()
    {
        var match = MatchAssembler.Build(
            Snapshot(teamSize: 2, Players(P("a", team: 1, party: null, you: true))),
            DateTimeOffset.UnixEpoch
        );

        Assert.Equal(FillStatus.Unknown, match.Teams.Single().Players.Single().Fill);
    }

    [Fact]
    public void Missing_team_size_is_unknown()
    {
        var match = MatchAssembler.Build(
            Snapshot(teamSize: null, Players(P("a", team: 1, party: "a", you: true))),
            DateTimeOffset.UnixEpoch
        );

        Assert.Equal(FillStatus.Unknown, match.Teams.Single().Players.Single().Fill);
    }

    [Fact]
    public void First_replay_owner_is_you()
    {
        var match = MatchAssembler.Build(
            Snapshot(
                teamSize: 2,
                Players(
                    P("a", team: 1, party: "a", you: true, placement: 4),
                    P("b", team: 1, party: "a", you: true, placement: 9)
                ),
                teams: [new TeamSnapshot(1, 4)]
            ),
            DateTimeOffset.UnixEpoch
        );

        Assert.Equal("a", match.Teams.Single().Players.Single(p => p.IsYou).EpicId);
        Assert.Equal(1, match.Teams.Single().Players.Count(p => p.IsYou));
        Assert.Equal(4, match.YourPlacement);
        Assert.Equal(4, match.TeamPlacement);
    }

    [Fact]
    public void Your_placement_falls_back_to_team_snapshot()
    {
        var match = MatchAssembler.Build(
            Snapshot(
                teamSize: 2,
                Players(P("a", team: 7, party: "a", you: true, placement: null)),
                teams: [new TeamSnapshot(7, 12)]
            ),
            DateTimeOffset.UnixEpoch
        );

        Assert.Equal(12, match.YourPlacement);
        Assert.Equal(12, match.TeamPlacement);
    }

    [Fact]
    public void Null_team_index_goes_to_bucket_minus_one()
    {
        var match = MatchAssembler.Build(
            Snapshot(teamSize: 2, Players(P("a", team: null, party: "a"))),
            DateTimeOffset.UnixEpoch
        );

        Assert.Equal(-1, match.Teams.Single().TeamIndex);
    }

    [Fact]
    public void Platform_maps_to_display_labels()
    {
        var match = MatchAssembler.Build(
            Snapshot(teamSize: 1, Players(P("a", team: 1, party: "a", platform: "WIN"))),
            DateTimeOffset.UnixEpoch
        );

        Assert.Equal("PC", match.Teams.Single().Players.Single().Platform);
    }

    [Fact]
    public void Players_sort_by_kills_inside_a_team()
    {
        var match = MatchAssembler.Build(
            Snapshot(
                teamSize: 2,
                Players(
                    P("low", team: 1, party: "a", kills: 1),
                    P("high", team: 1, party: "a", kills: 9)
                )
            ),
            DateTimeOffset.UnixEpoch
        );

        Assert.Equal(["HIGH", "LOW"], match.Teams.Single().Players.Select(p => p.Name).ToArray());
    }

    [Fact]
    public void Kills_count_finishes_when_unset()
    {
        var snapshot = new ParsedReplaySnapshot(
            "x.replay",
            "Playlist",
            2,
            null,
            null,
            null,
            Players(P("a", team: 1, party: "a", kills: null)),
            [],
            [
                new EventSnapshot(1, EventKind.Finish, "a", "A", "z", "Z", null, null, null),
                new EventSnapshot(2, EventKind.Knock, "a", "A", "z", "Z", null, null, null),
            ]
        );
        var match = MatchAssembler.Build(snapshot, DateTimeOffset.UnixEpoch);
        Assert.Equal(1, match.Teams.Single().Players.Single().Kills);
        Assert.Equal(1, match.Events[0].ActorTeam);
    }

    [Fact]
    public void Anonymous_players_get_placeholder_names()
    {
        var match = MatchAssembler.Build(
            Snapshot(
                1,
                Players(
                    AnonymousPlayer("a", number: 7),
                    AnonymousPlayer("b", number: null),
                    NamedPlayer("c")
                )
            ),
            DateTimeOffset.UnixEpoch
        );

        var names = match.Teams.Single().Players.ToDictionary(p => p.EpicId!);
        Assert.Equal("Anonymous 7", names["a"].Name);
        Assert.Equal("Anonymous", names["b"].Name);
        Assert.Equal("Unknown", names["c"].Name);
    }

    [Fact]
    public void Team_placement_falls_back_to_the_best_player_placement()
    {
        var match = MatchAssembler.Build(
            Snapshot(
                teamSize: 2,
                Players(
                    P("a", team: 1, party: "a", placement: 5),
                    P("b", team: 1, party: "a", placement: 9)
                )
            ),
            DateTimeOffset.UnixEpoch
        );

        Assert.Equal(5, match.Teams.Single().Placement);
    }

    [Fact]
    public void Event_ids_without_a_roster_entry_have_no_team()
    {
        var snapshot = new ParsedReplaySnapshot(
            "x.replay",
            "Playlist",
            1,
            null,
            null,
            null,
            Players(P("a", team: 1, party: "a")),
            [],
            [
                new EventSnapshot(1, EventKind.Knock, "ghost", "Ghost", "a", "A", null, null, null),
                new EventSnapshot(2, EventKind.Knock, null, null, "a", "A", null, null, null),
            ]
        );

        var match = MatchAssembler.Build(snapshot, DateTimeOffset.UnixEpoch);

        Assert.Null(match.Events[0].ActorTeam);
        Assert.Equal(1, match.Events[0].TargetTeam);
        Assert.Null(match.Events[1].ActorTeam);
    }

    [Fact]
    public void Owner_without_an_epic_id_is_matched_by_instance()
    {
        var you = new PlayerSnapshot(null, null, "WIN", true, false, false, true, 1, null, "owner", 0, 7);
        var mate = new PlayerSnapshot(null, null, "WIN", false, false, false, true, 1, null, "mate", 0, 8);

        var match = MatchAssembler.Build(
            Snapshot(2, Players(you, mate)),
            DateTimeOffset.UnixEpoch
        );

        var owner = Assert.Single(match.Teams.Single().Players, p => p.IsYou);
        Assert.Equal("Anonymous 7", owner.Name);
    }

    [Fact]
    public void Teams_are_ordered_by_team_index()
    {
        var match = MatchAssembler.Build(
            Snapshot(
                1,
                Players(P("first", team: 2, party: "a"), P("second", team: 1, party: "b"))
            ),
            DateTimeOffset.UnixEpoch
        );

        Assert.Equal([1, 2], match.Teams.Select(t => t.TeamIndex).ToArray());
    }

    [Fact]
    public void Display_names_are_trimmed()
    {
        var player = new PlayerSnapshot(
            "a",
            "  Spaced Name  ",
            "WIN",
            false,
            false,
            false,
            false,
            1,
            null,
            "a",
            0
        );

        var match = MatchAssembler.Build(Snapshot(1, Players(player)), DateTimeOffset.UnixEpoch);

        Assert.Equal("Spaced Name", match.Teams.Single().Players.Single().Name);
    }

    private static PlayerSnapshot AnonymousPlayer(string id, int? number, bool you = false) =>
        new(id, null, "WIN", you, false, false, true, 1, null, id, 0, number);

    private static PlayerSnapshot NamedPlayer(string id) =>
        new(id, null, "WIN", false, false, false, false, 1, null, id, 0);

    private static ParsedReplaySnapshot Snapshot(
        int? teamSize,
        IReadOnlyList<PlayerSnapshot> players,
        IReadOnlyList<TeamSnapshot>? teams = null
    ) =>
        new(
            "test.replay",
            "Playlist_DefaultSolo",
            teamSize,
            DateTimeOffset.UnixEpoch,
            90,
            null,
            players,
            teams ?? [],
            []
        );

    private static IReadOnlyList<PlayerSnapshot> Players(params PlayerSnapshot[] players) => players;

    private static PlayerSnapshot P(
        string id,
        int? team,
        string? party,
        bool you = false,
        int? placement = null,
        string? platform = "WIN",
        int? kills = 0
    ) =>
        new(
            id,
            id.ToUpperInvariant(),
            platform,
            you,
            IsBot: false,
            Disconnected: false,
            Anonymous: false,
            team,
            placement,
            party,
            kills
        );
}
