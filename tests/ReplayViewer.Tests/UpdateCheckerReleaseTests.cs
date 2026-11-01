using System.Text.Json;

namespace ReplayViewer.Tests;

public sealed class UpdateCheckerReleaseTests
{
    [Fact]
    public void Picks_the_zip_asset_and_trims_the_tag_prefix()
    {
        var release = Release(
            "v2.0.0",
            """
            {"name":"notes.txt","browser_download_url":"https://example.test/notes.txt"},
            {"name":"ReplayViewer-win-x64-2.0.0.zip","browser_download_url":"https://example.test/app.zip","digest":"sha256:abc"}
            """
        );

        var available = UpdateChecker.ParseRelease(release, "1.0.6");

        Assert.NotNull(available);
        Assert.Equal("2.0.0", available.Version);
        Assert.Equal("https://example.test/app.zip", available.ZipUrl);
        Assert.Equal("sha256:abc", available.Digest);
    }

    [Fact]
    public void Same_version_is_not_an_update()
    {
        var release = Release(
            "v1.0.6",
            """{"name":"a.zip","browser_download_url":"https://example.test/a.zip"}"""
        );

        Assert.Null(UpdateChecker.ParseRelease(release, "1.0.6"));
    }

    [Fact]
    public void Older_version_is_not_an_update()
    {
        var release = Release(
            "v1.0.5",
            """{"name":"a.zip","browser_download_url":"https://example.test/a.zip"}"""
        );

        Assert.Null(UpdateChecker.ParseRelease(release, "1.0.6"));
    }

    [Fact]
    public void A_release_without_assets_throws()
    {
        var release = Release("v2.0.0", null);

        Assert.Throws<InvalidOperationException>(() =>
            UpdateChecker.ParseRelease(release, "1.0.6")
        );
    }

    [Fact]
    public void A_release_without_a_zip_throws()
    {
        var release = Release(
            "v2.0.0",
            """{"name":"notes.txt","browser_download_url":"https://example.test/notes.txt"}"""
        );

        Assert.Throws<InvalidOperationException>(() =>
            UpdateChecker.ParseRelease(release, "1.0.6")
        );
    }

    [Fact]
    public void A_zip_without_a_download_url_is_skipped()
    {
        var release = Release(
            "v2.0.0",
            """
            {"name":"broken.zip"},
            {"name":"good.zip","browser_download_url":"https://example.test/good.zip"}
            """
        );

        var available = UpdateChecker.ParseRelease(release, "1.0.6");

        Assert.NotNull(available);
        Assert.Equal("https://example.test/good.zip", available.ZipUrl);
    }

    [Fact]
    public void A_missing_digest_is_reported_as_null()
    {
        var release = Release(
            "v2.0.0",
            """{"name":"a.zip","browser_download_url":"https://example.test/a.zip"}"""
        );

        var available = UpdateChecker.ParseRelease(release, "1.0.6");

        Assert.NotNull(available);
        Assert.Null(available.Digest);
    }

    private static JsonElement Release(string tag, string? assets)
    {
        var json = assets is null
            ? $$"""{"tag_name":"{{tag}}"}"""
            : $$"""{"tag_name":"{{tag}}","assets":[{{assets}}]}""";
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
