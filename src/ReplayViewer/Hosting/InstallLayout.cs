using System.Runtime.InteropServices;
using Microsoft.Win32;
using ReplayViewer.Core;

namespace ReplayViewer;

internal static class InstallLayout
{
    private static readonly string[] PreservedDirectoryNames = ["logs"];
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "ReplayViewer";

    public static string StartMenuShortcutPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs",
            "Replay Viewer.lnk"
        );

    public static void CopyToInstallDir(string sourceDirectory, AppPaths paths)
    {
        paths.EnsureDirectories();
        InstallPayload.Sync(
            sourceDirectory,
            paths.RootDirectory,
            PreservedDirectoryNames,
            FileLog.Info
        );
        CreateStartMenuShortcut(paths.InstalledExePath);
    }

    public static bool IsStartupRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public static void SetStartup(bool enabled)
    {
        if (enabled)
        {
            RegisterStartup(AppPaths.Default);
        }
        else
        {
            RemoveStartup();
        }
    }

    public static void RegisterStartup(AppPaths paths)
    {
        LogonTask.Remove();
        using var key =
            Registry.CurrentUser.CreateSubKey(RunKeyPath)
            ?? throw new InvalidOperationException("Could not open the current-user Run key.");
        key.SetValue(RunValueName, $"\"{paths.InstalledExePath}\"");
    }

    public static void RemoveStartup()
    {
        LogonTask.Remove();
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(RunValueName, throwOnMissingValue: false);
    }

    public static void CreateStartMenuShortcut(string targetExePath)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell");
        if (type is null)
        {
            return;
        }

        dynamic shell = Activator.CreateInstance(type)!;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StartMenuShortcutPath)!);
            dynamic shortcut = shell.CreateShortcut(StartMenuShortcutPath);
            shortcut.TargetPath = targetExePath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetExePath) ?? "";
            shortcut.IconLocation = targetExePath + ",0";
            shortcut.Description = "Replay Viewer";
            shortcut.Save();
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    public static void RemoveStartMenuShortcut()
    {
        try
        {
            if (File.Exists(StartMenuShortcutPath))
            {
                File.Delete(StartMenuShortcutPath);
            }
        }
        catch (IOException)
        {
        }
    }
}
