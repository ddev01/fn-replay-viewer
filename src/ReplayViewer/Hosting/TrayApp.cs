using System.Runtime.InteropServices;
using ReplayViewer.Core;

namespace ReplayViewer;

internal sealed class TrayApp : ApplicationContext
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly NotifyIcon _icon;
    private readonly ListenerHost _host;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _updateItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly System.Windows.Forms.Timer _timer = new();
    private UpdateChecker.Available? _available;
    private bool _checking;

    public TrayApp(ListenerHost host)
    {
        _host = host;

        _menu = new ContextMenuStrip();
        _menu.Items.Add("Open", null, (_, _) => SingleInstance.OpenBrowser());

        _updateItem = new ToolStripMenuItem("Check for updates");
        _updateItem.Click += async (_, _) => await OnUpdateClickedAsync();
        _menu.Items.Add(_updateItem);

        _startupItem = new ToolStripMenuItem("Start with Windows");
        _startupItem.Click += (_, _) => ToggleStartup();
        _menu.Items.Add(_startupItem);

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => ExitThread());
        _menu.Opening += (_, _) => _startupItem.Checked = InstallLayout.IsStartupRegistered();

        _icon = new NotifyIcon
        {
            Text = "Replay Viewer " + AppConfig.Version,
            Icon = LoadIcon(),
            Visible = true,
        };
        // Show the menu ourselves: NotifyIcon's built-in ContextMenuStrip positions itself
        // against the full screen rather than the working area, so it can land behind the taskbar.
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                // A manually shown menu is not the foreground window, so it never receives the
                // click that should dismiss it and stays open when you click into another app.
                // Taking the foreground first gives it the normal popup behaviour.
                SetForegroundWindow(_menu.Handle);
                _menu.Show(Cursor.Position);
            }
        };
        _icon.DoubleClick += (_, _) => SingleInstance.OpenBrowser();

        // The first check waits for the message loop so its continuation lands on the UI thread.
        _timer.Interval = 1000;
        _timer.Tick += OnFirstTick;
        _timer.Start();

        SingleInstance.OpenBrowser();
    }

    private async void OnFirstTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        _timer.Tick -= OnFirstTick;
        _timer.Tick += async (_, _) => await CheckAsync(manual: false);
        _timer.Interval = (int)CheckInterval.TotalMilliseconds;
        _timer.Start();
        await CheckAsync(manual: false);
    }

    private async Task OnUpdateClickedAsync()
    {
        if (_available is null)
        {
            await CheckAsync(manual: true);
            return;
        }

        await ApplyAsync(_available);
    }

    private async Task CheckAsync(bool manual)
    {
        if (_checking)
        {
            return;
        }

        _checking = true;
        _updateItem.Enabled = false;
        _updateItem.Text = "Checking for updates…";
        try
        {
            _available = await UpdateChecker.CheckAsync(CancellationToken.None);
            if (_available is null)
            {
                FileLog.Info("update check: up to date");
                UpdateChecker.AvailableVersion = null;
                _updateItem.Text = $"Up to date ({AppConfig.Version})";
            }
            else
            {
                FileLog.Info($"update check: {_available.Version} available");
                UpdateChecker.AvailableVersion = _available.Version;
                _updateItem.Text = $"Update to {_available.Version}";
                _icon.ShowBalloonTip(
                    4000,
                    "Replay Viewer",
                    $"Version {_available.Version} is available. It installs the next time Replay Viewer starts.",
                    ToolTipIcon.Info
                );
                // Silent: fetch and verify it now so the next launch can apply it immediately.
                if (!UpdateChecker.TryGetPending(out _, out _))
                {
                    _ = StageAsync(_available);
                }
            }
        }
        catch (Exception ex)
        {
            FileLog.Error("update check failed", ex);
            _updateItem.Text =
                _available is null ? "Check for updates" : $"Update to {_available.Version}";
            if (manual)
            {
                _icon.ShowBalloonTip(
                    4000,
                    "Replay Viewer",
                    "Could not check for updates. See the log for details.",
                    ToolTipIcon.Warning
                );
            }
        }
        finally
        {
            _updateItem.Enabled = true;
            _checking = false;
        }
    }

    /// <summary>Downloads and verifies an update without touching the running install.</summary>
    private static async Task StageAsync(UpdateChecker.Available update)
    {
        try
        {
            await UpdateChecker.StageAsync(update, CancellationToken.None);
        }
        catch (Exception ex)
        {
            FileLog.Error("update staging failed", ex);
        }
    }

    private async Task ApplyAsync(UpdateChecker.Available update)
    {
        _updateItem.Enabled = false;
        _updateItem.Text = $"Updating to {update.Version}…";
        try
        {
            // Reuse a payload that was already staged silently; otherwise fetch it now.
            var exe = UpdateChecker.TryGetPending(out _, out var pending)
                ? pending
                : await UpdateChecker.StageAsync(update, CancellationToken.None);
            UpdateChecker.MarkApplied();
            UpdateInstaller.Apply(exe, AppPaths.Default.InstalledExePath);
            Application.Exit();
        }
        catch (Exception ex)
        {
            FileLog.Error("update apply failed", ex);
            _updateItem.Text = $"Update to {update.Version}";
            _updateItem.Enabled = true;
            _icon.ShowBalloonTip(
                4000,
                "Replay Viewer",
                "The update failed. See the log for details.",
                ToolTipIcon.Error
            );
        }
    }

    private void ToggleStartup()
    {
        try
        {
            InstallLayout.SetStartup(!InstallLayout.IsStartupRegistered());
        }
        catch (Exception ex)
        {
            FileLog.Error("startup toggle failed", ex);
        }

        _startupItem.Checked = InstallLayout.IsStartupRegistered();
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _timer.Dispose();
        _menu.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        _host.Dispose();
        base.ExitThreadCore();
    }

    private static Icon LoadIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "app.ico");
        if (File.Exists(path))
        {
            return new Icon(path, SystemInformation.SmallIconSize);
        }

        var fromExe = Icon.ExtractAssociatedIcon(
            Environment.ProcessPath ?? Application.ExecutablePath
        );
        return fromExe ?? SystemIcons.Application;
    }
}
