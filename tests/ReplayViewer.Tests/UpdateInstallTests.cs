namespace ReplayViewer.Tests;

public sealed class UpdateInstallTests
{
    [Fact]
    public void The_installed_app_is_relaunched_even_when_the_install_step_fails()
    {
        const string payload = @"C:\p\ReplayViewer.exe";
        var line = UpdateInstaller.BuildCommandLine(payload, @"C:\i\ReplayViewer.exe");

        // Waits for the install, then starts the installed exe unconditionally ("&", not "&&").
        Assert.Contains($"start \"\" /wait \"{payload}\" --install & start", line, StringComparison.Ordinal);
        Assert.DoesNotContain("&&", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_payload_whose_version_matches_the_release_is_accepted()
    {
        UpdateChecker.VerifyPayloadVersion(CoreAssemblyPath(), AppConfig.Version);
    }

    [Fact]
    public void A_payload_older_than_the_release_tag_is_refused()
    {
        Assert.Throws<InvalidDataException>(() =>
            UpdateChecker.VerifyPayloadVersion(CoreAssemblyPath(), "9.9.9")
        );
    }

    private static string CoreAssemblyPath() => typeof(MatchAssembler).Assembly.Location;
}
