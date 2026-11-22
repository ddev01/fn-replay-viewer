using System.Text.Json;
using System.Text.RegularExpressions;
using ReplayViewer.Core;

namespace ReplayViewer;

/// <summary>
/// The owner-only linking switch: <c>%LOCALAPPDATA%\ReplayViewer\linking.json</c> holding
/// <c>{"baseUrl","token"}</c>. Read once at start. A missing or invalid file means linking is off:
/// no routes, no UI, no outbound request. A class, not a record, so the token can never end up
/// in a log line through <c>ToString()</c>.
/// </summary>
internal sealed partial class LinkingConfig
{
    private LinkingConfig(string baseUrl, string token)
    {
        BaseUrl = baseUrl;
        Token = token;
    }

    /// <summary>Absolute URL without a trailing slash.</summary>
    public string BaseUrl { get; }

    public string Token { get; }

    public static string DefaultPath => Path.Combine(AppPaths.Default.RootDirectory, "linking.json");

    // shortcut: the token sits in a plain file. DPAPI needs the System.Security.Cryptography.ProtectedData
    // package, and a new package means regenerating the lock files. This is a switch, not the secret's
    // only protection (the server key and allowlist are); upgrade if the file ever leaves the owner's PC.
    public static LinkingConfig? Load(string path)
    {
        string text;
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var config = Parse(text);
        if (config is null)
        {
            // Never log the content: it holds the token.
            FileLog.Info("linking.json is invalid; linking is off");
        }

        return config;
    }

    internal static LinkingConfig? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (
                root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("baseUrl", out var url)
                || url.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("token", out var tokenProp)
                || tokenProp.ValueKind != JsonValueKind.String
            )
            {
                return null;
            }

            var token = tokenProp.GetString();
            if (
                token is null
                || !PrintableAscii().IsMatch(token)
                || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri)
                || uri.UserInfo.Length != 0
                || uri.Query.Length != 0
                || uri.Fragment.Length != 0
            )
            {
                return null;
            }

            // http is for a local test server only: the token must not cross a network in clear.
            var secure =
                uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
            return secure ? new LinkingConfig(uri.GetLeftPart(UriPartial.Path).TrimEnd('/'), token) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // A header value: no whitespace or control characters, and never empty.
    [GeneratedRegex(@"^[\x21-\x7E]+$")]
    private static partial Regex PrintableAscii();
}
