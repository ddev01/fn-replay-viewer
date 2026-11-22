using System.Net;
using System.Text;

namespace ReplayViewer.Tests;

public sealed class LinkingConfigTests
{
    private const string Token = "tok-123";

    [Theory]
    [InlineData("{\"baseUrl\":\"https://ttc.example\",\"token\":\"tok-123\"}", "https://ttc.example")]
    [InlineData("{\"baseUrl\":\"https://ttc.example/\",\"token\":\"tok-123\"}", "https://ttc.example")]
    [InlineData("{\"baseUrl\":\"https://ttc.example/sub/\",\"token\":\"tok-123\"}", "https://ttc.example/sub")]
    [InlineData("{\"baseUrl\":\"http://127.0.0.1:5000\",\"token\":\"tok-123\"}", "http://127.0.0.1:5000")]
    [InlineData("{\"baseUrl\":\"http://localhost:5000\",\"token\":\"tok-123\"}", "http://localhost:5000")]
    public void A_valid_file_is_accepted(string json, string baseUrl)
    {
        var config = LinkingConfig.Parse(json);

        Assert.NotNull(config);
        Assert.Equal(baseUrl, config.BaseUrl);
        Assert.Equal(Token, config.Token);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"baseUrl\":\"https://ttc.example\"}")]
    [InlineData("{\"token\":\"tok-123\"}")]
    [InlineData("{\"baseUrl\":\"https://ttc.example\",\"token\":\"\"}")]
    [InlineData("{\"baseUrl\":\"https://ttc.example\",\"token\":\"two words\"}")]
    [InlineData("{\"baseUrl\":\"https://ttc.example\",\"token\":\"line\\nbreak\"}")]
    [InlineData("{\"baseUrl\":\"https://ttc.example\",\"token\":5}")]
    [InlineData("{\"baseUrl\":\"\",\"token\":\"tok-123\"}")]
    [InlineData("{\"baseUrl\":\"ttc.example\",\"token\":\"tok-123\"}")]
    [InlineData("{\"baseUrl\":\"http://ttc.example\",\"token\":\"tok-123\"}")]
    [InlineData("{\"baseUrl\":\"ftp://ttc.example\",\"token\":\"tok-123\"}")]
    [InlineData("{\"baseUrl\":\"https://user:pw@ttc.example\",\"token\":\"tok-123\"}")]
    [InlineData("{\"baseUrl\":\"https://ttc.example/?x=1\",\"token\":\"tok-123\"}")]
    public void An_invalid_file_counts_as_absent(string json)
    {
        Assert.Null(LinkingConfig.Parse(json));
    }

    [Fact]
    public void A_missing_file_counts_as_absent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"linking-{Guid.NewGuid():N}.json");

        Assert.Null(LinkingConfig.Load(path));
    }

    [Fact]
    public void A_file_on_disk_is_read()
    {
        var path = Path.Combine(Path.GetTempPath(), $"linking-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{\"baseUrl\":\"https://ttc.example\",\"token\":\"tok-123\"}");
        try
        {
            Assert.NotNull(LinkingConfig.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public sealed class LinkingRoutesTests : IDisposable
{
    private const string Token = "tok-123";
    private readonly FakeUpstream _upstream = new();
    private readonly List<IDisposable> _owned = [];

    public void Dispose()
    {
        foreach (var item in _owned)
        {
            item.Dispose();
        }
    }

    [Theory]
    [InlineData("GET", "/api/linking/lookups")]
    [InlineData("POST", "/api/linking/replay")]
    [InlineData("POST", "/api/linking/links")]
    [InlineData("DELETE", "/api/linking/links")]
    [InlineData("POST", "/api/linking/rejections")]
    public async Task With_linking_off_every_route_is_404_and_nothing_goes_out(string method, string path)
    {
        var (uri, _) = Start(enabled: false);

        using var response = await Send(uri, method, path, "{}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(_upstream.Requests);
    }

    [Theory]
    [InlineData("GET", "/api/linking/lookups?since=1790000000", "GET", "/api/viewer/lookups?since=1790000000")]
    [InlineData("POST", "/api/linking/replay", "POST", "/api/viewer/replay")]
    [InlineData("POST", "/api/linking/links", "POST", "/api/viewer/links")]
    [InlineData("DELETE", "/api/linking/links", "DELETE", "/api/viewer/links")]
    [InlineData("POST", "/api/linking/rejections", "POST", "/api/viewer/rejections")]
    public async Task Each_route_forwards_with_the_token_attached(
        string method,
        string path,
        string upstreamMethod,
        string upstreamPath
    )
    {
        _upstream.Reply = (HttpStatusCode.OK, "{\"ok\":true}");
        var (uri, _) = Start();
        var body = method == "GET" ? null : "{\"epic_id\":\"abc\"}";

        using var response = await Send(uri, method, path, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "{\"ok\":true}",
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );
        var seen = Assert.Single(_upstream.Requests);
        Assert.Equal(upstreamMethod, seen.Method);
        Assert.Equal("https://ttc.example" + upstreamPath, seen.Url);
        Assert.Equal(Token, seen.Token);
        Assert.Equal(body, seen.Body);
        // The browser must never be handed the token.
        Assert.DoesNotContain(Token, response.Headers.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_204_passes_through_empty()
    {
        _upstream.Reply = (HttpStatusCode.NoContent, "");
        var (uri, _) = Start();

        using var response = await Send(uri, "DELETE", "/api/linking/links", "{\"epic_id\":\"a\"}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [InlineData("POST", "/api/linking/links", "{", "application/json", 400)]
    [InlineData("POST", "/api/linking/links", "[]", "application/json", 400)]
    [InlineData("POST", "/api/linking/links", "{}", "text/plain", 415)]
    [InlineData("GET", "/api/linking/lookups?since=abc", null, "application/json", 400)]
    [InlineData("GET", "/api/linking/replay", null, "application/json", 404)]
    [InlineData("PUT", "/api/linking/links", "{}", "application/json", 404)]
    public async Task Bad_requests_are_refused_before_anything_goes_out(
        string method,
        string path,
        string? body,
        string contentType,
        int expected
    )
    {
        var (uri, _) = Start();

        using var response = await Send(uri, method, path, body, contentType);

        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Empty(_upstream.Requests);
    }

    [Theory]
    [InlineData(400, "{\"error\":\"bad_request\",\"detail\":\"Not a Twitch URL.\"}", 400, "Not a Twitch URL.")]
    [InlineData(400, "nope", 400, "The linking server rejected the request.")]
    [InlineData(401, "{\"error\":\"unauthorized\"}", 502, "The linking server refused the token.")]
    [InlineData(403, "{\"error\":\"forbidden\"}", 502, "The linking server refused the token.")]
    [InlineData(404, "", 404, "The linking server does not know that.")]
    [InlineData(429, "{\"error\":\"rate_limited\"}", 429, "Too many requests. Try again in a minute.")]
    [InlineData(500, "<html>boom</html>", 502, "The linking server had a problem (500).")]
    [InlineData(302, "", 502, "The linking server had a problem (302).")]
    public async Task Upstream_errors_become_a_plain_error_message(
        int upstreamStatus,
        string upstreamBody,
        int expectedStatus,
        string expectedMessage
    )
    {
        _upstream.Reply = ((HttpStatusCode)upstreamStatus, upstreamBody);
        var (uri, _) = Start();

        using var response = await Send(uri, "POST", "/api/linking/links", "{\"url\":\"x\"}");

        Assert.Equal(expectedStatus, (int)response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal($"{{\"error\":\"{expectedMessage}\"}}", text);
    }

    [Fact]
    public async Task An_unreachable_server_is_502_and_a_slow_one_is_504()
    {
        _upstream.Throw = new HttpRequestException("down");
        var (uri, _) = Start();

        using var down = await Send(uri, "POST", "/api/linking/links", "{}");
        Assert.Equal(HttpStatusCode.BadGateway, down.StatusCode);

        _upstream.Throw = new TaskCanceledException("timeout");
        using var slow = await Send(uri, "POST", "/api/linking/links", "{}");
        Assert.Equal(HttpStatusCode.GatewayTimeout, slow.StatusCode);
    }

    [Fact]
    public async Task A_success_that_is_not_json_is_502()
    {
        _upstream.Reply = (HttpStatusCode.OK, "<html>");
        var (uri, _) = Start();

        using var response = await Send(uri, "POST", "/api/linking/replay", "{}");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Theory]
    [InlineData("Sec-Fetch-Site", "cross-site")]
    [InlineData("Sec-Fetch-Site", "same-site")]
    [InlineData("Origin", "https://evil.example")]
    public async Task Cross_site_calls_are_refused_for_the_new_routes(string header, string value)
    {
        var (uri, _) = Start();
        using var request = Request(uri, "POST", "/api/linking/links", "{}");
        request.Headers.TryAddWithoutValidation(header, value);

        using var response = await new HttpClient().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_upstream.Requests);
    }

    [Fact]
    public async Task A_foreign_host_header_is_refused_for_the_new_routes()
    {
        var (uri, _) = Start();
        using var request = Request(uri, "GET", "/api/linking/lookups", null);
        request.Headers.Host = $"evil.example:{uri.Port}";

        using var response = await new HttpClient().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(421, (int)response.StatusCode);
        Assert.Empty(_upstream.Requests);
    }

    private (Uri Uri, LinkingRoutes Routes) Start(bool enabled = true)
    {
        var config = enabled
            ? LinkingConfig.Parse("{\"baseUrl\":\"https://ttc.example\",\"token\":\"tok-123\"}")
            : null;
        var routes = new LinkingRoutes(config, _upstream);
        _owned.Add(routes);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var port = LoopbackRoute.FreePort();
            var host = new ListenerHost([routes], $"http://127.0.0.1:{port}/");
            try
            {
                host.Start();
                _owned.Add(host);
                return (new Uri($"http://127.0.0.1:{port}/"), routes);
            }
            catch (HttpListenerException)
            {
                host.Dispose();
            }
        }

        throw new InvalidOperationException("Could not bind a loopback HttpListener.");
    }

    private static HttpRequestMessage Request(Uri uri, string method, string path, string? body, string contentType = "application/json")
    {
        var request = new HttpRequestMessage(new HttpMethod(method), new Uri(uri, path));
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, contentType);
        }

        return request;
    }

    private static async Task<HttpResponseMessage> Send(
        Uri uri,
        string method,
        string path,
        string? body,
        string contentType = "application/json"
    )
    {
        using var client = new HttpClient();
        using var request = Request(uri, method, path, body, contentType);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed record Seen(string Method, string Url, string? Token, string? Body);

    private sealed class FakeUpstream : HttpMessageHandler
    {
        public List<Seen> Requests { get; } = [];

        public (HttpStatusCode Status, string Body) Reply { get; set; } = (HttpStatusCode.OK, "{}");

        public Exception? Throw { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(
                new Seen(
                    request.Method.Method,
                    request.RequestUri!.ToString(),
                    request.Headers.TryGetValues("X-Viewer-Token", out var values) ? values.Single() : null,
                    body
                )
            );
            if (Throw is not null)
            {
                throw Throw;
            }

            return new HttpResponseMessage(Reply.Status)
            {
                Content = new StringContent(Reply.Body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
