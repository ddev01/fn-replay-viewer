using System.Net;
using ReplayViewer.Core;

namespace ReplayViewer;

internal sealed class SkinRoutes : IHttpRoute
{
    private readonly ISkinCache _cache;

    public SkinRoutes(ISkinCache cache)
    {
        _cache = cache;
    }

    public bool Matches(HttpListenerRequest request)
    {
        return string.Equals(request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase)
            && (request.Url?.AbsolutePath.StartsWith("/skins/", StringComparison.OrdinalIgnoreCase)
                ?? false);
    }

    public async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var cid = Uri.UnescapeDataString(context.Request.Url!.AbsolutePath["/skins/".Length..]);
        byte[]? png = null;
        try
        {
            png = await _cache.GetPngAsync(cid, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            FileLog.Error("skin fetch failed", ex);
        }

        var hit = png is { Length: > 0 };
        var body = hit ? png! : PlaceholderPng.Bytes;
        await HttpJson
            .WriteBytesAsync(
                context.Response,
                body,
                "image/png",
                cacheControl: hit ? "public, max-age=86400" : "no-store"
            )
            .ConfigureAwait(false);
    }
}
