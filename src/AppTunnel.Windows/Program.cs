using System.ComponentModel;
using System.Text.Json;
using AppTunnel.Core;
using static AppTunnel.Core.Localized;

namespace AppTunnel.Windows;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args is ["--uninstall"])
        {
            try
            {
                if (!WindowsStartup.IsInstalled) return 1;
                WindowsStartup.SetEnabled(false);
                WireSockEngine.ForUninstall().Stop();
                WindowsTelemetry.RemoveForUninstall();
                return 0;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                InvalidOperationException or Win32Exception or System.TimeoutException or System.ServiceProcess.TimeoutException)
            {
                return 1;
            }
        }

        if (args is ["--telemetry-preview", var language])
        {
            SetLanguage(language);
            DesktopTheme.Apply(ThemeMode.System);
            using var preview = WindowsTelemetry.Create(readOnly: true);
            TelemetryWindow.Show(null, preview, allowChanges: false);
            return 0;
        }

        var trayOnly = args is ["--startup"];
        using var activation = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AppTunnel.Open");
        using var setupExit = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AppTunnel.QuitForSetup");
        using var mutex = new Mutex(false, @"Local\AppTunnel.Interface");
        try
        {
            if (!mutex.WaitOne(0))
            {
                if (!trayOnly) activation.Set();
                return 0;
            }
        }
        catch (AbandonedMutexException) { }

        using var telemetry = WindowsTelemetry.Create();
        try
        {
            var store = new ProfileStore();
            var preferences = store.Load();
            SetLanguage(preferences.Language);
            DesktopTheme.Apply(preferences.Theme);
            using var context = new AppTunnelApplicationContext(store, telemetry, activation, setupExit, trayOnly);
            telemetry.RecordStart();
            Application.Run(context);
            return 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or
            FormatException or InvalidOperationException or JsonException)
        {
            _ = telemetry.ReportErrorAsync(TelemetryStage.Startup, error);
            ThemedMessage.Warn(null, error.Message);
            return 1;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
