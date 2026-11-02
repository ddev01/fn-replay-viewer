using System.Diagnostics;

namespace ReplayViewer;

/// <summary>
/// Runs the payload's --install and relaunches the installed exe afterwards. The delay lets the
/// current process exit so the payload can replace the exe it is running from.
/// </summary>
internal static class UpdateInstaller
{
    public static void Apply(string payloadExe, string installedExe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadExe);
        ArgumentException.ThrowIfNullOrWhiteSpace(installedExe);
        FileLog.Info($"applying update from {payloadExe}");
        Process.Start(
            new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments =
                    "/c ping -n 4 127.0.0.1 >nul & \""
                    + payloadExe
                    + "\" --install && start \"\" \""
                    + installedExe
                    + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        );
    }
}
