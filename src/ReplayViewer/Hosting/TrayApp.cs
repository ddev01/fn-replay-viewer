namespace ReplayViewer;

internal sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly ListenerHost _host;

    public TrayApp(ListenerHost host)
    {
        _host = host;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => SingleInstance.OpenBrowser());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        _icon = new NotifyIcon
        {
            Text = "Replay Viewer",
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => SingleInstance.OpenBrowser();
        SingleInstance.OpenBrowser();
    }

    protected override void ExitThreadCore()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _host.Dispose();
        base.ExitThreadCore();
    }
}
