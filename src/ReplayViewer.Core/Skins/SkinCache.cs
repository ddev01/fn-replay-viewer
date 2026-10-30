using System.Text.Json;

namespace ReplayViewer.Core;

public sealed class SkinCache : ISkinCache
{
    public const int MaxPngBytes = 256 * 1024;
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47];

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

            using var pngResponse = await _http
                .GetAsync(new Uri(iconUrl), HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!pngResponse.IsSuccessStatusCode)
            {
                return null;
            }

            if (pngResponse.Content.Headers.ContentLength is > MaxPngBytes)
            {
                return null;
            }

            var png = await pngResponse.Content.ReadAsByteArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            if (png.Length is 0 or > MaxPngBytes)
            {
                return null;
            }

            if (png.Length < 4 || !png.AsSpan(0, 4).SequenceEqual(PngMagic))
            {
                return null;
            }

            return png;
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
