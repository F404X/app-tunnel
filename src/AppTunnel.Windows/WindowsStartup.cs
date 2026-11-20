using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.CSharp.RuntimeBinder;
using Microsoft.Win32;
using static AppTunnel.Core.Localized;

namespace AppTunnel.Windows;

internal static class WindowsStartup
{
    private const string InstallationKey = @"SOFTWARE\AppTunnel\Installation";
    private const int FileNotFound = unchecked((int)0x80070002);
    private const int TaskNotFound = unchecked((int)0x8004130F);
    private const int InteractiveToken = 3;

    public static bool IsInstalled
    {
        get
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AppTunnel");
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var installation = hive.OpenSubKey(InstallationKey);
            return installation?.GetValue("Path") is string registered &&
                directory.Equals(registered, StringComparison.OrdinalIgnoreCase) &&
                Path.Combine(directory, "AppTunnel.exe").Equals(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) &&
                (File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0 &&
                (File.GetAttributes(Application.ExecutablePath) & FileAttributes.ReparsePoint) == 0;
        }
    }

    public static bool IsEnabled() => IsInstalled && WithScheduler((_, folder, sid) =>
    {
        try
        {
            dynamic task = folder.GetTask(TaskName(sid));
            return (bool)task.Enabled;
        }
        catch (Exception error) when (error.HResult is FileNotFound or TaskNotFound) { return false; }
    });

    public static void SetEnabled(bool enabled)
    {
        if (!IsInstalled)
            throw new InvalidOperationException(T("Run the AppTunnel installer to enable Start with Windows.",
                "Use o instalador do AppTunnel para ativar Iniciar com o Windows."));

        WithScheduler((scheduler, folder, sid) =>
        {
            var name = TaskName(sid);
            if (!enabled)
            {
                try { folder.DeleteTask(name, 0); }
                catch (Exception error) when (error.HResult is FileNotFound or TaskNotFound) { }
                return false;
            }

            dynamic task = scheduler.NewTask(0);
            task.RegistrationInfo.Description = "Open AppTunnel in the system tray when this user signs in.";
            task.Principal.UserId = sid;
            task.Principal.LogonType = InteractiveToken;
            task.Principal.RunLevel = 1; // TASK_RUNLEVEL_HIGHEST
            task.Settings.DisallowStartIfOnBatteries = false;
            task.Settings.StopIfGoingOnBatteries = false;
            task.Settings.ExecutionTimeLimit = "PT0S";
            dynamic trigger = task.Triggers.Create(9); // TASK_TRIGGER_LOGON
            trigger.UserId = sid;
            dynamic action = task.Actions.Create(0); // TASK_ACTION_EXEC
            action.Path = Application.ExecutablePath;
            action.WorkingDirectory = AppContext.BaseDirectory;
            action.Arguments = "--startup";
            folder.RegisterTaskDefinition(name, task, 6, sid, null, InteractiveToken, null); // TASK_CREATE_OR_UPDATE
            return true;
        });
    }

    private static bool WithScheduler(Func<dynamic, dynamic, string, bool> operation)
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User?.Value ?? throw new InvalidOperationException(T("Windows user unavailable.", "Usuário do Windows indisponível."));
            var type = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!;
            dynamic scheduler = Activator.CreateInstance(type)!;
            scheduler.Connect();
            dynamic folder = scheduler.GetFolder(@"\");
            return operation(scheduler, folder, sid);
        }
        catch (Exception error) when (error is COMException or RuntimeBinderException)
        {
            throw new InvalidOperationException(T("AppTunnel could not access the Windows Task Scheduler.",
                "O AppTunnel não conseguiu acessar o Agendador de Tarefas do Windows."), error);
        }
    }

    private static string TaskName(string sid) => "AppTunnel Startup " + sid;
}
