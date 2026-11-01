using System.Net;

namespace ReplayViewer.Tests;

public sealed class StaticFilesTests : IAsyncLifetime
{
    private const string Secret = "outside-the-root";
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "ReplayViewerTests",
        Guid.NewGuid().ToString("N")
    );
    private LoopbackRoute _server = null!;

    private string OutsideRoot => Path.GetDirectoryName(_root)!;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "index.html"), "<h1>index</h1>");
        File.WriteAllText(Path.Combine(_root, "app.js"), "console.log(1);");
        File.WriteAllText(Path.Combine(_root, "style.css"), "body{}");
        File.WriteAllText(Path.Combine(_root, "icon.svg"), "<svg/>");
        File.WriteAllBytes(Path.Combine(_root, "favicon.ico"), [0, 0, 1, 0]);
        File.WriteAllText(Path.Combine(_root, "data.bin"), "raw");
        File.WriteAllText(Path.Combine(_root, "..hidden.txt"), Secret);
        File.WriteAllText(Path.Combine(OutsideRoot, "outside.txt"), Secret);

        var files = new StaticFiles(_root);
        _server = await LoopbackRoute.StartAsync(
            (context, _) => Task.FromResult(files.TryServe(context.Request, context.Response))
        );
    }

    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync();
        Directory.Delete(_root, recursive: true);
        File.Delete(Path.Combine(OutsideRoot, "outside.txt"));
    }

    private string OutsideRoot => Path.GetDirectoryName(_root)!;

    [Fact]
    public async Task Root_serves_index_html()
    {
        using var response = await Get("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.Contains(
            "index",
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task Index_html_is_served_directly()
    {
        using var response = await Get("/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString());
    }

    [Theory]
    [InlineData("/app.js", "text/javascript; charset=utf-8")]
    [InlineData("/style.css", "text/css; charset=utf-8")]
    [InlineData("/icon.svg", "image/svg+xml")]
    [InlineData("/favicon.ico", "image/x-icon")]
    [InlineData("/data.bin", "application/octet-stream")]
    public async Task Content_type_follows_the_extension(string path, string expected)
    {
        using var response = await Get(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, response.Content.Headers.ContentType?.ToString());
    }

    [Fact]
    public async Task Dot_dot_in_a_file_name_is_refused()
    {
        // The guard rejects any name containing "..", so this existing file must not be served.
        using var response = await Get("/..hidden.txt");

        Assert.Equal(LoopbackRoute.UnhandledStatus, (int)response.StatusCode);
    }

    [Fact]
    public async Task Encoded_traversal_is_not_served()
    {
        using var response = await RawGet("/sub/%2e%2e/outside.txt");

        await AssertNotServed(response);
    }

    [Fact]
    public async Task Path_resolving_outside_the_root_is_not_served()
    {
        using var response = await RawGet("/../outside.txt");

        await AssertNotServed(response);
    }

    [Fact]
    public async Task Non_get_is_refused()
    {
        using var content = new StringContent("x");
        using var response = await _server.Client.PostAsync(
            _server.Url("/index.html"),
            content,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(LoopbackRoute.UnhandledStatus, (int)response.StatusCode);
    }

    [Fact]
    public async Task Missing_file_is_refused()
    {
        using var response = await Get("/missing.css");

        Assert.Equal(LoopbackRoute.UnhandledStatus, (int)response.StatusCode);
    }

    private Task<HttpResponseMessage> Get(string path) =>
        _server.Client.GetAsync(_server.Url(path), TestContext.Current.CancellationToken);

    private async Task<HttpResponseMessage> RawGet(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _server.RawUrl(path));
        return await _server.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task AssertNotServed(HttpResponseMessage response)
    {
        // http.sys may reject a traversal path before the handler sees it; either way nothing is served.
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(
            Secret,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );
    }
}
