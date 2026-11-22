using System.ComponentModel;
using AppTunnel.Core;
using Microsoft.Win32;
using static AppTunnel.Core.Localized;

namespace AppTunnel.Windows;

internal sealed class AppTunnelApplicationContext : ApplicationContext
{
    private readonly ProfileStore store;
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer activationTimer = new() { Interval = 230 };
    private readonly ProtonLogin proton;
    private readonly Telemetry telemetry;
    private long nextThemePoll;
    private long nextMaintenance;
    private bool updating;
    private MainForm window;
    private bool exiting;
    private bool preparingSetup;
    private Icon? trayIcon;
    private string? trayVariant;
    private Size traySize;

    public AppTunnelApplicationContext(ProfileStore store, Telemetry telemetry, EventWaitHandle activation, EventWaitHandle setupExit, bool trayOnly)
    {
        this.store = store;
        this.telemetry = telemetry;
        proton = new ProtonLogin(store);
        tray = new NotifyIcon { Text = "AppTunnel" };
        RefreshTrayIcon();
        tray.Visible = true;
        tray.DoubleClick += (_, _) => ShowWindow();
        window = CreateWindow();
        BuildTrayMenu();
        if (!trayOnly) ShowWindow();

        activationTimer.Tick += async (_, _) =>
        {
            if (!preparingSetup && !window.HasPendingOperation && setupExit.WaitOne(0))
            {
                preparingSetup = true;
                window.Enabled = false;
                try { await proton.CloseForSetupAsync(); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
                finally { Quit(); }
                return;
            }

            if (!preparingSetup && activation.WaitOne(0)) ShowWindow();
            if (preparingSetup || window.HasPendingOperation || Application.OpenForms.Cast<Form>().Any(form => form.Modal)) return;
            _ = window.RestoreConnection();
            var now = Environment.TickCount64;
            if (now >= nextThemePoll)
            {
                nextThemePoll = now + 2000;
                RefreshTrayIcon();
                if (DesktopTheme.ResolveDark(window.PreferredTheme) != DesktopTheme.IsDark)
                {
                    window.SaveInterface();
                    DesktopTheme.Apply(window.PreferredTheme);
                    RecreateWindow();
                }
            }
            if (now < nextMaintenance || updating) return;
            nextMaintenance = now + 60_000;
            _ = telemetry.SendStatisticsAsync();
            if (proton.HasOpenWindow) return;
            updating = true;
            try { await window.CheckForAutomaticUpdate(); }
            finally { updating = false; }
        };
        activationTimer.Start();
    }

    private MainForm CreateWindow()
    {
        var form = new MainForm(store, proton, telemetry);
        form.LanguageRequested += ChangeLanguage;
        form.ThemeRequested += ChangeTheme;
        form.StartupRequested += () => SetStartup(!form.StartsWithWindows);
        form.RefreshStartup();
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
        var menu = new ContextMenuStrip
        {
            Font = DesktopTheme.Body(), Renderer = new ThemeMenuRenderer()
        };
        menu.Items.Add(T("Open AppTunnel", "Abrir AppTunnel", "Открыть AppTunnel"), null, (_, _) => ShowWindow());
        var disconnect = menu.Items.Add(T("Disconnect VPN", "Desconectar VPN", "Отключить VPN"));
        disconnect.Click += async (_, _) => await window.Disconnect();
        var startup = new ToolStripMenuItem(T("Start with Windows", "Iniciar com o Windows", "Запускать вместе с Windows"));
        startup.Click += (_, _) => SetStartup(!startup.Checked);
        menu.Items.Add(startup);
        menu.Opening += (_, _) =>
        {
            menu.BackColor = DesktopTheme.Surface;
            menu.ForeColor = DesktopTheme.Text;
            disconnect.Enabled = window.CanDisconnect && !preparingSetup;
            startup.Enabled = !preparingSetup;
            window.RefreshStartup();
            startup.Checked = window.StartsWithWindows;
        };
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(T("Hide Tray", "Ocultar ícone", "Скрыть значок в трее"), null, (_, _) => tray.Visible = false);
        menu.Items.Add(T("Exit interface", "Sair da interface", "Закрыть интерфейс"), null, (_, _) => Quit());
        var oldMenu = tray.ContextMenuStrip;
        tray.ContextMenuStrip = menu;
        oldMenu?.Dispose();
    }

    // The tray glyph has no background, so it follows the taskbar theme (not the app theme) to stay legible.
    private void RefreshTrayIcon()
    {
        var variant = TaskbarIsLight() ? "light" : "dark";
        var size = SystemInformation.SmallIconSize;
        if (variant == trayVariant && size == traySize) return;
        using var stream = typeof(AppTunnelApplicationContext).Assembly.GetManifestResourceStream($"AppTunnel.Tray.tray-{variant}.ico");
        if (stream is null) return;
        var previous = trayIcon;
        trayIcon = new Icon(stream, size);
        tray.Icon = trayIcon;
        previous?.Dispose();
        trayVariant = variant;
        traySize = size;
    }

    private static bool TaskbarIsLight()
    {
        try
        {
            return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "SystemUsesLightTheme", 0) is 1;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private void SetStartup(bool enabled)
    {
        if (preparingSetup) return;
        window.SetStartup(enabled);
    }

    private void ShowWindow(bool restoreTray = true)
    {
        if (preparingSetup) return;
        if (restoreTray) tray.Visible = true;
        window.Show();
        if (window.WindowState == FormWindowState.Minimized) window.WindowState = FormWindowState.Normal;
        window.BringToFront();
        window.Activate();
    }

    private void ChangeLanguage(string language)
    {
        if (window.HasPendingOperation || !window.TrySaveLanguage(language)) return;
        // An open Proton window is not closed here: it may be saving a new profile. It restarts in the new language
        // the next time it is opened (ProtonLogin.OpenAsync compares the language it was started with).
        SetLanguage(language);
        RecreateWindow();
    }

    private void ChangeTheme(ThemeMode mode)
    {
        if (window.HasPendingOperation || !window.TrySaveTheme(mode)) return;
        DesktopTheme.Apply(mode);
        RecreateWindow();
    }

    private void RecreateWindow()
    {
        var previous = window;
        var state = previous.WindowState;
        var bounds = previous.WindowState == FormWindowState.Normal ? previous.Bounds : previous.RestoreBounds;
        var page = previous.CurrentPage;
        var visible = previous.Visible;
        window = CreateWindow();
        window.StartPosition = FormStartPosition.Manual;
        if (!bounds.IsEmpty) window.Bounds = bounds;
        if (state == FormWindowState.Maximized) window.WindowState = state;
        window.ShowPage(page);
        BuildTrayMenu();
        previous.Dispose();
        if (visible) ShowWindow(restoreTray: false);
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
            trayIcon?.Dispose();
        }
        base.Dispose(disposing);
    }
}
