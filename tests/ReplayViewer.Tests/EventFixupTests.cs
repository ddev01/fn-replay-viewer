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
    public void Near_duplicate_killfeed_updates_are_collapsed()
    {
        var a = Row(10, EventKind.Knock, "bob", "Bob", "john", "John");
        var b = Row(10.4, EventKind.Knock, "bob", "Bob", "john", "John");

        Assert.Single(EventFixup.Clean([a, b]));
    }

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
