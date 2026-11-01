using System.Reflection;

namespace ReplayViewer;

internal static class AppConfig
{
    public const string GitHubRepo = "ddev01/fn-replay-viewer";

    public static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

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
