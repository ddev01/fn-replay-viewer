using System.Reflection;

namespace ReplayViewer;

internal static class AppConfig
{
    public const string GitHubRepo = "ddev01/fn-replay-viewer";

    public static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>
    /// What the user sees: the informational version without the SDK's "+commit" suffix. A local
    /// build from scripts/install-dev.ps1 carries a "-dev.&lt;timestamp&gt;" tag here while its
    /// assembly version stays the release number, so the updater logic is unaffected.
    /// </summary>
    public static string DisplayVersion =>
        Assembly
            .GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0]
        ?? Version;

    /// <summary>A local dev build never updates itself: a release must not replace it unasked.</summary>
    public static bool IsDev => DisplayVersion.Contains("-dev", StringComparison.Ordinal);

    public static string? NameFnPublicUrl
    {
        get
        {
            var value = Assembly
                .GetExecutingAssembly()
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "NameFnPublicUrl")
                ?.Value;
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Trim().TrimEnd('/');
        }
    }
}
