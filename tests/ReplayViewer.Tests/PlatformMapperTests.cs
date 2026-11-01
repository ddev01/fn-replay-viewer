namespace ReplayViewer.Tests;

public class PlatformMapperTests
{
    [Theory]
    [InlineData("WIN", PlatformMapper.Epic)]
    [InlineData("windows", PlatformMapper.Epic)]
    [InlineData("PC", PlatformMapper.Epic)]
    [InlineData("epic", PlatformMapper.Epic)]
    [InlineData("PSN", PlatformMapper.Psn)]
    [InlineData("PS4", PlatformMapper.Psn)]
    [InlineData("ps5", PlatformMapper.Psn)]
    [InlineData("PS4PRO", PlatformMapper.Psn)]
    [InlineData("PLAYSTATION", PlatformMapper.Psn)]
    [InlineData("XBL", PlatformMapper.Xbl)]
    [InlineData("XBOX", PlatformMapper.Xbl)]
    [InlineData("XB1", PlatformMapper.Xbl)]
    [InlineData("XSX", PlatformMapper.Xbl)]
    [InlineData("XSS", PlatformMapper.Xbl)]
    [InlineData("XBOXONE", PlatformMapper.Xbl)]
    [InlineData("XBOXSERIES", PlatformMapper.Xbl)]
    [InlineData("SWT", PlatformMapper.Nintendo)]
    [InlineData("SWITCH", PlatformMapper.Nintendo)]
    [InlineData("NSW", PlatformMapper.Nintendo)]
    [InlineData("NINTENDO", PlatformMapper.Nintendo)]
    [InlineData("IOS", PlatformMapper.Ios)]
    [InlineData("IPHONE", PlatformMapper.Ios)]
    [InlineData("IPAD", PlatformMapper.Ios)]
    [InlineData("MAC", PlatformMapper.Ios)]
    [InlineData("APPLE", PlatformMapper.Ios)]
    [InlineData("AND", PlatformMapper.Android)]
    [InlineData("ANDROID", PlatformMapper.Android)]
    [InlineData("  ps5  ", PlatformMapper.Psn)]
    [InlineData("  ", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("STEAM", null)]
    public void Raw_platform_maps_to_a_name_fn_family(string? raw, string? expected) =>
        Assert.Equal(expected, PlatformMapper.ToNameFn(raw));

    [Theory]
    [InlineData("WIN", "PC")]
    [InlineData("PS5", "PlayStation")]
    [InlineData("XB1", "Xbox")]
    [InlineData("SWT", "Switch")]
    [InlineData("IOS", "iOS")]
    [InlineData("AND", "Android")]
    [InlineData("  ", null)]
    [InlineData("STEAM", null)]
    public void Display_label_follows_the_family(string? raw, string? expected) =>
        Assert.Equal(expected, PlatformMapper.ToDisplay(raw));
}
