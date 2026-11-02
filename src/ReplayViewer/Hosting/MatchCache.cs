using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReplayViewer.Core;

namespace ReplayViewer;

/// <summary>
/// Keeps the assembled match for a replay on disk so reopening it skips the parse. The key
/// covers the file's size and timestamp (Fortnite rewrites replays) and the app version (a
/// parser or mapping change must not serve an old result). Best effort: any failure is a miss.
/// </summary>
internal static class MatchCache
{
    private const int FormatVersion = 2;
    private const int KeepFiles = 200;

    public static string DefaultRoot => Path.Combine(AppPaths.Default.RootDirectory, "cache");

    public static string? KeyFor(string replayPath)
    {
        try
        {
            var info = new FileInfo(replayPath);
            var parser =
                typeof(FortniteReplayReader.ReplayReader).Assembly.GetName().Version?.ToString() ?? "0";
            var identity =
                $"{FormatVersion}|{AppConfig.Version}|{parser}|{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
            return Convert.ToHexString(hash)[..32].ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The file moved or is unreadable; the caller simply scrapes without a cache.
            return null;
        }
    }

    public static bool TryRead(string root, string key, out MatchDocument? match)
    {
        match = null;
        var path = Path.Combine(root, key + ".json.br");
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            using var file = File.OpenRead(path);
            using var brotli = new BrotliStream(file, CompressionMode.Decompress);
            match = JsonSerializer.Deserialize<MatchDocument>(brotli, HttpJson.Options);
            return match is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A half-written or corrupt entry is a miss, not a failure.
            return false;
        }
    }

    public static void Write(string root, string key, MatchDocument match)
    {
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, key + ".json.br");
            var temp = path + ".tmp";
            using (var file = File.Create(temp))
            using (var brotli = new BrotliStream(file, CompressionLevel.SmallestSize))
            {
                JsonSerializer.Serialize(brotli, match, HttpJson.Options);
            }

            File.Move(temp, path, overwrite: true);
            Prune(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The cache is an optimisation; a failure must never fail the scrape.
            FileLog.Info($"cache write failed: {ex.Message}");
        }
    }

    /// <summary>Drops the oldest entries once the folder grows past <see cref="KeepFiles"/>.</summary>
    internal static void Prune(string root, int keep = KeepFiles)
    {
        try
        {
            var files = new DirectoryInfo(root)
                .GetFiles("*.json.br")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Skip(keep)
                .ToArray();
            foreach (var file in files)
            {
                file.Delete();
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
