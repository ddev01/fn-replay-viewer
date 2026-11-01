using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using ReplayViewer.Core;

namespace ReplayViewer;

internal static class UpdateChecker
{
    private static readonly HttpClient CheckClient = CreateClient(TimeSpan.FromSeconds(15));

    public sealed record Available(string Version, string ZipUrl, string? Digest);

    /// <summary>Returns the newer release, or null when this build is current.</summary>
    public static async Task<Available?> CheckAsync(CancellationToken cancellationToken)
    {
        FileLog.Info($"update check: current {AppConfig.Version}");
        using var response = await CheckClient
            .GetAsync(
                $"https://api.github.com/repos/{AppConfig.GitHubRepo}/releases/latest",
                cancellationToken
            )
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GitHub returned HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var doc = await JsonDocument
            .ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var root = doc.RootElement;

        var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
        if (!AppVersion.IsNewer(tag, AppConfig.Version))
        {
            return null;
        }

        if (!root.TryGetProperty("assets", out var assets))
        {
            throw new InvalidOperationException($"Release {tag} has no update package.");
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
            if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var url = asset.TryGetProperty("browser_download_url", out var urlEl)
                ? urlEl.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            var digest = asset.TryGetProperty("digest", out var digestEl)
                ? digestEl.GetString()
                : null;
            return new Available(tag.TrimStart('v', 'V'), url, digest);
        }

        throw new InvalidOperationException($"Release {tag} has no update package.");
    }

    /// <summary>Downloads, verifies and extracts the update; returns the new executable path.</summary>
    public static async Task<string> PrepareAsync(Available update, CancellationToken cancellationToken)
    {
        var dir = Path.Combine(
            Path.GetTempPath(),
            "ReplayViewerUpdate",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(dir);
        var zipPath = Path.Combine(dir, "update.zip");
        using (var http = CreateClient(TimeSpan.FromMinutes(5)))
        using (
            var response = await http
                .GetAsync(update.ZipUrl, cancellationToken)
                .ConfigureAwait(false)
        )
        {
            response.EnsureSuccessStatusCode();
            await using var file = File.Create(zipPath);
            await response.Content
                .CopyToAsync(file, cancellationToken)
                .ConfigureAwait(false);
        }

        VerifyDigest(zipPath, update.Digest);
        ZipFile.ExtractToDirectory(zipPath, dir);
        return Directory
            .EnumerateFiles(dir, AppPaths.ExeFileName, SearchOption.AllDirectories)
            .FirstOrDefault() ?? throw new FileNotFoundException(
            $"Update package did not contain {AppPaths.ExeFileName}."
        );
    }

    private static void VerifyDigest(string path, string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
        {
            FileLog.Info("update digest missing, skipping verification");
            return;
        }

        const string prefix = "sha256:";
        if (!digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            FileLog.Info($"update digest uses an unknown algorithm, skipping verification: {digest}");
            return;
        }

        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        var expected = digest[prefix.Length..];
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Update download failed its SHA-256 check.");
        }
    }

    private static HttpClient CreateClient(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("fn-replay-viewer");
        http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json")
        );
        return http;
    }
}
