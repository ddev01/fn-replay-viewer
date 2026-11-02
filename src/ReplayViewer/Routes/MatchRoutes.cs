using System.Net;
using System.Text.Json;
using ReplayViewer.Core;

namespace ReplayViewer;

internal sealed class MatchRoutes : IHttpRoute
{
    private const int MaxBodyBytes = 64 * 1024;
    private static readonly SemaphoreSlim ScrapeGate = new(1, 1);
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
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase);
    }

    public async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = context.Response;
        var path = request.Url?.AbsolutePath ?? "/";
        var method = request.HttpMethod;

        try
        {
            if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) && path == "/api/config")
            {
                await HttpJson
                    .WriteJsonAsync(response, new { nameFnPublicUrl = AppConfig.NameFnPublicUrl })
                    .ConfigureAwait(false);
                return;
            }

            if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) && path == "/api/update")
            {
                var staged = UpdateChecker.TryGetPending(out var stagedVersion, out _);
                await HttpJson
                    .WriteJsonAsync(
                        response,
                        new
                        {
                            available = UpdateChecker.AvailableVersion,
                            staged,
                            stagedVersion = staged ? stagedVersion : null,
                        }
                    )
                    .ConfigureAwait(false);
                return;
            }

            if (
                string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase)
                && path == "/api/update/apply"
            )
            {
                if (!UpdateChecker.TryGetPending(out var pendingVersion, out var pendingExe))
                {
                    await HttpJson
                        .WriteErrorAsync(response, 409, "No update is ready.")
                        .ConfigureAwait(false);
                    return;
                }

                UpdateChecker.MarkApplied();
                UpdateInstaller.Apply(pendingExe, AppPaths.Default.InstalledExePath);
                await HttpJson
                    .WriteJsonAsync(response, new { applying = pendingVersion })
                    .ConfigureAwait(false);
                // Let the response reach the browser before the process goes away; the
                // installer chain relaunches the new version.
                _ = Task.Run(
                    async () =>
                    {
                        await Task.Delay(800).ConfigureAwait(false);
                        Environment.Exit(0);
                    },
                    CancellationToken.None
                );
                return;
            }

            if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) && path == "/api/demos")
            {
                await HttpJson.WriteJsonAsync(response, _scraper.ListRecent()).ConfigureAwait(false);
                return;
            }

            if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) && path == "/api/scrape")
            {
                var contentType = request.ContentType ?? "";
                if (
                    !contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)
                )
                {
                    await HttpJson
                        .WriteErrorAsync(response, 415, "Expected JSON.")
                        .ConfigureAwait(false);
                    return;
                }

                var body = await ReadBodyAsync(request, cancellationToken).ConfigureAwait(false);
                if (body is null)
                {
                    await HttpJson
                        .WriteErrorAsync(response, 413, "Request body is too large.")
                        .ConfigureAwait(false);
                    return;
                }

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

                // One scrape at a time: each copies the whole replay and starts a parser, and the
                // last response to finish would win the roster anyway.
                if (!await ScrapeGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
                {
                    await HttpJson
                        .WriteErrorAsync(response, 409, "A scrape is already running.")
                        .ConfigureAwait(false);
                    return;
                }

                MatchDocument match;
                try
                {
                    match = await _scraper
                        .ScrapeAsync(replayPath, cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    ScrapeGate.Release();
                }

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
        catch (UnreadableReplayException ex)
        {
            await HttpJson.WriteErrorAsync(response, 422, ex.Message).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            await HttpJson.WriteErrorAsync(response, 400, ex.Message).ConfigureAwait(false);
        }
        catch (FileNotFoundException ex)
        {
            await HttpJson.WriteErrorAsync(response, 404, ex.Message).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            await HttpJson.WriteErrorAsync(response, 504, ex.Message).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The client went away; nothing to report and nowhere to send it.
        }
        catch (JsonException)
        {
            await HttpJson.WriteErrorAsync(response, 400, "Invalid JSON.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            FileLog.Error("scrape failed", ex);
            await HttpJson.WriteErrorAsync(response, 500, "Scrape failed.").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Fills <paramref name="buffer"/> from <paramref name="reader"/> until the stream ends or the
    /// buffer is full. A single ReadAsync returns only what has arrived, not the whole body.
    /// </summary>
    internal static async Task<int> ReadAllAsync(
        StreamReader reader,
        char[] buffer,
        CancellationToken cancellationToken
    )
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(total), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static async Task<string?> ReadBodyAsync(
        HttpListenerRequest request,
        CancellationToken cancellationToken
    )
    {
        if (!request.HasEntityBody)
        {
            return "";
        }

        if (request.ContentLength64 > MaxBodyBytes)
        {
            return null;
        }

        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        var buffer = new char[MaxBodyBytes + 1];
        var total = await ReadAllAsync(reader, buffer, cancellationToken).ConfigureAwait(false);
        if (total > MaxBodyBytes)
        {
            return null;
        }

        return new string(buffer, 0, total);
    }
}
