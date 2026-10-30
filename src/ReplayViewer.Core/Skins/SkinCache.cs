using System.Text.Json;

namespace ReplayViewer.Core;

public sealed class SkinCache : ISkinCache
{
    private readonly string _directory;
    private readonly HttpClient _http;

    public SkinCache(string? directory = null, HttpClient? http = null)
    {
        _directory = directory ?? AppPaths.Default.SkinsDirectory;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        Directory.CreateDirectory(_directory);
    }

    public async Task<byte[]?> GetPngAsync(string cid, CancellationToken cancellationToken)
    {
        if (!CosmeticIds.IsSafe(cid))
        {
            return null;
        }

        var path = Path.Combine(_directory, cid + ".png");
        if (File.Exists(path))
        {
            return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }

        foreach (var lookup in CosmeticIds.LookupIds(cid))
        {
            var png = await TryDownloadAsync(lookup, cancellationToken).ConfigureAwait(false);
            if (png is { Length: > 0 })
            {
                await File.WriteAllBytesAsync(path, png, cancellationToken).ConfigureAwait(false);
                return png;
            }
        }

        return null;
    }

    private async Task<byte[]?> TryDownloadAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http
                .GetAsync(
                    $"https://fortnite-api.com/v2/cosmetics/br/{Uri.EscapeDataString(id)}",
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var json = await response
                .Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(json, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (
                !doc.RootElement.TryGetProperty("data", out var data)
                || !data.TryGetProperty("images", out var images)
                || !images.TryGetProperty("smallIcon", out var icon)
            )
            {
                return null;
            }

            var iconUrl = icon.GetString();
            if (string.IsNullOrWhiteSpace(iconUrl))
            {
                return null;
            }

            var png = await _http.GetByteArrayAsync(new Uri(iconUrl), cancellationToken)
                .ConfigureAwait(false);
            return png.Length == 0 ? null : png;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
