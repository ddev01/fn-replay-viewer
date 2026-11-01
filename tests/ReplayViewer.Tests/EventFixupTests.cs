namespace ReplayViewer.Tests;

public class EventFixupTests
{
    [Fact]
    public void Missing_finisher_after_a_knock_credits_the_knocker()
    {
        var knock = Row(10, EventKind.Knock, "bob", "Bob", "john", "John");
        var finish = Row(12, EventKind.Finish, null, null, "john", "John");

        var filled = Assert.Single(EventFixup.Clean([knock, finish]), e => e.Kind == EventKind.Finish);
        Assert.Equal("bob", filled.ActorId);
        Assert.Equal("Bob", filled.ActorName);
    }

    [Fact]
    public void Bleed_out_credits_the_knocker()
    {
        var knock = Row(10, EventKind.Knock, "bob", "Bob", "john", "John");
        var finish = Row(18, EventKind.Finish, null, null, "john", "John", cause: "50");

        var filled = Assert.Single(EventFixup.Clean([knock, finish]), e => e.Kind == EventKind.Finish);
        Assert.Equal("bob", filled.ActorId);
    }

    [Fact]
    public void Storm_or_fall_does_not_credit_the_knocker()
    {
        var knock = Row(10, EventKind.Knock, "bob", "Bob", "john", "John");
        var storm = Row(18, EventKind.Finish, null, null, "john", "John", cause: "17");

        var finish = Assert.Single(EventFixup.Clean([knock, storm]), e => e.Kind == EventKind.Finish);
        Assert.Null(finish.ActorId);
        Assert.Equal("Storm", finish.ActorName);
    }

    [Fact]
    public void Extra_bleed_after_a_real_elim_is_dropped()
    {
        var knock = Row(10, EventKind.Knock, "bob", "Bob", "john", "John");
        var finish = Row(12, EventKind.Finish, "amy", "Amy", "john", "John");
        var bleed = Row(20, EventKind.Finish, null, null, "john", "John", cause: "50");

        var rows = EventFixup.Clean([knock, finish, bleed]);
        Assert.Equal(2, rows.Count);
        Assert.Equal("Amy", Assert.Single(rows, e => e.Kind == EventKind.Finish).ActorName);
    }

    [Fact]
    public void Later_player_finish_on_same_victim_is_kept()
    {
        var knock = Row(10, EventKind.Knock, "bob", "Bob", "john", "John");
        var first = Row(12, EventKind.Finish, null, null, "john", "John");
        var second = Row(90, EventKind.Finish, "bob", "Bob", "john", "John");

        var finishes = EventFixup.Clean([knock, first, second]).Where(e => e.Kind == EventKind.Finish).ToArray();
        Assert.Equal(2, finishes.Length);
        Assert.All(finishes, e => Assert.Equal("bob", e.ActorId));
    }

    [Fact]
    public void Storm_after_a_revive_is_kept()
    {
        var knock = Row(10, EventKind.Knock, "bob", "Bob", "john", "John");
        var revive = Row(14, EventKind.Revive, "amy", "Amy", "john", "John");
        var storm = Row(20, EventKind.Finish, null, null, "john", "John", cause: "17");

        var rows = EventFixup.Clean([knock, revive, storm]);
        var finish = Assert.Single(rows, e => e.Kind == EventKind.Finish);
        Assert.Equal("Storm", finish.ActorName);
        Assert.Contains(rows, e => e.Kind == EventKind.Revive);
    }

    [Fact]
    public void Near_duplicate_killfeed_updates_are_collapsed()
    {
        var a = Row(10, EventKind.Knock, "bob", "Bob", "john", "John");
        var b = Row(10.4, EventKind.Knock, "bob", "Bob", "john", "John");

        Assert.Single(EventFixup.Clean([a, b]));
    }

    [Fact]
    public void Killfeed_rows_without_ids_dedupe_by_name()
    {
        var a = Row(10, EventKind.Knock, null, "Bob", null, "John");
        var b = Row(10.4, EventKind.Knock, null, "bob", null, "john");

        Assert.Single(EventFixup.Clean([a, b]));
    }

    [Fact]
    public void Unattributed_finish_without_a_cause_becomes_environment()
    {
        var finish = Row(20, EventKind.Finish, null, null, "john", "John");

        var kept = Assert.Single(EventFixup.Clean([finish]));

        Assert.Null(kept.ActorId);
        Assert.Equal("Environment", kept.ActorName);
    }

    [Fact]
    public void Fall_cause_is_labelled_fall()
    {
        var finish = Row(20, EventKind.Finish, null, null, "john", "John", cause: "1");

        var kept = Assert.Single(EventFixup.Clean([finish]));

        Assert.Equal("Fall", kept.ActorName);
    }

    [Fact]
    public void Rows_that_already_say_storm_are_still_unattributed()
    {
        var storm = Row(20, EventKind.Finish, null, "Storm", "john", "John");

        var kept = Assert.Single(EventFixup.Clean([storm]));

        Assert.Null(kept.ActorId);
        Assert.Equal("Environment", kept.ActorName);
    }

    [Theory]
    [InlineData("17", true)]
    [InlineData("1", true)]
    [InlineData("50", false)]
    [InlineData(null, false)]
    public void Environmental_causes_are_recognised(string? cause, bool expected) =>
        Assert.Equal(expected, EventFixup.IsEnvironmentalCause(cause));

    [Theory]
    [InlineData("17", "Storm")]
    [InlineData("1", "Fall")]
    [InlineData("50", "Environment")]
    [InlineData(null, "Environment")]
    public void Environment_labels_cover_known_causes(string? cause, string expected) =>
        Assert.Equal(
            expected,
            EventFixup.EnvironmentLabel(
                new EventSnapshot(1, EventKind.Finish, null, null, "john", "John", null, null, cause)
            )
        );

    private static EventSnapshot Row(
        double time,
        EventKind kind,
        string? actorId,
        string? actorName,
        string? targetId,
        string? targetName,
        string? cause = null
    ) =>
        new(time, kind, actorId, actorName, targetId, targetName, null, null, cause);
}
