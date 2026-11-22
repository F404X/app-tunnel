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
            // Taken at once, even while an operation runs: Setup waits a limited time, and an operation that blocked
            // the request would leave it timing out. The operation in progress is drained below instead.
            if (!preparingSetup && setupExit.WaitOne(0))
            {
                preparingSetup = true;
                window.PrepareForSetup();
                tray.ContextMenuStrip?.Close();
                try { await proton.CloseForSetupAsync(); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
                // An operation that started before the request (such as applying a change) finishes first; Setup
                // waits for AppTunnel to close, so giving up here would leave both waiting.
                for (var waited = 0; window.HasPendingOperation && waited < 30_000; waited += 200) await Task.Delay(200);
                Quit(force: true);
                return;
            }

            if (!preparingSetup && activation.WaitOne(0)) ShowWindow();
            if (preparingSetup || window.HasPendingOperation || Application.OpenForms.Cast<Form>().Any(form => form.Modal)) return;
            _ = window.RestoreConnection();
            // A reconnect just started must not be cut off by recreating the window for a theme change.
            if (window.HasPendingOperation) return;
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
        var menu = new ThemeMenu();
        menu.Add(T("Open AppTunnel", "Abrir AppTunnel", "Открыть AppTunnel"), Glyph.Window, (_, _) => ShowWindow());
        var disconnect = menu.Add(T("Disconnect VPN", "Desconectar VPN", "Отключить VPN"), Glyph.Power);
        disconnect.Click += async (_, _) =>
        {
            if (!preparingSetup) await window.Disconnect();
        };
        var startup = menu.Add(T("Start with Windows", "Iniciar com o Windows", "Запускать вместе с Windows"), Glyph.Bolt);
        startup.Click += (_, _) => SetStartup(!startup.Checked);
        menu.Opening += (_, _) =>
        {
            disconnect.Enabled = window.CanDisconnect && !preparingSetup;
            startup.Enabled = !preparingSetup;
            window.RefreshStartup();
            startup.Checked = window.StartsWithWindows;
        };
        menu.AddSeparator();
        menu.Add(T("Hide Tray", "Ocultar ícone", "Скрыть значок в трее"), Glyph.EyeOff, (_, _) => tray.Visible = false);
        menu.Add(T("Exit interface", "Sair da interface", "Закрыть интерфейс"), Glyph.Exit, (_, _) => Quit());
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
        // This runs inside a BeginInvoke callback, after which .NET restores the UI thread's culture. The idle event
        // is raised straight from the message loop, so setting it again there keeps formatting and sorting in the
        // new language too (the texts already follow it through Localized).
        EventHandler? reapply = null;
        reapply = (_, _) =>
        {
            Application.Idle -= reapply;
            SetLanguage(language);
        };
        Application.Idle += reapply;
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

    private void Quit(bool force = false)
    {
        if (!force && window.HasPendingOperation) return;
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
