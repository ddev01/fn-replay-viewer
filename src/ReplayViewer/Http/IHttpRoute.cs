using System.Net;

namespace ReplayViewer;

public interface IHttpRoute
{
    bool Matches(HttpListenerRequest request);

    Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken);
}
