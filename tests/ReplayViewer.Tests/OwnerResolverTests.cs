namespace ReplayViewer.Tests;

public sealed class OwnerResolverTests
{
    private const string Owner = "9A869FE1A02E44238C965778C201F9C9";

    [Fact]
    public void A_known_id_becomes_the_owner_and_brings_the_placement()
    {
        var match = Match(
            new TeamRoster(
                4,
                9,
                [
                    Player("a", you: false),
                    Player(Owner, you: false, placement: 3),
                ]
            )
        );

        var resolved = OwnerResolver.Apply(match, Ids(Owner));

        // Index in the roster means nothing; only the id does.
        Assert.False(resolved.Teams[0].Players[0].IsYou);
        Assert.True(resolved.Teams[0].Players[1].IsYou);
        // The player's own placement, not the team's, matches what the game shows.
        Assert.Equal(3, resolved.YourPlacement);
    }

    [Fact]
    public void Without_a_player_placement_the_team_placement_is_used()
    {
        var match = Match(new TeamRoster(4, 9, [Player(Owner, you: false)]));

        Assert.Equal(9, OwnerResolver.Apply(match, Ids(Owner)).YourPlacement);
    }

    [Fact]
    public void A_match_that_names_its_owner_is_left_alone()
    {
        var match = Match(
            new TeamRoster(1, 1, [Player(Owner, you: true), Player("other", you: false)])
        );

        var resolved = OwnerResolver.Apply(match, Ids("someone-else"));

        Assert.True(resolved.Teams[0].Players[0].IsYou);
        Assert.False(resolved.Teams[0].Players[1].IsYou);
        Assert.Same(match, resolved);
    }

    [Fact]
    public void No_known_id_leaves_the_match_unchanged()
    {
        var match = Match(new TeamRoster(1, 1, [Player(Owner, you: false)]));

        Assert.Same(match, OwnerResolver.Apply(match, Ids("someone-else")));
    }

    [Fact]
    public void Ids_are_matched_without_case()
    {
        var match = Match(new TeamRoster(1, 2, [Player(Owner.ToLowerInvariant(), you: false)]));

        Assert.True(OwnerResolver.Apply(match, Ids(Owner)).Teams[0].Players[0].IsYou);
    }

    private static IReadOnlySet<string> Ids(params string[] ids) =>
        new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);

    private static RosterPlayer Player(string epicId, bool you, int? placement = null) =>
        new(epicId, epicId, "WIN", 3, you, false, false, false, FillStatus.None, Placement: placement);

    private static MatchDocument Match(params TeamRoster[] teams) =>
        new(
            "a.replay",
            DateTimeOffset.UnixEpoch,
            "Playlist_DefaultDuo",
            2,
            DateTimeOffset.UnixEpoch,
            null,
            null,
            null,
            null,
            teams,
            []
        );
}
