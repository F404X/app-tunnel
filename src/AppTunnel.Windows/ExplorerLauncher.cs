using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;
using static AppTunnel.Core.Localized;

namespace AppTunnel.Windows;

internal static class ExplorerLauncher
{
    public static void Open(string executable, string arguments = "")
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic windows = shell.Windows();
            object location = 0;
            object? root = null;
            int handle;
            dynamic desktop = windows.FindWindowSW(ref location, ref root, 8, out handle, 1);
            if (desktop is null || handle == 0)
                throw new InvalidOperationException(T("The Windows desktop could not be reached.",
                    "A área de trabalho do Windows está indisponível.", "Не удалось получить доступ к рабочему столу Windows."));
            desktop.Document.Application.ShellExecute(executable, arguments, Path.GetDirectoryName(executable), "open", 1);
        }
        catch (Exception error) when (error is COMException or RuntimeBinderException or Win32Exception)
        {
            throw new InvalidOperationException(T("Windows Explorer could not open the application.",
                "O Windows Explorer não conseguiu abrir o aplicativo.", "Проводник Windows не смог открыть приложение."), error);
        }
    }
}
