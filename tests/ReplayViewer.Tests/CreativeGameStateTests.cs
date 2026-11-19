using System.Reflection;
using FortniteReplayReader.Models.NetFieldExports;
using Unreal.Core.Attributes;

namespace ReplayViewer.Tests;

public sealed class CreativeGameStateTests
{
    [Fact]
    public void The_creative_game_state_is_a_game_state_the_parser_will_scan()
    {
        var type = CreativeGameState.Register();

        Assert.True(type.IsSubclassOf(typeof(GameState)));
        Assert.Contains("ReplayReader", type.Assembly.FullName);
        Assert.Equal(
            CreativeGameState.Path,
            type.GetCustomAttribute<NetFieldExportGroupAttribute>()!.Path
        );
        Assert.Same(type, CreativeGameState.Register());
    }
}
