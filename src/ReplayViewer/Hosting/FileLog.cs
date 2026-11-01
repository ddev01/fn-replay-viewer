using ReplayViewer.Core;

namespace ReplayViewer;

internal static class FileLog
{
    private static readonly object Gate = new();
    private const long MaxBytes = 512 * 1024;

    public static string ActivePath => Path.Combine(AppPaths.Default.LogsDirectory, "replay-viewer.log");

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            AppPaths.Default.EnsureDirectories();
            var line = $"{DateTimeOffset.Now:u} {level}: {message}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            line += Environment.NewLine;
            lock (Gate)
            {
                RotateIfNeeded();
                File.AppendAllText(ActivePath, line);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must not crash the host: a locked-down profile, antivirus or a replaced
            // log folder throws here, and the caller is usually already handling a failure.
        }
    }

    private static void RotateIfNeeded()
    {
        var path = ActivePath;
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < MaxBytes)
        {
            return;
        }

        var bak = path + ".1";
        if (File.Exists(bak))
        {
            File.Delete(bak);
        }

        File.Move(path, bak);
    }
}
