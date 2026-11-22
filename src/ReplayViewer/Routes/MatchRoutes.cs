using System.Net;
using System.Text.Json;
using ReplayViewer.Core;

namespace ReplayViewer;

internal sealed class MatchRoutes : IHttpRoute
{
    private const int MaxBodyBytes = 64 * 1024;
    private static readonly SemaphoreSlim ScrapeGate = new(1, 1);
    private static readonly object ApplyGate = new();
    private static MatchDocument? s_last;
    private readonly IMatchScraper _scraper;
    private readonly bool _linking;
    private readonly StaticFiles _files = new();
    private readonly Dictionary<(string Method, string Path), Handler> _handlers;

    private delegate Task Handler(HttpListenerContext context, CancellationToken cancellationToken);

    public MatchRoutes(IMatchScraper scraper, bool linking = false)
    {
        _scraper = scraper;
        _linking = linking;
        _handlers = new()
        {
            [("GET", "/api/config")] = Config,
            [("GET", "/api/update")] = UpdateStatus,
            [("POST", "/api/update/apply")] = ApplyUpdate,
            [("GET", "/api/demos")] = Demos,
            [("POST", "/api/demos/delete")] = DeleteDemos,
            [("POST", "/api/scrape")] = Scrape,
            [("GET", "/api/match")] = (context, _) => LastMatch(context, fileName: null),
            [("GET", "/api/match.json")] = (context, _) => LastMatch(context, fileName: "match.json"),
        };
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
        var key = (request.HttpMethod.ToUpperInvariant(), request.Url?.AbsolutePath ?? "/");

        try
        {
            if (_handlers.TryGetValue(key, out var handler))
            {
                await handler(context, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (_files.TryServe(request, response))
            {
                return;
            }

            await HttpJson.WriteErrorAsync(response, 404, "Not found.").ConfigureAwait(false);
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
        catch (InvalidReplayPathException ex)
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
            // The app is shutting down; nothing to report and nowhere to send it.
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

    private Task Config(HttpListenerContext context, CancellationToken cancellationToken) =>
        HttpJson.WriteJsonAsync(
            context.Response,
            new
            {
                nameFnPublicUrl = AppConfig.NameFnPublicUrl,
                version = AppConfig.DisplayVersion,
                dev = AppConfig.IsDev,
                linking = _linking,
            }
        );

    private static Task UpdateStatus(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var staged = UpdateChecker.TryGetPending(out var stagedVersion, out _);
        return HttpJson.WriteJsonAsync(
            context.Response,
            new
            {
                available = UpdateChecker.AvailableVersion,
                staged,
                stagedVersion = staged ? stagedVersion : null,
            }
        );
    }

    private static async Task ApplyUpdate(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var response = context.Response;
        string pendingVersion;
        // Check-then-act: two quick POSTs must not both start an installer.
        lock (ApplyGate)
        {
            if (!UpdateChecker.TryGetPending(out pendingVersion, out var pendingExe))
            {
                pendingVersion = "";
            }
            else
            {
                UpdateChecker.MarkApplied();
                UpdateInstaller.Apply(pendingExe, AppPaths.Default.InstalledExePath);
            }
        }

        if (pendingVersion.Length == 0)
        {
            await HttpJson.WriteErrorAsync(response, 409, "No update is ready.").ConfigureAwait(false);
            return;
        }

        await HttpJson.WriteJsonAsync(response, new { applying = pendingVersion }).ConfigureAwait(false);
        // Let the response reach the browser before the process goes away: the payload is waiting
        // for this process to exit before it installs and relaunches.
        _ = Task.Run(
            async () =>
            {
                await Task.Delay(800).ConfigureAwait(false);
                Environment.Exit(0);
            },
            CancellationToken.None
        );
    }

    private async Task Demos(HttpListenerContext context, CancellationToken cancellationToken)
    {
        // The page only asks for the list while it is visible and focused, so this is user
        // activity rather than a background timer. The page polls every two seconds while
        // backfill is true, so read it before starting a pass: a pass that finishes in
        // milliseconds would otherwise always look busy.
        var backfill = _scraper.BackfillRunning;
        _scraper.StartBackfill();
        await HttpJson
            .WriteJsonAsync(context.Response, new { demos = _scraper.ListDemos(), backfill })
            .ConfigureAwait(false);
    }

    private async Task DeleteDemos(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = context.Response;

        if (!(request.ContentType ?? "").StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            await HttpJson.WriteErrorAsync(response, 415, "Expected JSON.").ConfigureAwait(false);
            return;
        }

        var body = await ReadBodyAsync(request, cancellationToken).ConfigureAwait(false);
        if (body is null)
        {
            await HttpJson.WriteErrorAsync(response, 413, "Request body is too large.").ConfigureAwait(false);
            return;
        }

        using var doc = JsonDocument.Parse(body);
        if (
            doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("names", out var namesProp)
            || namesProp.ValueKind != JsonValueKind.Array
        )
        {
            await HttpJson.WriteErrorAsync(response, 400, "Expected {\"names\": [...]}.").ConfigureAwait(false);
            return;
        }

        var names = new List<string>();
        foreach (var item in namesProp.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                await HttpJson.WriteErrorAsync(response, 400, "Names must be strings.").ConfigureAwait(false);
                return;
            }

            names.Add(item.GetString()!);
        }

        // Never delete under a scrape that may be reading the same file.
        await ScrapeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await HttpJson.WriteJsonAsync(response, _scraper.DeleteDemos(names)).ConfigureAwait(false);
        }
        finally
        {
            ScrapeGate.Release();
        }
    }

    private async Task Scrape(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = context.Response;

        var contentType = request.ContentType ?? "";
        if (!contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            await HttpJson.WriteErrorAsync(response, 415, "Expected JSON.").ConfigureAwait(false);
            return;
        }

        var body = await ReadBodyAsync(request, cancellationToken).ConfigureAwait(false);
        if (body is null)
        {
            await HttpJson.WriteErrorAsync(response, 413, "Request body is too large.").ConfigureAwait(false);
            return;
        }

        string? replayPath = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                await HttpJson.WriteErrorAsync(response, 400, "Expected a JSON object.").ConfigureAwait(false);
                return;
            }

            if (
                doc.RootElement.TryGetProperty("path", out var pathProp)
                && pathProp.ValueKind == JsonValueKind.String
            )
            {
                replayPath = pathProp.GetString();
            }
        }

        // One scrape at a time: each starts a parser, and the last response to finish would win
        // the roster anyway.
        if (!await ScrapeGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            await HttpJson.WriteErrorAsync(response, 409, "A scrape is already running.").ConfigureAwait(false);
            return;
        }

        ScrapeResult result;
        try
        {
            result = await _scraper.ScrapeAsync(replayPath, cancellationToken).ConfigureAwait(false);
            // Publish while still holding the gate: after the release a faster scrape could
            // finish first and then be overwritten by this older one.
            s_last = result.Match;
        }
        finally
        {
            ScrapeGate.Release();
        }

        if (result.Cached)
        {
            response.AddHeader("X-Replay-Cached", "1");
        }

        await HttpJson.WriteJsonAsync(response, result.Match).ConfigureAwait(false);
    }

    private static async Task LastMatch(HttpListenerContext context, string? fileName)
    {
        var last = s_last;
        if (last is null)
        {
            await HttpJson.WriteErrorAsync(context.Response, 404, "No match has been opened yet.").ConfigureAwait(false);
            return;
        }

        await HttpJson.WriteJsonAsync(context.Response, last, fileName: fileName).ConfigureAwait(false);
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

    internal static async Task<string?> ReadBodyAsync(
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
