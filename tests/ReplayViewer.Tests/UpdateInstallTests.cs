namespace ReplayViewer.Tests;

public sealed class UpdateInstallTests
{
    [Fact]
    public void The_payload_is_told_to_wait_for_this_process_and_to_relaunch_the_app()
    {
        var args = UpdateInstaller.BuildArguments(1234);

        Assert.Equal(["--install", "--relaunch", "--wait-pid", "1234"], args);
        Assert.Equal(1234, UpdateInstaller.WaitPid(args));
    }

    [Theory]
    [InlineData("--install")]
    [InlineData("--install", "--wait-pid")]
    [InlineData("--install", "--wait-pid", "abc")]
    [InlineData("--install", "--wait-pid", "-5")]
    public void A_missing_or_malformed_wait_pid_is_ignored(params string[] args)
    {
        Assert.Null(UpdateInstaller.WaitPid(args));
    }

    [Fact]
    public void Waiting_for_a_process_that_is_already_gone_returns_at_once()
    {
        UpdateInstaller.WaitForExit(int.MaxValue, TimeSpan.FromSeconds(30));
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
