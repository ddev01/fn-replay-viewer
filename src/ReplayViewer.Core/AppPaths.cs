namespace ReplayViewer.Core;

public sealed class AppPaths
{
    public const string ProductFolderName = "ReplayViewer";
    public const string ExeFileName = "ReplayViewer.exe";
    public const string MutexName = @"Local\ReplayViewer.SingleInstance";
    public const string ListenPrefix = "http://127.0.0.1:47231/";

    public AppPaths(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        RootDirectory = rootDirectory;
    }

    public string RootDirectory { get; }

    public string LogsDirectory => Path.Combine(RootDirectory, "logs");

    public string InstalledExePath => Path.Combine(RootDirectory, ExeFileName);

    public static AppPaths Default { get; } =
        new(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ProductFolderName
            )
        );

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
