using System.Net;
using ReplayViewer.Core;

namespace ReplayViewer;

internal sealed class StaticFiles
{
    private readonly string _root;

    public StaticFiles(string? root = null)
    {
        _root = root ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");
    }

    public bool TryServe(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (!string.Equals(request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relative = request.Url?.AbsolutePath ?? "/";
        if (relative is "/" or "/index.html")
        {
            relative = "index.html";
        }
        else
        {
            relative = relative.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        }

        if (relative.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var path = Path.GetFullPath(Path.Combine(_root, relative));
        var rootFull = Path.GetFullPath(_root);
        if (!path.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            return false;
        }

        response.ContentType = ContentType(path);
        response.StatusCode = 200;
        using var file = File.OpenRead(path);
        file.CopyTo(response.OutputStream);
        response.OutputStream.Close();
        return true;
    }

    private static string ContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".png" => "image/png",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            _ => "application/octet-stream",
        };
}
