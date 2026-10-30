using System.Net;
using ReplayViewer.Core;

namespace ReplayViewer;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(a => string.Equals(a, "--install", StringComparison.OrdinalIgnoreCase)))
        {
            var paths = AppPaths.Default;
            InstallLayout.CopyToInstallDir(AppContext.BaseDirectory, paths);
            InstallLayout.RegisterStartup(paths);
            FileLog.Info($"installed to {paths.InstalledExePath}");
            return 0;
        }

        if (args.Any(a => string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            LogonTask.Remove();
            FileLog.Info("removed Replay Viewer logon task");
            return 0;
        }

        using var instance = SingleInstance.TryAcquire();
        if (instance is null)
        {
            SingleInstance.OpenBrowser();
            return 0;
        }

        AppPaths.Default.EnsureDirectories();
        ApplicationConfiguration.Initialize();
        var scraper = new MatchScraper();
        var skins = new SkinCache();
        IHttpRoute[] routes = [new SkinRoutes(skins), new MatchRoutes(scraper)];
        var host = new ListenerHost(routes);
        try
        {
            host.Start();
        }
        catch (HttpListenerException ex)
        {
            FileLog.Error("failed to bind loopback listener", ex);
            MessageBox.Show(
                $"Could not listen on {AppPaths.ListenPrefix}\n{ex.Message}",
                "Replay Viewer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
            host.Dispose();
            return 1;
        }

        Application.Run(new TrayApp(host));
        return 0;
    }
}
