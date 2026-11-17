namespace ReplayViewer.Core;

public static class MapNames
{
    // Reload playlist ids carry the island's internal codename, not its display name.
    private static readonly (string Alias, string Name)[] ReloadMaps =
    [
        ("BlastBerry", "Venture"),
        ("PunchBerry", "Oasis"),
        ("DashBerry", "Slurp Rush"),
        ("SourSpawn", "Nitemare Island"),
        ("PiperBoot", "Surf City"),
        ("RopeSmile", "Springfield"),
        ("TimberStake", "Squid Grounds"),
        ("MatchMist", "Elite Stronghold"),
        ("BlendHeaded", "Stranger Things"),
        ("JumpBear", "Mini-Venture"),
    ];

    public static string? ForPlaylist(string? playlist)
    {
        if (string.IsNullOrWhiteSpace(playlist))
        {
            return null;
        }

        foreach (var (alias, name) in ReloadMaps)
        {
            if (playlist.Contains(alias, StringComparison.OrdinalIgnoreCase))
            {
                return "Reload - " + name;
            }
        }

        return null;
    }
}
