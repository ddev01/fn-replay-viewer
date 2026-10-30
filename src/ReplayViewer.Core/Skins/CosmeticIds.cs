using System.Text.RegularExpressions;

namespace ReplayViewer.Core;

public static class CosmeticIds
{
    private static readonly Regex SafeId = new(
        @"^(CID_|HID_|Character_)[A-Za-z0-9_]+$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly string[] Prefixes = ["CID_", "HID_", "Character_"];

    public static bool IsSafe(string? id) => !string.IsNullOrWhiteSpace(id) && SafeId.IsMatch(id);

    public static string? Extract(params string?[] values)
    {
        foreach (var value in values)
        {
            var id = ExtractOne(value);
            if (id is not null)
            {
                return id;
            }
        }

        return null;
    }

    public static IReadOnlyList<string> LookupIds(string id)
    {
        if (!IsSafe(id))
        {
            return [];
        }

        var list = new List<string> { id };
        if (id.StartsWith("HID_", StringComparison.OrdinalIgnoreCase))
        {
            var rest = id[4..];
            AddUnique(list, "CID_" + rest + "_Default");
            AddUnique(list, "CID_" + rest);
        }

        return list;
    }

    private static void AddUnique(List<string> list, string id)
    {
        if (!list.Exists(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase)))
        {
            list.Add(id);
        }
    }

    private static string? ExtractOne(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        foreach (var prefix in Prefixes)
        {
            var start = text.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                continue;
            }

            var span = text.AsSpan(start);
            var end = 0;
            while (end < span.Length && (char.IsAsciiLetterOrDigit(span[end]) || span[end] == '_'))
            {
                end++;
            }

            if (end > prefix.Length)
            {
                return span[..end].ToString();
            }
        }

        return null;
    }
}
