using ReplayViewer.Core;

namespace ReplayViewer;

internal static class InstallLayout
{
    public static void CopyToInstallDir(string sourceDirectory, AppPaths paths)
    {
        paths.EnsureDirectories();
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            File.Copy(file, Path.Combine(paths.RootDirectory, name), overwrite: true);
        }

        var www = Path.Combine(sourceDirectory, "wwwroot");
        if (Directory.Exists(www))
        {
            CopyDirectory(www, Path.Combine(paths.RootDirectory, "wwwroot"));
        }
    }

    public static void RegisterStartup(AppPaths paths) => LogonTask.Register(paths.InstalledExePath);

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }
    }
}
