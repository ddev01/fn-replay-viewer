using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using ReplayViewer.Core;

namespace ReplayViewer;

internal static class UpdateChecker
{
    private const string VersionFileName = "version.txt";
    private static readonly HttpClient CheckClient = CreateClient(TimeSpan.FromSeconds(15));

    public sealed record Available(
        string Version,
        string ZipUrl,
        string? Digest,
        string? SignatureUrl
    );

    /// <summary>Set by the tray after a check so the web UI can show a banner.</summary>
    public static string? AvailableVersion { get; set; }

    /// <summary>Where a verified update waits for the next launch (or an explicit apply).</summary>
    public static string PendingRoot => Path.Combine(Path.GetTempPath(), "ReplayViewerPending");

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

        return ParseRelease(doc.RootElement, AppConfig.Version);
    }

    /// <summary>
    /// Picks the update asset out of a GitHub "latest release" payload. Pure so it can be
    /// tested without the network: a bad asset selection ships a broken updater.
    /// </summary>
    internal static Available? ParseRelease(JsonElement root, string currentVersion)
    {
        var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
        if (!AppVersion.IsNewer(tag, currentVersion))
        {
            return null;
        }

        if (!root.TryGetProperty("assets", out var assets))
        {
            throw new InvalidOperationException($"Release {tag} has no update package.");
        }

        string? zipName = null;
        string? zipUrl = null;
        string? digest = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = AssetName(asset);
            var url = AssetUrl(asset);
            if (string.IsNullOrWhiteSpace(url) || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            zipName = name;
            zipUrl = url;
            digest = asset.TryGetProperty("digest", out var digestEl) ? digestEl.GetString() : null;
        }

        if (zipName is null || zipUrl is null)
        {
            throw new InvalidOperationException($"Release {tag} has no update package.");
        }

        // The signature is a separate asset named after the zip, so it is looked up once the
        // zip name is known.
        string? signatureUrl = null;
        foreach (var asset in assets.EnumerateArray())
        {
            if (!AssetName(asset).Equals(zipName + ".sig", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var url = AssetUrl(asset);
            if (!string.IsNullOrWhiteSpace(url))
            {
                signatureUrl = url;
            }
        }

        return new Available(tag.TrimStart('v', 'V'), zipUrl, digest, signatureUrl);
    }

    private static string AssetName(JsonElement asset) =>
        asset.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";

    private static string? AssetUrl(JsonElement asset) =>
        asset.TryGetProperty("browser_download_url", out var urlEl) ? urlEl.GetString() : null;

    /// <summary>
    /// Downloads, verifies and extracts an update into the pending folder, where it waits for
    /// the next launch. Silent: nothing is installed and nothing restarts.
    /// </summary>
    public static async Task<string> StageAsync(Available update, CancellationToken cancellationToken)
    {
        ClearPending();
        Directory.CreateDirectory(PendingRoot);
        var exe = await DownloadVerifiedAsync(update, PendingRoot, cancellationToken)
            .ConfigureAwait(false);
        File.WriteAllText(Path.Combine(PendingRoot, VersionFileName), update.Version);
        FileLog.Info($"update {update.Version} staged at {exe}");
        return exe;
    }

    /// <summary>Downloads, verifies and extracts the update; returns the new executable path.</summary>
    public static async Task<string> PrepareAsync(Available update, CancellationToken cancellationToken)
    {
        var root = Path.Combine(Path.GetTempPath(), "ReplayViewerUpdate");
        var dir = Path.Combine(root, Guid.NewGuid().ToString("N"));
        PruneOldUpdates(root, keep: dir);
        Directory.CreateDirectory(dir);
        return await DownloadVerifiedAsync(update, dir, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> DownloadVerifiedAsync(
        Available update,
        string directory,
        CancellationToken cancellationToken
    )
    {
        var zipPath = Path.Combine(directory, "update.zip");
        using (var http = CreateClient(TimeSpan.FromMinutes(5)))
        {
            await DownloadAsync(http, update.ZipUrl, zipPath, cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(update.SignatureUrl))
            {
                throw new InvalidDataException(
                    "Release has no detached signature, refusing to install it."
                );
            }

            var signaturePath = zipPath + ".sig";
            await DownloadAsync(http, update.SignatureUrl, signaturePath, cancellationToken)
                .ConfigureAwait(false);
            VerifySignature(zipPath, signaturePath);
        }

        VerifyDigest(zipPath, update.Digest);
        ZipFile.ExtractToDirectory(zipPath, directory, overwriteFiles: true);
        File.Delete(zipPath);
        File.Delete(zipPath + ".sig");
        return Directory
            .EnumerateFiles(directory, AppPaths.ExeFileName, SearchOption.AllDirectories)
            .FirstOrDefault() ?? throw new FileNotFoundException(
            $"Update package did not contain {AppPaths.ExeFileName}."
        );
    }

    private static async Task DownloadAsync(
        HttpClient http,
        string url,
        string path,
        CancellationToken cancellationToken
    )
    {
        using var response = await http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var file = File.Create(path);
        await response.Content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>True when a verified payload is waiting to be applied.</summary>
    public static bool TryGetPending(out string version, out string exePath) =>
        TryGetPending(PendingRoot, out version, out exePath);

    internal static bool TryGetPending(string root, out string version, out string exePath)
    {
        version = "";
        exePath = "";
        try
        {
            var versionFile = Path.Combine(root, VersionFileName);
            var exe = Path.Combine(root, AppPaths.ExeFileName);
            if (!File.Exists(versionFile) || !File.Exists(exe))
            {
                return false;
            }

            version = File.ReadAllText(versionFile).Trim();
            exePath = exe;
            return version.Length > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Drops the "ready" marker but keeps the payload, because the installer is about to run
    /// from it. Without this the next launch would try to apply the same update again.
    /// </summary>
    public static void MarkApplied()
    {
        try
        {
            File.Delete(Path.Combine(PendingRoot, VersionFileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FileLog.Info($"could not clear the pending marker: {ex.Message}");
        }
    }

    public static void ClearPending() => ClearPending(PendingRoot);

    internal static void ClearPending(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FileLog.Info($"could not clear pending update: {ex.Message}");
        }
    }

    /// <summary>
    /// Each update extracts a fresh payload; without this they accumulate in temp forever.
    /// Best effort: a folder a previous update still has open is not worth failing over.
    /// </summary>
    internal static void PruneOldUpdates(string root, string keep)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var stale in Directory.EnumerateDirectories(root))
        {
            if (string.Equals(stale, keep, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                Directory.Delete(stale, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                FileLog.Info($"could not remove old update folder {stale}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Fails closed: an unsigned release is refused, because the signature is the only proof
    /// that the artifact came from the signing key and not from a compromised account.
    /// </summary>
    internal static void VerifySignature(string path, string signaturePath)
    {
        var data = File.ReadAllBytes(path);
        var signature = File.ReadAllBytes(signaturePath);
        if (!ReleaseSignature.Verify(data, signature))
        {
            throw new InvalidDataException("Update signature did not verify.");
        }
    }

    internal static void VerifyDigest(string path, string? digest)
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
