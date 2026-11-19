namespace ReplayViewer.Tests;

public sealed class RecorderResolverTests
{
    private const string Recorder = "9A869FE1A02E44238C965778C201F9C9";

    [Fact]
    public void The_recorder_the_state_names_is_you()
    {
        var players = RecorderResolver.ApplyRecorder(
            [Player("someone"), Player(Recorder)],
            Recorder
        );

        Assert.False(players[0].IsReplayOwner);
        Assert.True(players[1].IsReplayOwner);
    }

    [Fact]
    public void A_replay_that_already_names_its_owner_is_left_alone()
    {
        var players = RecorderResolver.ApplyRecorder(
            [Player("a", owner: true), Player(Recorder)],
            Recorder
        );

        Assert.True(players[0].IsReplayOwner);
        Assert.False(players[1].IsReplayOwner);
    }

    [Fact]
    public void A_recorder_who_is_not_in_the_roster_changes_nothing()
    {
        var players = RecorderResolver.ApplyRecorder([Player("a"), Player("b")], Recorder);

        Assert.All(players, player => Assert.False(player.IsReplayOwner));
    }

    [Fact]
    public void No_recorder_leaves_the_players_alone()
    {
        var players = RecorderResolver.ApplyRecorder([Player(Recorder)], null);

        Assert.False(players[0].IsReplayOwner);
    }

    [Fact]
    public void Ids_are_matched_without_case()
    {
        var players = RecorderResolver.ApplyRecorder(
            [Player(Recorder.ToLowerInvariant())],
            Recorder
        );

        Assert.True(players[0].IsReplayOwner);
    }

    private static PlayerSnapshot Player(string epicId, bool owner = false) =>
        new(epicId, epicId, "WIN", owner, false, false, false, 1, 5, epicId, 3);
}
