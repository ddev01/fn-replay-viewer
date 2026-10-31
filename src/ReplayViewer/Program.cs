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
            var source =
                Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            InstallLayout.CopyToInstallDir(source, paths);
            InstallLayout.RegisterStartup(paths);
            FileLog.Info($"installed to {paths.InstalledExePath}");
            return 0;
        }

        if (args.Any(a => string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            InstallLayout.RemoveStartup();
            InstallLayout.RemoveStartMenuShortcut();
            FileLog.Info("removed Replay Viewer startup registration");
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
        IHttpRoute[] routes = [new MatchRoutes(scraper)];
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

        var tray = new TrayApp(host);
        _ = UpdateChecker.RunAsync(tray, CancellationToken.None);
        Application.Run(tray);
        return 0;
    }
}
