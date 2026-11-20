using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReplayViewer.Tests;

/// <summary>
/// The UI's types live in src/ReplayViewer/types/match.d.ts, written by hand. This keeps them
/// honest: a record gaining, losing or changing the nullability of a property fails here until
/// the interface follows.
/// </summary>
public sealed partial class ContractTests
{
    public static TheoryData<Type, string> Pairs =>
        new()
        {
            { typeof(OwnerStatsSnapshot), "OwnerStats" },
            { typeof(RosterPlayer), "RosterPlayer" },
            { typeof(TeamRoster), "TeamRoster" },
            { typeof(MatchEvent), "MatchEvent" },
            { typeof(MatchDocument), "MatchDocument" },
            { typeof(DemoListItem), "DemoListItem" },
        };

    [Theory]
    [MemberData(nameof(Pairs))]
    public void The_typescript_interface_matches_the_record(Type record, string tsName)
    {
        var declared = Interface(tsName);
        var expected = Expected(record);

        Assert.True(
            expected.OrderBy(p => p.Key).SequenceEqual(declared.OrderBy(p => p.Key)),
            $"{tsName} in match.d.ts differs from {record.Name}.\n"
                + $"  C#:  {Describe(expected)}\n  d.ts: {Describe(declared)}"
        );
    }

    [Theory]
    [InlineData(typeof(EventKind), "EventKind")]
    [InlineData(typeof(FillStatus), "FillStatus")]
    public void The_typescript_union_matches_the_enum(Type enumType, string tsName)
    {
        var text = DeclarationFile();
        var union = Regex.Match(text, $@"type {tsName} = ([^;]+);");
        Assert.True(union.Success, $"{tsName} is missing from match.d.ts");

        var declared = Regex.Matches(union.Groups[1].Value, "\"([^\"]+)\"").Select(m => m.Groups[1].Value);
        var expected = Enum.GetNames(enumType).Select(JsonNamingPolicy.CamelCase.ConvertName);

        Assert.Equal(expected.Order(), declared.Order());
    }

    private static Dictionary<string, bool> Expected(Type record)
    {
        var nullability = new NullabilityInfoContext();
        return record
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(
                property => JsonNamingPolicy.CamelCase.ConvertName(property.Name),
                property =>
                    Nullable.GetUnderlyingType(property.PropertyType) is not null
                    || nullability.Create(property).ReadState == NullabilityState.Nullable
            );
    }

    private static Dictionary<string, bool> Interface(string name)
    {
        var body = Regex.Match(DeclarationFile(), $@"interface {name} \{{(.*?)\n\}}", RegexOptions.Singleline);
        Assert.True(body.Success, $"interface {name} is missing from match.d.ts");

        return PropertyLine()
            .Matches(body.Groups[1].Value)
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value == "?");
    }

    private static string Describe(Dictionary<string, bool> properties) =>
        string.Join(", ", properties.OrderBy(p => p.Key).Select(p => p.Key + (p.Value ? "?" : "")));

    private static string DeclarationFile()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "src", "ReplayViewer", "types", "match.d.ts");
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }

        throw new FileNotFoundException("match.d.ts not found above the test output folder.");
    }

    [GeneratedRegex(@"^\s*(\w+)(\??):", RegexOptions.Multiline)]
    private static partial Regex PropertyLine();
}
