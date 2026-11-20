using System.Reflection;

namespace ReplayViewer.Tests;

/// <summary>
/// The stall guard and the recorder lookup read private fields of FortniteReplayReader. If a
/// package update moves them, the guard silently turns off and the recorder is no longer found.
/// This fails the build at that moment, with no real replay needed.
/// </summary>
public sealed class ParserInternalsTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [Fact]
    public void The_stall_guard_can_still_read_the_parsers_chunk_counter()
    {
        var field = Find(typeof(FortniteReplayReader.ReplayReader), "replayDataIndex");

        Assert.NotNull(field);
        Assert.Equal(typeof(int), field.FieldType);
    }

    [Fact]
    public void The_recorder_lookup_can_still_read_the_parsers_player_maps()
    {
        var builder = Find(typeof(FortniteReplayReader.ReplayReader), "Builder");
        Assert.NotNull(builder);

        Assert.NotNull(Find(builder.FieldType, "_actorToChannel"));
        Assert.NotNull(Find(builder.FieldType, "_players"));
    }

    private static FieldInfo? Find(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.GetField(name, Any) is { } field)
            {
                return field;
            }
        }

        return null;
    }
}
