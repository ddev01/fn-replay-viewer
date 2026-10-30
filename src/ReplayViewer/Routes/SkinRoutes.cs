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

        if (png is not { Length: > 0 })
        {
            context.Response.StatusCode = 404;
            context.Response.OutputStream.Close();
            return;
        }

        await HttpJson
            .WriteBytesAsync(context.Response, png, "image/png", cacheControl: "public, max-age=86400")
            .ConfigureAwait(false);
    }
}
