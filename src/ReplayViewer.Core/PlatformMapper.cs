namespace ReplayViewer.Core;

public static class PlatformMapper
{
    public const string Epic = "epic";
    public const string Psn = "psn";
    public const string Xbl = "xbl";
    public const string Nintendo = "nintendo";
    public const string Ios = "ios";
    public const string Android = "android";

    public static string? ToNameFn(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var p = raw.Trim().ToUpperInvariant();
        return p switch
        {
            "WIN" or "WINDOWS" or "PC" or "EPIC" => Epic,
            "PSN" or "PS4" or "PS5" or "PS4PRO" or "PLAYSTATION" => Psn,
            "XBL" or "XBOX" or "XB1" or "XSX" or "XSS" or "XBOXONE" or "XBOXSERIES" => Xbl,
            "SWT" or "SWITCH" or "NSW" or "NINTENDO" => Nintendo,
            "IOS" or "IPHONE" or "IPAD" or "MAC" or "APPLE" => Ios,
            "AND" or "ANDROID" => Android,
            _ => null,
        };
    }

    public static string? ToDisplay(string? raw)
    {
        return ToNameFn(raw) switch
        {
            Epic => "PC",
            Psn => "PlayStation",
            Xbl => "Xbox",
            Nintendo => "Switch",
            Ios => "iOS",
            Android => "Android",
            _ => null,
        };
    }
}
