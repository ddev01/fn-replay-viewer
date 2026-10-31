namespace ReplayViewer;

internal sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly ListenerHost _host;
    private readonly ToolStripMenuItem _updateItem;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private Action? _applyUpdate;

    public TrayApp(ListenerHost host)
    {
        _host = host;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => SingleInstance.OpenBrowser());
        _updateItem = new ToolStripMenuItem("Update") { Enabled = false };
        _updateItem.Click += (_, _) => _applyUpdate?.Invoke();
        menu.Items.Add(_updateItem);
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        _icon = new NotifyIcon
        {
            Text = "Replay Viewer " + AppConfig.Version,
            Icon = LoadIcon(),
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => SingleInstance.OpenBrowser();
        SingleInstance.OpenBrowser();
    }

    public void NotifyUpdate(Action apply)
    {
        void Show()
        {
            _applyUpdate = apply;
            _updateItem.Enabled = true;
            _icon.ShowBalloonTip(
                4000,
                "Replay Viewer",
                "An update is available. Use Update on the tray icon.",
                ToolTipIcon.Info
            );
        }

        if (_ui is not null)
        {
            _ui.Post(_ => Show(), null);
            return;
        }

        Show();
    }

    protected override void ExitThreadCore()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _host.Dispose();
        base.ExitThreadCore();
    }

    private static Icon LoadIcon()
    {
        var fromExe = Icon.ExtractAssociatedIcon(
            Environment.ProcessPath ?? Application.ExecutablePath
        );
        if (fromExe is not null)
        {
            return fromExe;
        }

        var path = Path.Combine(AppContext.BaseDirectory, "app.ico");
        if (File.Exists(path))
        {
            return new Icon(path);
        }

        return SystemIcons.Application;
    }
}
