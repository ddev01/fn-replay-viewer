using System.Runtime.InteropServices;
using ReplayViewer.Core;

namespace ReplayViewer;

internal static class InstallLayout
{
    public static string StartMenuShortcutPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs",
            "Replay Viewer.lnk"
        );

    public static void CopyToInstallDir(string sourceDirectory, AppPaths paths)
    {
        paths.EnsureDirectories();
        var sourceRoot = Path.GetFullPath(sourceDirectory);
        var destRoot = Path.GetFullPath(paths.RootDirectory);
        if (string.Equals(sourceRoot, destRoot, StringComparison.OrdinalIgnoreCase))
        {
            CreateStartMenuShortcut(paths.InstalledExePath);
            return;
        }

        foreach (
            var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)
        )
        {
            if (file.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = Path.GetRelativePath(sourceDirectory, file);
            var dest = Path.Combine(paths.RootDirectory, relative);
            var destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.Copy(file, dest, overwrite: true);
        }

        RemoveStalePayload(sourceRoot, destRoot);
        CreateStartMenuShortcut(paths.InstalledExePath);
    }

    private static readonly HashSet<string> PreservedDirectoryNames =
        new(StringComparer.OrdinalIgnoreCase) { "logs", "skins" };

    private static void RemoveStalePayload(string sourceRoot, string destRoot)
    {
        foreach (var file in Directory.EnumerateFiles(destRoot))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith("unins", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (File.Exists(Path.Combine(sourceRoot, name)))
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
            }
        }

        foreach (var dir in Directory.EnumerateDirectories(destRoot))
        {
            var name = Path.GetFileName(dir);
            if (PreservedDirectoryNames.Contains(name))
            {
                continue;
            }

            if (Directory.Exists(Path.Combine(sourceRoot, name)))
            {
                continue;
            }

            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    public static void RegisterStartup(AppPaths paths) => LogonTask.Register(paths.InstalledExePath);

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
