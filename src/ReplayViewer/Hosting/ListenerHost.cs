using System.Net;
using ReplayViewer.Core;

namespace ReplayViewer;

internal sealed class ListenerHost : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly IReadOnlyList<IHttpRoute> _routes;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public ListenerHost(IReadOnlyList<IHttpRoute> routes)
    {
        _routes = routes;
        _listener.Prefixes.Add(AppPaths.ListenPrefix);
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
            _listener.Stop();
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

    private async Task DispatchAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
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
