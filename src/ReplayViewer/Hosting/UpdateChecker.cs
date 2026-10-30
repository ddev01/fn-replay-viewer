using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using ReplayViewer.Core;

namespace ReplayViewer;

internal static class UpdateChecker
{
    public static async Task RunAsync(TrayApp tray, CancellationToken cancellationToken)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("fn-replay-viewer");
            http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json")
            );

            using var response = await http
                .GetAsync(
                    $"https://api.github.com/repos/{AppConfig.GitHubRepo}/releases/latest",
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var root = doc.RootElement;
            if (!root.TryGetProperty("tag_name", out var tagEl))
            {
                return;
            }

            var tag = tagEl.GetString() ?? "";
            if (!AppVersion.IsNewer(tag, AppConfig.Version))
            {
                return;
            }

            string? zipUrl = null;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    zipUrl = asset.GetProperty("browser_download_url").GetString();
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(zipUrl))
            {
                zipUrl = root.GetProperty("html_url").GetString();
                tray.NotifyUpdate(() => OpenUrl(zipUrl));
                return;
            }

            FileLog.Info($"update available: {tag} (current {AppConfig.Version})");
            var download = zipUrl;
            tray.NotifyUpdate(() => Apply(download));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            FileLog.Error("update check failed", ex);
        }
    }

    private static void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    private static void Apply(string zipUrl)
    {
        try
        {
            FileLog.Info("applying update from " + zipUrl);
            var dir = Path.Combine(Path.GetTempPath(), "ReplayViewerUpdate", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var zipPath = Path.Combine(dir, "update.zip");
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("fn-replay-viewer");
                using var response = http.GetAsync(zipUrl).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
                using var file = File.Create(zipPath);
                response.Content.CopyTo(file, null, CancellationToken.None);
            }

            ZipFile.ExtractToDirectory(zipPath, dir);
            var exe = Directory
                .EnumerateFiles(dir, AppPaths.ExeFileName, SearchOption.AllDirectories)
                .FirstOrDefault();
            if (exe is null)
            {
                FileLog.Error("update zip had no " + AppPaths.ExeFileName);
                return;
            }

            var installed = AppPaths.Default.InstalledExePath;
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments =
                        "/c timeout /t 2 /nobreak >nul & \""
                        + exe
                        + "\" --install & start \"\" \""
                        + installed
                        + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            );
            Application.Exit();
        }
        catch (Exception ex)
        {
            FileLog.Error("update apply failed", ex);
        }
    }
}
