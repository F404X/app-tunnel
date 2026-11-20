using System.ComponentModel;
using System.Text.Json;
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
                if (WindowsStartup.IsEnabled()) WindowsStartup.SetEnabled(false);
                new WireSockEngine().Stop();
                return 0;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                InvalidOperationException or Win32Exception or System.TimeoutException or System.ServiceProcess.TimeoutException)
            {
                return 1;
            }
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

        try
        {
            var store = new ProfileStore();
            SetLanguage(store.Load().Language);
            using var context = new AppTunnelApplicationContext(store, activation, setupExit, trayOnly);
            Application.Run(context);
            return 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or
            FormatException or InvalidOperationException or JsonException)
        {
            MessageBox.Show(error.Message, "AppTunnel", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
