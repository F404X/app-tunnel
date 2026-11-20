using System.ComponentModel;
using System.Text.Json;
using static AppTunnel.Core.Localized;

namespace AppTunnel.Windows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var activation = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AppTunnel.Open");
        using var mutex = new Mutex(false, @"Local\AppTunnel.Interface");
        try
        {
            if (!mutex.WaitOne(0))
            {
                activation.Set();
                return;
            }
        }
        catch (AbandonedMutexException) { }

        try
        {
            var store = new ProfileStore();
            SetLanguage(store.Load().Language);
            using var context = new AppTunnelApplicationContext(store, activation);
            Application.Run(context);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or
            FormatException or InvalidOperationException or JsonException)
        {
            MessageBox.Show(error.Message, "AppTunnel", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
