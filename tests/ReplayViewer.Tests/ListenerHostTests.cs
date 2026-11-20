using System.Net;
using System.Text;

namespace ReplayViewer.Tests;

public sealed class ListenerHostTests
{
    [Fact]
    public async Task Responses_carry_the_security_headers()
    {
        var (host, baseUri) = StartWithFreePort([new StubRoute(_ => true, WriteBody)]);
        using var _ = host;
        using var client = new HttpClient();

        using var response = await client.GetAsync(
            new Uri(baseUri, "/anything"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "nosniff",
            response.Headers.GetValues("X-Content-Type-Options").Single()
        );
        Assert.Contains(
            "default-src 'self'",
            response.Headers.GetValues("Content-Security-Policy").Single(),
            StringComparison.Ordinal
        );
        Assert.Contains(
            "frame-ancestors 'none'",
            response.Headers.GetValues("Content-Security-Policy").Single(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task A_request_no_route_matches_is_404()
    {
        var (host, baseUri) = StartWithFreePort([new StubRoute(_ => false, WriteBody)]);
        using var _ = host;
        using var client = new HttpClient();

        using var response = await client.GetAsync(
            new Uri(baseUri, "/nothing-here"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_route_that_throws_becomes_500_and_the_listener_keeps_serving()
    {
        var routes = new IHttpRoute[]
        {
            new StubRoute(
                request => request.Url!.AbsolutePath == "/explodes",
                (_, _) => throw new InvalidOperationException("boom")
            ),
            new StubRoute(request => request.Url!.AbsolutePath == "/after", WriteBody),
        };
        var (host, baseUri) = StartWithFreePort(routes);
        using var _ = host;
        using var client = new HttpClient();

        using var failed = await client.GetAsync(
            new Uri(baseUri, "/explodes"),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);

        // One bad request must not take the listener down.
        using var after = await client.GetAsync(
            new Uri(baseUri, "/after"),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task A_foreign_host_header_is_refused_so_dns_rebinding_gets_nothing()
    {
        var (host, baseUri) = StartWithFreePort([new StubRoute(_ => true, WriteBody)]);
        using var _ = host;
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "/api/match"));
        request.Headers.Host = $"evil.example:{baseUri.Port}";

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(421, (int)response.StatusCode);
    }

    [Fact]
    public async Task The_localhost_name_is_accepted()
    {
        var (host, baseUri) = StartWithFreePort([new StubRoute(_ => true, WriteBody)]);
        using var _ = host;
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "/api/match"));
        request.Headers.Host = $"localhost:{baseUri.Port}";

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("Sec-Fetch-Site", "cross-site", 403)]
    [InlineData("Sec-Fetch-Site", "same-site", 403)]
    [InlineData("Sec-Fetch-Site", "same-origin", 200)]
    [InlineData("Sec-Fetch-Site", "none", 200)]
    [InlineData("Origin", "https://evil.example", 403)]
    public async Task Api_calls_made_for_another_site_are_refused(string header, string value, int expected)
    {
        var (host, baseUri) = StartWithFreePort([new StubRoute(_ => true, WriteBody)]);
        using var _ = host;
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "/api/update/apply"));
        request.Headers.TryAddWithoutValidation(header, value);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(expected, (int)response.StatusCode);
    }

    [Fact]
    public async Task Our_own_origin_is_allowed_and_api_responses_are_not_cacheable()
    {
        var (host, baseUri) = StartWithFreePort([new StubRoute(_ => true, WriteBody)]);
        using var _ = host;
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "/api/scrape"));
        request.Headers.TryAddWithoutValidation("Origin", $"http://127.0.0.1:{baseUri.Port}");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("same-origin", response.Headers.GetValues("Cross-Origin-Resource-Policy").Single());
    }

    private static async Task WriteBody(
        HttpListenerContext context,
        CancellationToken cancellationToken
    )
    {
        var body = Encoding.UTF8.GetBytes("ok");
        context.Response.ContentType = "text/plain";
        context.Response.ContentLength64 = body.Length;
        await context.Response.OutputStream.WriteAsync(body, cancellationToken);
        context.Response.OutputStream.Close();
    }

    private static (ListenerHost Host, Uri BaseUri) StartWithFreePort(
        IReadOnlyList<IHttpRoute> routes
    )
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var port = LoopbackRoute.FreePort();
            var host = new ListenerHost(routes, $"http://127.0.0.1:{port}/");
            try
            {
                host.Start();
                return (host, new Uri($"http://127.0.0.1:{port}/"));
            }
            catch (HttpListenerException)
            {
                // The probed port was taken between the probe and the bind; try the next one.
                host.Dispose();
            }
        }

        throw new InvalidOperationException("Could not bind a loopback HttpListener.");
    }

    private sealed class StubRoute(
        Func<HttpListenerRequest, bool> matches,
        Func<HttpListenerContext, CancellationToken, Task> handle
    ) : IHttpRoute
    {
        public bool Matches(HttpListenerRequest request) => matches(request);

        public Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken) =>
            handle(context, cancellationToken);
    }
}
