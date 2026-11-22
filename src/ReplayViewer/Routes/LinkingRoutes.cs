using System.Net;
using System.Text;
using System.Text.Json;

namespace ReplayViewer;

/// <summary>
/// Loopback proxy for the owner-only streamer linking. The browser calls these routes and never
/// sees the token: they attach <c>X-Viewer-Token</c> and forward to the contract's
/// <c>/api/viewer/*</c> endpoints. With no <see cref="LinkingConfig"/> every route is a 404 and no
/// <see cref="HttpClient"/> exists, so nothing can go out. The listener's Host / Origin /
/// Sec-Fetch-Site checks already cover every <c>/api/</c> path.
/// </summary>
internal sealed class LinkingRoutes : IHttpRoute, IDisposable
{
    private const string Prefix = "/api/linking/";
    private const int MaxDetailChars = 200;

    private static readonly Dictionary<(string Method, string Path), string> Upstream = new()
    {
        [("GET", "/api/linking/lookups")] = "/api/viewer/lookups",
        [("POST", "/api/linking/replay")] = "/api/viewer/replay",
        [("POST", "/api/linking/links")] = "/api/viewer/links",
        [("DELETE", "/api/linking/links")] = "/api/viewer/links",
        [("POST", "/api/linking/rejections")] = "/api/viewer/rejections",
    };

    private readonly LinkingConfig? _config;
    private readonly HttpClient? _http;

    public LinkingRoutes(LinkingConfig? config, HttpMessageHandler? handler = null)
    {
        _config = config;
        if (config is null)
        {
            return;
        }

        // No redirects: a redirect must not carry the token to another host.
        _http = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(8),
            MaxResponseContentBufferSize = 4 * 1024 * 1024,
        };
    }

    public void Dispose() => _http?.Dispose();

    public bool Matches(HttpListenerRequest request) =>
        request.Url?.AbsolutePath.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) == true;

    public async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = context.Response;
        var key = (request.HttpMethod.ToUpperInvariant(), request.Url!.AbsolutePath.ToLowerInvariant());
        if (_config is null || _http is null || !Upstream.TryGetValue(key, out var target))
        {
            await HttpJson.WriteErrorAsync(response, 404, "Not found.").ConfigureAwait(false);
            return;
        }

        string? body = null;
        if (key.Item1 == "GET")
        {
            var since = request.QueryString["since"];
            if (since is not null)
            {
                if (!long.TryParse(since, out var seconds))
                {
                    await HttpJson.WriteErrorAsync(response, 400, "since must be a unix time.")
                        .ConfigureAwait(false);
                    return;
                }

                target += "?since=" + seconds;
            }
        }
        else
        {
            if (!(request.ContentType ?? "").StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
            {
                await HttpJson.WriteErrorAsync(response, 415, "Expected JSON.").ConfigureAwait(false);
                return;
            }

            body = await MatchRoutes.ReadBodyAsync(request, cancellationToken).ConfigureAwait(false);
            if (body is null)
            {
                await HttpJson.WriteErrorAsync(response, 413, "Request body is too large.")
                    .ConfigureAwait(false);
                return;
            }

            if (!IsJsonObject(body))
            {
                await HttpJson.WriteErrorAsync(response, 400, "Expected a JSON object.")
                    .ConfigureAwait(false);
                return;
            }
        }

        using var upstream = new HttpRequestMessage(new HttpMethod(key.Item1), _config.BaseUrl + target);
        upstream.Headers.Add("X-Viewer-Token", _config.Token);
        if (body is not null)
        {
            upstream.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        try
        {
            using var reply = await _http.SendAsync(upstream, cancellationToken).ConfigureAwait(false);
            var text = await reply.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            await Relay(response, (int)reply.StatusCode, text).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The app is shutting down; nowhere to send anything.
        }
        catch (OperationCanceledException)
        {
            await HttpJson.WriteErrorAsync(response, 504, "The linking server did not answer in time.")
                .ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            await HttpJson.WriteErrorAsync(response, 502, "Could not reach the linking server.")
                .ConfigureAwait(false);
        }
    }

    private static bool IsJsonObject(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Passes a success through; turns every failure into a plain <c>{error}</c>.</summary>
    private static async Task Relay(HttpListenerResponse response, int status, string text)
    {
        if (status is >= 200 and < 300)
        {
            if (text.Length == 0)
            {
                response.StatusCode = status;
                response.OutputStream.Close();
                return;
            }

            try
            {
                using var doc = JsonDocument.Parse(text);
                await HttpJson.WriteJsonAsync(response, doc.RootElement.Clone(), status).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                await HttpJson.WriteErrorAsync(response, 502, "The linking server sent an unexpected reply.")
                    .ConfigureAwait(false);
            }

            return;
        }

        var (code, message) = status switch
        {
            400 => (400, Detail(text) ?? "The linking server rejected the request."),
            401 or 403 => (502, "The linking server refused the token."),
            404 => (404, "The linking server does not know that."),
            429 => (429, "Too many requests. Try again in a minute."),
            _ => (502, $"The linking server had a problem ({status})."),
        };
        await HttpJson.WriteErrorAsync(response, code, message).ConfigureAwait(false);
    }

    /// <summary>The server's short <c>detail</c> text, e.g. why a pasted URL was refused.</summary>
    private static string? Detail(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (
                doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("detail", out var detail)
                && detail.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(detail.GetString())
            )
            {
                var value = detail.GetString()!.Trim();
                return value.Length > MaxDetailChars ? value[..MaxDetailChars] : value;
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }
}
