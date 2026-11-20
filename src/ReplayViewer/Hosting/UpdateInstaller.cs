using System.Diagnostics;

namespace ReplayViewer;

/// <summary>
/// Runs the payload's --install and relaunches the installed exe afterwards. The delay lets the
/// current process exit so the payload can replace the exe it is running from.
/// The installed exe is relaunched whether or not the install succeeded: a failed update must not
/// leave the user with no app (the next update check stages and retries it). "start /wait" is
/// needed because cmd does not otherwise wait for a GUI-subsystem exe.
/// </summary>
internal static class UpdateInstaller
{
    internal static string BuildCommandLine(string payloadExe, string installedExe) =>
        "/c ping -n 4 127.0.0.1 >nul & start \"\" /wait \""
        + payloadExe
        + "\" --install & start \"\" \""
        + installedExe
        + "\"";

    public static void Apply(string payloadExe, string installedExe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadExe);
        ArgumentException.ThrowIfNullOrWhiteSpace(installedExe);
        FileLog.Info($"applying update from {payloadExe}");
        Process.Start(
            new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = BuildCommandLine(payloadExe, installedExe),
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        );
    }
}
