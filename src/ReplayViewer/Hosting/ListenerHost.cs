using System.Net;
using ReplayViewer.Core;

namespace ReplayViewer;

internal sealed class ListenerHost : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly IReadOnlyList<IHttpRoute> _routes;
    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<string> _allowedHosts;
    private Task? _loop;

    public ListenerHost(IReadOnlyList<IHttpRoute> routes, string? prefix = null)
    {
        _routes = routes;
        prefix ??= AppPaths.ListenPrefix;
        _listener.Prefixes.Add(prefix);
        // HTTP.sys does not match the Host header against an IP-literal prefix, so a DNS-rebound
        // page (Host: evil.example) would otherwise be served. Only our own names are allowed.
        var port = new Uri(prefix).Port;
        _allowedHosts = new(StringComparer.OrdinalIgnoreCase)
        {
            $"127.0.0.1:{port}",
            $"localhost:{port}",
        };
    }

    public void Start()
    {
        _listener.Start();
        _loop = Task.Run(() => AcceptLoopAsync(_cts.Token));
        FileLog.Info($"listening on {AppPaths.ListenPrefix}");
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            // Stop() throws when the listener never started, and Program.cs disposes after a
            // failed Start(), so only stop what is actually listening.
            if (_listener.IsListening)
            {
                _listener.Stop();
            }

            _listener.Close();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _cts.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }

            _ = Task.Run(() => DispatchAsync(context, cancellationToken), CancellationToken.None);
        }
    }

    private static void Reject(HttpListenerContext context, int status)
    {
        context.Response.StatusCode = status;
        context.Response.OutputStream.Close();
    }

    /// <summary>
    /// Refuses API calls a browser made on behalf of another site. Requests with neither header
    /// (curl, tests, older browsers) are allowed: they are not cross-site browser traffic.
    /// </summary>
    private bool IsSameOrigin(HttpListenerRequest request)
    {
        var site = request.Headers["Sec-Fetch-Site"];
        if (site is not null && site is not ("same-origin" or "none"))
        {
            return false;
        }

        var origin = request.Headers["Origin"];
        return origin is null
            || (
                origin.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && _allowedHosts.Contains(origin["http://".Length..])
            );
    }

    private async Task DispatchAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            // The UI needs no inline script/style and no remote resources, so keep it locked down.
            // Mirrored by tests/ui/serve.mjs, which runs the UI tests under the same policy.
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; img-src 'self' data:; object-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";

            if (!_allowedHosts.Contains(context.Request.UserHostName ?? ""))
            {
                Reject(context, 421);
                return;
            }

            var isApi = context.Request.Url?.AbsolutePath.StartsWith(
                "/api/",
                StringComparison.OrdinalIgnoreCase
            ) == true;
            if (isApi)
            {
                // API JSON holds player names and file paths: never let a browser cache it.
                context.Response.Headers["Cache-Control"] = "no-store";
                if (!IsSameOrigin(context.Request))
                {
                    Reject(context, 403);
                    return;
                }
            }

            foreach (var route in _routes)
            {
                if (route.Matches(context.Request))
                {
                    await route.HandleAsync(context, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }

            context.Response.StatusCode = 404;
            context.Response.OutputStream.Close();
        }
        catch (Exception ex)
        {
            FileLog.Error("request failed", ex);
            try
            {
                context.Response.StatusCode = 500;
                context.Response.OutputStream.Close();
            }
            catch (HttpListenerException)
            {
            }
        }
    }
}
