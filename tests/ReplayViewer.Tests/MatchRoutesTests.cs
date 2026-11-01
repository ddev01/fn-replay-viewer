using System.Net;
using System.Reflection;
using System.Text;

namespace ReplayViewer.Tests;

public sealed class MatchRoutesTests : IAsyncLifetime
{
    private static readonly MatchDocument Match = new(
        "demo.replay",
        DateTimeOffset.UnixEpoch,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        [],
        []
    );

    private readonly FakeScraper _scraper = new();
    private LoopbackRoute _server = null!;

    public async ValueTask InitializeAsync()
    {
        var routes = new MatchRoutes(_scraper);
        _server = await LoopbackRoute.StartAsync(
            async (context, cancellationToken) =>
            {
                await routes.HandleAsync(context, cancellationToken);
                return true;
            }
        );
    }

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task Post_scrape_without_a_json_content_type_is_415()
    {
        using var response = await Post("hello", "text/plain");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(0, _scraper.Calls);
    }

    [Fact]
    public async Task Post_scrape_body_over_64_kb_is_413()
    {
        using var response = await Post(new string(' ', 65 * 1024), "application/json");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, _scraper.Calls);
    }

    [Fact]
    public async Task Post_scrape_with_invalid_json_is_400()
    {
        using var response = await Post("{", "application/json");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _scraper.Calls);
    }

    [Fact]
    public async Task Get_match_before_any_scrape_is_404()
    {
        ResetLastMatch();

        using var response = await _server.Client.GetAsync(
            _server.Url("/api/match"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_scrape_returns_the_match_and_get_match_serves_it_afterwards()
    {
        ResetLastMatch();

        using var posted = await Post("""{"path":"demo.replay"}""", "application/json");
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        Assert.Equal("demo.replay", _scraper.LastPath);

        using var fetched = await _server.Client.GetAsync(
            _server.Url("/api/match"),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Contains(
            "demo.replay",
            await fetched.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task Post_scrape_maps_a_replay_that_is_still_writing_to_409()
    {
        _scraper.OnScrape = () => throw new ReplayStillWritingException();

        using var response = await Post("{}", "application/json");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_scrape_maps_a_live_replay_to_422()
    {
        _scraper.OnScrape = () => throw new LiveReplayException();

        using var response = await Post("{}", "application/json");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_scrape_maps_a_timeout_to_504()
    {
        _scraper.OnScrape = () => throw new TimeoutException("slow");

        using var response = await Post("{}", "application/json");

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
    }

    [Fact]
    public async Task Post_scrape_maps_an_unexpected_failure_to_500()
    {
        _scraper.OnScrape = () => throw new InvalidOperationException("boom");

        using var response = await Post("{}", "application/json");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    private async Task<HttpResponseMessage> Post(string body, string contentType)
    {
        using var content = new StringContent(body, Encoding.UTF8, contentType);
        return await _server.Client.PostAsync(
            _server.Url("/api/scrape"),
            content,
            TestContext.Current.CancellationToken
        );
    }

    // shortcut: the last match is a private static cache, so clear it to pin the 404 contract.
    private static void ResetLastMatch() =>
        typeof(MatchRoutes)
            .GetField("s_last", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, null);

    private sealed class FakeScraper : IMatchScraper
    {
        public Func<MatchDocument> OnScrape { get; set; } = () => Match;

        public int Calls { get; private set; }

        public string? LastPath { get; private set; }

        public IReadOnlyList<DemoListItem> ListRecent(int take = 20) => [];

        public Task<MatchDocument> ScrapeAsync(
            string? replayPath,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            LastPath = replayPath;
            return Task.FromResult(OnScrape());
        }
    }
}
