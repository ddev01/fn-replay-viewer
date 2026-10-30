namespace ReplayViewer.Tests;

public class AppVersionTests
{
    [Fact]
    public void Tagged_release_newer_than_assembly()
    {
        Assert.True(AppVersion.IsNewer("v1.0.1", "1.0.0"));
        Assert.False(AppVersion.IsNewer("1.0.0", "1.0.0"));
        Assert.False(AppVersion.IsNewer("nope", "1.0.0"));
    }
}
