using System.Net;
using System.Text.Json;
using ReplayViewer.Core;

namespace ReplayViewer;

internal sealed class MatchRoutes : IHttpRoute
{
    private static MatchDocument? s_last;
    private readonly IMatchScraper _scraper;
    private readonly StaticFiles _files = new();

    public MatchRoutes(IMatchScraper scraper)
    {
        _scraper = scraper;
    }

    public bool Matches(HttpListenerRequest request)
    {
        var path = request.Url?.AbsolutePath ?? "/";
        if (path.StartsWith("/skins/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    public async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = context.Response;
        var path = request.Url?.AbsolutePath ?? "/";
        var method = request.HttpMethod;

        try
        {
            if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) && path == "/api/demos")
            {
                await HttpJson.WriteJsonAsync(response, _scraper.ListRecent()).ConfigureAwait(false);
                return;
            }

            if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) && path == "/api/scrape")
            {
                var body = await ReadBodyAsync(request, cancellationToken).ConfigureAwait(false);
                string? replayPath = null;
                if (!string.IsNullOrWhiteSpace(body))
                {
                    using var doc = JsonDocument.Parse(body);
                    if (
                        doc.RootElement.TryGetProperty("path", out var pathProp)
                        && pathProp.ValueKind == JsonValueKind.String
                    )
                    {
                        replayPath = pathProp.GetString();
                    }
                }

                var match = await _scraper.ScrapeAsync(replayPath, cancellationToken)
                    .ConfigureAwait(false);
                s_last = match;
                await HttpJson.WriteJsonAsync(response, match).ConfigureAwait(false);
                return;
            }

            if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) && path == "/api/match")
            {
                if (s_last is null)
                {
                    response.StatusCode = 404;
                    response.OutputStream.Close();
                    return;
                }

                await HttpJson.WriteJsonAsync(response, s_last).ConfigureAwait(false);
                return;
            }

            if (
                string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
                && path == "/api/match.json"
            )
            {
                if (s_last is null)
                {
                    response.StatusCode = 404;
                    response.OutputStream.Close();
                    return;
                }

                await HttpJson
                    .WriteJsonAsync(response, s_last, fileName: "match.json")
                    .ConfigureAwait(false);
                return;
            }

            if (_files.TryServe(request, response))
            {
                return;
            }

            response.StatusCode = 404;
            response.OutputStream.Close();
        }
        catch (ReplayStillWritingException ex)
        {
            await HttpJson.WriteErrorAsync(response, 409, ex.Message).ConfigureAwait(false);
        }
        catch (LiveReplayException ex)
        {
            await HttpJson.WriteErrorAsync(response, 422, ex.Message).ConfigureAwait(false);
        }
        catch (FileNotFoundException ex)
        {
            await HttpJson.WriteErrorAsync(response, 404, ex.Message).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            await HttpJson.WriteErrorAsync(response, 504, ex.Message).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            await HttpJson.WriteErrorAsync(response, 400, ex.Message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            FileLog.Error("scrape failed", ex);
            await HttpJson.WriteErrorAsync(response, 500, ex.Message).ConfigureAwait(false);
        }
    }

    private static async Task<string> ReadBodyAsync(
        HttpListenerRequest request,
        CancellationToken cancellationToken
    )
    {
        if (!request.HasEntityBody)
        {
            return "";
        }

        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }
}
