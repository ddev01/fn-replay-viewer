using System.Net;
using ReplayViewer.Core;

namespace ReplayViewer;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // A parse runs in a child process so a replay the parser stalls on can be killed.
        if (
            args.Length >= 3
            && string.Equals(args[0], ReplayParseProcess.Switch, StringComparison.OrdinalIgnoreCase)
        )
        {
            return ReplayParseCommand.Run(args[1], args[2], args.Length > 3 ? args[3] : null);
        }

        if (HasSwitch(args, UpdateInstaller.InstallSwitch))
        {
            // The app being replaced may still be exiting; its exe cannot be replaced until it is.
            if (UpdateInstaller.WaitPid(args) is { } parent)
            {
                UpdateInstaller.WaitForExit(parent, TimeSpan.FromSeconds(30));
            }

            var exitCode = Install();
            // A failed install still relaunches: the old files were restored, and no app running
            // is worse than an old app running.
            if (HasSwitch(args, UpdateInstaller.RelaunchSwitch))
            {
                Relaunch();
            }

            return exitCode;
        }

        if (HasSwitch(args, "--uninstall"))
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

        // A verified update staged by a previous run is applied here, before the UI starts, so
        // updating never interrupts a session. The payload's --install relaunches the app.
        var installed = AppPaths.Default.InstalledExePath;
        if (
            !AppConfig.IsDev
            && string.Equals(Environment.ProcessPath, installed, StringComparison.OrdinalIgnoreCase)
            && UpdateChecker.TryGetPending(out var pendingVersion, out var pendingExe)
        )
        {
            UpdateChecker.MarkApplied();
            UpdateInstaller.Apply(pendingExe, installed);
            FileLog.Info($"applying staged update {pendingVersion}");
            return 0;
        }

        // No ready marker here means any payload still in temp is a leftover of an update that
        // already applied (the app starts it from there); it is a full copy of the old version.
        // Nothing is staging yet, so this cannot delete a download in progress.
        if (!UpdateChecker.TryGetPending(out _, out _))
        {
            UpdateChecker.ClearPending();
        }
        AppPaths.Default.EnsureDirectories();
        ReplayFileAccess.SweepLegacyCopies(
            Path.Combine(Path.GetTempPath(), "ReplayViewer"),
            TimeSpan.FromHours(1)
        );
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

        scraper.StartBackfill();
        var tray = new TrayApp(host);
        Application.Run(tray);
        return 0;
    }

    private static bool HasSwitch(string[] args, string name) =>
        args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

    private static int Install()
    {
        try
        {
            var paths = AppPaths.Default;
            var source = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            InstallLayout.CopyToInstallDir(source, paths);
            // Startup is owned by the installer's autostart task and the tray toggle, so an
            // update never re-enables it. A legacy logon task is migrated, not dropped.
            if (LogonTask.Exists())
            {
                InstallLayout.RegisterStartup(paths);
            }

            FileLog.Info($"installed to {paths.InstalledExePath}");
            return 0;
        }
        catch (Exception ex)
        {
            FileLog.Error("install failed", ex);
            return 1;
        }
    }

    private static void Relaunch()
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(AppPaths.Default.InstalledExePath)
                {
                    UseShellExecute = false,
                }
            );
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            FileLog.Error("could not relaunch after install", ex);
        }
    }
}
