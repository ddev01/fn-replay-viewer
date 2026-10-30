namespace ReplayViewer.Core;

public sealed class DemosFolder
{
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FortniteGame",
            "Saved",
            "Demos"
        );
}
