using System.Reflection;
using System.Reflection.Emit;
using FortniteReplayReader.Models.NetFieldExports;
using Unreal.Core.Attributes;
using Unreal.Core.Models.Enums;

namespace ReplayViewer.Core;

/// <summary>
/// Makes the parser read the game state of Creative replays. The parser only knows
/// <c>Athena_GameState_C</c>; Creative replays use <c>Playspace_GameState_C</c>, which has the same
/// <c>RecorderPlayerState</c> field, so without this they never name their recorder.
/// </summary>
public static class CreativeGameState
{
    public const string Path = "/Game/Athena/Playspace_GameState.Playspace_GameState_C";

    private static readonly Lazy<Type> Registered = new(Build);

    /// <summary>Call before constructing a <c>ReplayReader</c>; safe to call repeatedly.</summary>
    public static Type Register() => Registered.Value;

    // shortcut: the parser only scans assemblies whose name contains "ReplayReader", so the subclass
    // is emitted into one; replace with a real subclass if the parser ever takes explicit types.
    private static Type Build()
    {
        var module = AssemblyBuilder
            .DefineDynamicAssembly(new AssemblyName("ReplayReader.CreativeGameState"), AssemblyBuilderAccess.Run)
            .DefineDynamicModule("CreativeGameState");
        var type = module.DefineType(
            "PlayspaceGameState",
            TypeAttributes.Public | TypeAttributes.Class,
            typeof(GameState)
        );
        var ctor = typeof(NetFieldExportGroupAttribute).GetConstructor([typeof(string), typeof(ParseMode)])!;
        type.SetCustomAttribute(new CustomAttributeBuilder(ctor, [Path, ParseMode.Minimal]));
        return type.CreateType();
    }
}
