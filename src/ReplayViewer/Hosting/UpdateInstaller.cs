using System.Diagnostics;
using System.Globalization;

namespace ReplayViewer;

/// <summary>
/// Starts the payload's own <c>--install</c>. The payload waits for this process to exit (so it can
/// replace the exe this process runs from), installs, and then relaunches the installed exe
/// whether or not the install succeeded: a failed update must not leave the user with no app (the
/// next update check stages and retries it).
/// </summary>
internal static class UpdateInstaller
{
    public const string InstallSwitch = "--install";
    public const string RelaunchSwitch = "--relaunch";
    public const string WaitPidSwitch = "--wait-pid";

    public static void Apply(string payloadExe, string installedExe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadExe);
        ArgumentException.ThrowIfNullOrWhiteSpace(installedExe);
        FileLog.Info($"applying update from {payloadExe}");
        var start = new ProcessStartInfo(payloadExe) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in BuildArguments(Environment.ProcessId))
        {
            start.ArgumentList.Add(argument);
        }

        Process.Start(start);
    }

    internal static string[] BuildArguments(int parentProcessId) =>
        [InstallSwitch, RelaunchSwitch, WaitPidSwitch, parentProcessId.ToString(CultureInfo.InvariantCulture)];

    /// <summary>The process id after <c>--wait-pid</c>, or null when it is absent or malformed.</summary>
    internal static int? WaitPid(string[] args)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, WaitPidSwitch, StringComparison.OrdinalIgnoreCase));
        return index >= 0
            && index + 1 < args.Length
            && int.TryParse(args[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
            ? pid
            : null;
    }

    /// <summary>Waits for the app being replaced to exit; a process that is already gone is fine.</summary>
    internal static void WaitForExit(int processId, TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.WaitForExit(timeout);
        }
        catch (ArgumentException)
        {
            // Already exited.
        }
    }
}
