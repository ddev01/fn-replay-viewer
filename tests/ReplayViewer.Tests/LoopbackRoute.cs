using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ReplayViewer.Tests;

/// <summary>
/// Drives a route over a real <see cref="HttpListener"/> on a free loopback port:
/// <see cref="HttpListenerContext"/> has no public constructor, so it cannot be faked.
/// </summary>
internal sealed class LoopbackRoute : IAsyncDisposable
{
    /// <summary>Status written when the route declines the request (returned false).</summary>
    public const int UnhandledStatus = 418;

    private readonly HttpListener _listener = new();
    private readonly Func<HttpListenerContext, CancellationToken, Task<bool>> _handle;
    private readonly CancellationTokenSource _cts = new();
    private Task _loop = Task.CompletedTask;

    private LoopbackRoute(Func<HttpListenerContext, CancellationToken, Task<bool>> handle)
    {
        _handle = handle;
    }

    public Uri BaseUri { get; private set; } = new("http://127.0.0.1/");

    public HttpClient Client { get; } = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static async Task<LoopbackRoute> StartAsync(
        Func<HttpListenerContext, CancellationToken, Task<bool>> handle
    )
    {
        var route = new LoopbackRoute(handle);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var port = FreePort();
            route._listener.Prefixes.Clear();
            route._listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try
            {
                route._listener.Start();
            }
            catch (HttpListenerException)
            {
                // The probed port was taken before the bind; try the next one.
                await Task.Delay(10, TestContext.Current.CancellationToken);
                continue;
            }

            route.BaseUri = new Uri($"http://127.0.0.1:{port}/");
            route._loop = Task.Run(() => route.AcceptLoopAsync(route._cts.Token));
            return route;
        }

        route._listener.Close();
        throw new InvalidOperationException("Could not bind a loopback HttpListener.");
    }

    public Uri Url(string pathAndQuery) => new(BaseUri, pathAndQuery);

    /// <summary>Sends the path verbatim, so traversal sequences survive to the server.</summary>
    public Uri RawUrl(string pathAndQuery) =>
        new(
            BaseUri.GetLeftPart(UriPartial.Authority) + pathAndQuery,
            new UriCreationOptions { DangerousDisablePathAndQueryCanonicalization = true }
        );

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
        }

        Client.Dispose();
        _cts.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or OperationCanceledException)
            {
                return;
            }

            try
            {
                if (!await _handle(context, cancellationToken))
                {
                    context.Response.StatusCode = UnhandledStatus;
                    context.Response.OutputStream.Close();
                }
            }
            catch (Exception ex)
            {
                await WriteFailureAsync(context.Response, ex);
            }
        }
    }

    private static async Task WriteFailureAsync(HttpListenerResponse response, Exception exception)
    {
        // A route that throws must not leave the client waiting for a response. The exception
        // text is deliberately not written: echoing a stack trace into a response is the pattern
        // CodeQL flags, and the tests assert on status codes, not bodies.
        var body = Encoding.UTF8.GetBytes("unhandled: " + exception.GetType().Name);
        response.StatusCode = 599;
        response.ContentType = "text/plain";
        response.ContentLength64 = body.Length;
        await response.OutputStream.WriteAsync(body);
        response.OutputStream.Close();
    }

    internal static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
