using System.ComponentModel;
using static AppTunnel.Core.Localized;

namespace AppTunnel.Windows;

internal sealed class AppTunnelApplicationContext : ApplicationContext
{
    private readonly ProfileStore store;
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer activationTimer = new() { Interval = 230 };
    private readonly ProtonLogin proton;
    private MainForm window;
    private bool exiting;

    public AppTunnelApplicationContext(ProfileStore store, EventWaitHandle activation)
    {
        this.store = store;
        proton = new ProtonLogin(store);
        tray = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application,
            Text = "AppTunnel", Visible = true
        };
        tray.DoubleClick += (_, _) => ShowWindow();
        window = CreateWindow();
        BuildTrayMenu();
        ShowWindow();

        activationTimer.Tick += (_, _) =>
        {
            if (activation.WaitOne(0)) ShowWindow();
        };
        activationTimer.Start();
    }

    private MainForm CreateWindow()
    {
        var form = new MainForm(store, proton);
        form.LanguageRequested += ChangeLanguage;
        form.FormClosing += OnWindowClosing;
        form.FormClosed += (_, args) =>
        {
            if (args.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing)
                ExitThread();
        };
        return form;
    }

    private void BuildTrayMenu()
    {
        var menu = new ContextMenuStrip { Font = DesktopTheme.Body() };
        menu.Items.Add(T("Open AppTunnel", "Abrir AppTunnel"), null, (_, _) => ShowWindow());
        var disconnect = menu.Items.Add(T("Disconnect VPN", "Desconectar VPN"));
        disconnect.Click += async (_, _) => await window.Disconnect();
        menu.Opening += (_, _) => disconnect.Enabled = window.CanDisconnect;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(T("Hide Tray", "Ocultar ícone"), null, (_, _) => tray.Visible = false);
        menu.Items.Add(T("Exit interface", "Sair da interface"), null, (_, _) => Quit());
        var oldMenu = tray.ContextMenuStrip;
        tray.ContextMenuStrip = menu;
        oldMenu?.Dispose();
    }

    private void ShowWindow(bool restoreTray = true)
    {
        if (restoreTray) tray.Visible = true;
        window.Show();
        if (window.WindowState == FormWindowState.Minimized) window.WindowState = FormWindowState.Normal;
        window.BringToFront();
        window.Activate();
    }

    private void ChangeLanguage(string language)
    {
        if (window.HasPendingOperation || !window.TrySaveLanguage(language)) return;
        var previous = window;
        var state = previous.WindowState;
        var bounds = previous.WindowState == FormWindowState.Normal ? previous.Bounds : previous.RestoreBounds;
        var page = previous.CurrentPage;
        SetLanguage(language);
        window = CreateWindow();
        window.StartPosition = FormStartPosition.Manual;
        if (!bounds.IsEmpty) window.Bounds = bounds;
        if (state == FormWindowState.Maximized) window.WindowState = state;
        window.ShowPage(page);
        BuildTrayMenu();
        previous.Dispose();
        ShowWindow(restoreTray: false);
    }

    private void OnWindowClosing(object? sender, FormClosingEventArgs args)
    {
        if (exiting || args.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing) return;
        args.Cancel = true;
        window.SaveInterface();
        window.Hide();
    }

    private void Quit()
    {
        if (window.HasPendingOperation) return;
        exiting = true;
        window.SaveInterface();
        window.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            activationTimer.Dispose();
            if (!window.IsDisposed) window.Dispose();
            tray.Visible = false;
            tray.ContextMenuStrip?.Dispose();
            tray.Dispose();
        }
        base.Dispose(disposing);
    }
}
