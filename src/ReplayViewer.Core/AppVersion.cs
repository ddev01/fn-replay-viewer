namespace ReplayViewer.Core;

public static class AppVersion
{
    public static bool IsNewer(string tag, string current)
    {
        var t = tag.Trim();
        if (t.StartsWith('v') || t.StartsWith('V'))
        {
            t = t[1..];
        }

        if (!Version.TryParse(t, out var remote) || !Version.TryParse(current, out var local))
        {
            return false;
        }

        return remote > local;
    }
}
