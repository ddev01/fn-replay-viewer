using System.Text.Json;

namespace ReplayViewer.Tests;

public sealed class UpdatePendingTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "ReplayViewerTests",
        Guid.NewGuid().ToString("N")
    );

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void Nothing_is_pending_on_a_fresh_install()
    {
        Assert.False(UpdateChecker.TryGetPending(_root, out _, out _));
    }

    [Fact]
    public void A_payload_without_its_marker_is_not_pending()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "ReplayViewer.exe"), "exe");

        Assert.False(UpdateChecker.TryGetPending(_root, out _, out _));
    }

    [Fact]
    public void A_marker_without_a_payload_is_not_pending()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "version.txt"), "1.2.3");

        Assert.False(UpdateChecker.TryGetPending(_root, out _, out _));
    }

    [Fact]
    public void A_staged_payload_reports_its_version()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "ReplayViewer.exe"), "exe");
        File.WriteAllText(Path.Combine(_root, "version.txt"), "1.2.3\n");

        Assert.True(UpdateChecker.TryGetPending(_root, out var version, out var exe));
        Assert.Equal("1.2.3", version);
        Assert.Equal(Path.Combine(_root, "ReplayViewer.exe"), exe);
    }

    [Fact]
    public void Clearing_pending_removes_the_folder()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "ReplayViewer.exe"), "exe");
        File.WriteAllText(Path.Combine(_root, "version.txt"), "1.2.3");

        UpdateChecker.ClearPending(_root);

        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void A_release_without_a_signature_is_reported_as_unsigned()
    {
        var release = JsonDocument
            .Parse(
                """
                {"tag_name":"v9.9.9","assets":[
                  {"name":"ReplayViewer-win-x64-9.9.9.zip","browser_download_url":"https://example.test/a.zip","digest":"sha256:abc"}
                ]}
                """
            )
            .RootElement.Clone();

        var available = UpdateChecker.ParseRelease(release, "1.0.0");

        Assert.NotNull(available);
        Assert.Null(available.SignatureUrl);
    }

    [Fact]
    public void A_release_signature_asset_is_picked_up()
    {
        var release = JsonDocument
            .Parse(
                """
                {"tag_name":"v9.9.9","assets":[
                  {"name":"ReplayViewer-win-x64-9.9.9.zip.sig","browser_download_url":"https://example.test/a.zip.sig"},
                  {"name":"ReplayViewer-win-x64-9.9.9.zip","browser_download_url":"https://example.test/a.zip","digest":"sha256:abc"}
                ]}
                """
            )
            .RootElement.Clone();

        var available = UpdateChecker.ParseRelease(release, "1.0.0");

        Assert.NotNull(available);
        Assert.Equal("https://example.test/a.zip.sig", available.SignatureUrl);
    }
}
