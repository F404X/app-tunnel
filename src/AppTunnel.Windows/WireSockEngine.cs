using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using AppTunnel.Core;
using static AppTunnel.Core.Localized;

namespace AppTunnel.Windows;

internal sealed record TunnelSession(StoredProfile Profile, int ProgramCount, string[] RequestedPrograms,
    RouteOptions Options, bool Diagnostics, string EnginePath = "", string ConfigPath = "", string OwnerSid = "",
    VpnProtocol Protocol = VpnProtocol.WireGuard, string[]? BlockedIpv6Applications = null,
    bool VerifyHandshake = false, Guid RequestedProfile = default, ProtocolPreference RequestedProtocol = ProtocolPreference.Profile);

internal sealed partial class WireSockEngine : ITunnelEngine
{
    private const string Executable = "wiresock-client.exe";
    private const string ServiceName = "wiresock-client-service";
    private const string ServiceRegistryPath = @"SYSTEM\CurrentControlSet\Services\wiresock-client-service";
    private const string AppRegistryPath = @"SOFTWARE\AppTunnel";
    private const string LegacyRegistryPath = @"SOFTWARE\RotaProton";
    private static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(15);
    private static readonly Encoding Utf8 = new UTF8Encoding(false);
    private static readonly string ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    private readonly SecurityIdentifier owner = CurrentUser();

    private WireSockHealth? health;
    private string? observedSession;
    public event Action<string>? Output;

    public bool Supports(VpnProtocol protocol) =>
        protocol is VpnProtocol.WireGuard or VpnProtocol.WireGuardPadding or VpnProtocol.AmneziaWG;

    public VpnSessionConfig Prepare(WireGuardProfile profile, string[] applications, RouteOptions options, VpnProtocol protocol) =>
        profile.BuildSessionConfig(applications, options, protocol);

    public TunnelHealth ReadHealth(TunnelSession session)
    {
        if (health is null || observedSession != session.ConfigPath)
        {
            health = new WireSockHealth();
            observedSession = session.ConfigPath;
        }
        return health.Poll();
    }

    public TunnelSession? GetActive()
    {
        var session = ReadRecord();
        if (session is null || !OwnsService(session)) return null;
        using var service = new ServiceController(ServiceName);
        try { return service.Status == ServiceControllerStatus.Running ? session : null; }
        catch (InvalidOperationException) when (!OwnsService(session)) { return null; }
    }

    public TunnelSession Start(string preferredPath, VpnSessionConfig config, TunnelSession request, bool verifyHandshake)
    {
        var executable = preferredPath.Length > 0 && File.Exists(preferredPath) ? preferredPath : Locate();
        if (executable is null)
            throw new InvalidOperationException(T("Install WireSock Secure Connect CLI using the command in Diagnostics.",
                "Instale o WireSock Secure Connect CLI. O comando está na aba Diagnóstico.",
                "Установите WireSock Secure Connect CLI с помощью команды на вкладке «Диагностика»."));
        var path = ValidateInstallation(executable);
        using var gate = LockOperations();
        try
        {
            var previous = ReadRecord();
            var installed = ServiceImage() is not null;
            if (installed && (previous is null || !OwnsService(previous)))
                throw new InvalidOperationException(T("A different WireSock service is installed. Disconnect it in its original app first.",
                    "Outro serviço do WireSock está instalado. Desconecte-o no aplicativo de origem primeiro.", "Установлена другая служба WireSock. Отключите её в исходном приложении."));
            if (!installed)
            {
                if (Process.GetProcessesByName("wiresock-client").Any(IsLiveProcess))
                    throw new InvalidOperationException(T("Close the other running WireSock client before connecting.",
                        "Feche o outro cliente WireSock em execução antes de conectar.", "Перед подключением закройте другой работающий клиент WireSock."));
                if (previous is not null) RemoveConfig(previous);
                previous = null;
            }

            var session = Stage(path, config.Text, request with
            {
                VerifyHandshake = verifyHandshake
            });
            try
            {
                Ipv6Firewall.Add(session);
                if (previous is not null) RemoveService(previous);
                WriteRecord(session);
                health = new WireSockHealth();
                observedSession = session.ConfigPath;
                InstallAndStart(session);
                if (verifyHandshake) VerifyHealth(session);
            }
            catch
            {
                RollBack(session, previous);
                throw;
            }

            if (previous is not null) RemoveConfig(previous);
            return session;
        }
        finally
        {
            gate.ReleaseMutex();
        }
    }

    public void Stop()
    {
        using var gate = LockOperations();
        try
        {
            var session = ReadRecord();
            if (session is null) return;
            if (OwnsService(session)) RemoveService(session);
            if (ServiceImage() is not null)
                throw new InvalidOperationException(T("The installed WireSock service no longer belongs to this AppTunnel session.",
                    "O serviço WireSock instalado não pertence mais a esta sessão do AppTunnel.", "Установленная служба WireSock больше не принадлежит этому сеансу AppTunnel."));
            RemoveConfig(session);
            WriteRecord(null);
        }
        finally
        {
            gate.ReleaseMutex();
        }
    }

    private void InstallAndStart(TunnelSession session)
    {
        RunTool(session.EnginePath, "install", "-start-type", "3", "-config", session.ConfigPath, "-log-level",
            session.Diagnostics || session.VerifyHandshake ? "info" : "error");
        if (!OwnsService(session))
            throw new InvalidOperationException(T("WireSock did not register the expected Windows service.",
                "O WireSock não registrou o serviço do Windows esperado.", "WireSock не зарегистрировал ожидаемую службу Windows."));

        using var service = new ServiceController(ServiceName);
        if (service.Status == ServiceControllerStatus.Stopped) service.Start();
        service.WaitForStatus(ServiceControllerStatus.Running, ServiceTimeout);
    }

    private void VerifyHealth(TunnelSession session)
    {
        var watch = Stopwatch.StartNew();
        var state = TunnelHealth.Unknown;
        do
        {
            state = ReadHealth(session);
            if (state == TunnelHealth.HandshakeReceived) break;
            if (state is TunnelHealth.EndpointResolutionFailed or TunnelHealth.RoutingFailed)
                throw new InvalidOperationException(TunnelDiagnostics.Describe(state));
            Thread.Sleep(300);
        } while (watch.Elapsed < TimeSpan.FromSeconds(12));
        if (state is TunnelHealth.HandshakeSent or TunnelHealth.HandshakeTimedOut)
            throw new System.TimeoutException(TunnelDiagnostics.Describe(TunnelHealth.HandshakeTimedOut));
        if (GetActive()?.ConfigPath != session.ConfigPath)
            throw new InvalidOperationException(T("WireSock stopped while verifying the route.",
                "O WireSock parou durante a verificação da rota.", "WireSock остановился при проверке маршрута."));
        Output?.Invoke(TunnelDiagnostics.Describe(state));
    }

    private void RemoveService(TunnelSession session)
    {
        if (!OwnsService(session))
            throw new InvalidOperationException(T("The WireSock service configuration changed during the operation.",
                "A configuração do serviço WireSock mudou durante a operação.", "Конфигурация службы WireSock изменилась во время операции."));
        using (var service = new ServiceController(ServiceName))
        {
            if (service.Status == ServiceControllerStatus.StartPending)
                service.WaitForStatus(ServiceControllerStatus.Running, ServiceTimeout);
            if (service.Status is not (ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending))
                service.Stop();
            service.WaitForStatus(ServiceControllerStatus.Stopped, ServiceTimeout);
        }
        if (!OwnsService(session))
            throw new InvalidOperationException(T("The WireSock service configuration changed during the operation.",
                "A configuração do serviço WireSock mudou durante a operação.", "Конфигурация службы WireSock изменилась во время операции."));
        RunTool(session.EnginePath, "uninstall");
        var watch = Stopwatch.StartNew();
        while (ServiceImage() is not null && watch.Elapsed < ServiceTimeout) Thread.Sleep(120);
        if (ServiceImage() is not null)
            throw new InvalidOperationException(T("Windows is still removing the WireSock service.",
                "O Windows ainda está removendo o serviço WireSock.", "Windows ещё удаляет службу WireSock."));
    }

    private void RollBack(TunnelSession session, TunnelSession? previous)
    {
        try
        {
            if (OwnsService(session)) RemoveService(session);
            if (ServiceImage() is null)
            {
                RemoveConfig(session);
                WriteRecord(previous);
                if (previous is not null) InstallAndStart(previous);
            }
            else if (previous is not null && OwnsService(previous))
            {
                RemoveConfig(session);
                WriteRecord(previous);
                using var service = new ServiceController(ServiceName);
                if (service.Status == ServiceControllerStatus.Stopped) service.Start();
                service.WaitForStatus(ServiceControllerStatus.Running, ServiceTimeout);
            }
            else if (!OwnsService(session))
            {
                RemoveConfig(session);
                WriteRecord(null);
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or System.ServiceProcess.TimeoutException)
        {
            Output?.Invoke(T($"Could not restore the previous service: {ex.Message}",
                $"Não foi possível restaurar o serviço anterior: {ex.Message}", $"Не удалось восстановить предыдущую службу: {ex.Message}"));
        }
    }

    private TunnelSession Stage(string executable, string config, TunnelSession request)
    {
        var directory = Path.Combine(ProgramData, "AppTunnel-" + Guid.NewGuid().ToString("N"));
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        foreach (var user in new[] { owner, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                     new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) })
        {
            security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        }
        new DirectoryInfo(directory).Create(security);
        var session = request with { ConfigPath = Path.Combine(directory, "session.conf"), EnginePath = executable, OwnerSid = owner.Value };
        var bytes = Utf8.GetBytes(config);
        try
        {
            using var file = new FileStream(session.ConfigPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Write(bytes);
            file.Flush(flushToDisk: true);
            return session;
        }
        catch
        {
            RemoveConfig(session);
            throw;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static void RemoveConfig(TunnelSession session)
    {
        if (SafeConfigDirectory(session.ConfigPath) is not { } directory || !Directory.Exists(directory)) return;
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
        Ipv6Firewall.Remove(session);
        File.Delete(session.ConfigPath);
        Directory.Delete(directory);
    }

    private TunnelSession? ReadRecord()
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        var session = ReadSession(machine, AppRegistryPath) ?? ReadSession(machine, LegacyRegistryPath);
        return session?.OwnerSid == owner.Value && SafeConfigDirectory(session.ConfigPath) is not null
            ? session : null;
    }

    private static TunnelSession? ReadSession(RegistryKey machine, string path)
    {
        using var key = machine.OpenSubKey(path);
        if (key?.GetValue("ServiceSession") is not string data) return null;
        var session = JsonSerializer.Deserialize(data, PreferencesJson.Default.TunnelSession);
        return session;
    }

    private static void WriteRecord(TunnelSession? session)
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = machine.CreateSubKey(AppRegistryPath);
        if (session is null) key.DeleteValue("ServiceSession", throwOnMissingValue: false);
        else key.SetValue("ServiceSession", JsonSerializer.Serialize(session, PreferencesJson.Default.TunnelSession));
        if (ReadSession(machine, LegacyRegistryPath)?.OwnerSid == CurrentUser().Value)
        {
            using var legacy = machine.OpenSubKey(LegacyRegistryPath, writable: true);
            legacy?.DeleteValue("ServiceSession", throwOnMissingValue: false);
        }
    }

    private static string? SafeConfigDirectory(string path)
    {
        if (!Path.IsPathFullyQualified(path) || Path.GetFileName(path) != "session.conf") return null;
        var directory = Path.GetDirectoryName(path);
        if (directory is null || !string.Equals(Path.GetDirectoryName(directory), ProgramData, StringComparison.OrdinalIgnoreCase))
            return null;
        var name = Path.GetFileName(directory);
        return IsManagedName("AppTunnel-") || IsManagedName("RotaProton-") ? directory : null;

        bool IsManagedName(string prefix) => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParseExact(name[prefix.Length..], "N", out _);
    }

    private static string? ServiceImage()
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var service = machine.OpenSubKey(ServiceRegistryPath);
        return service?.GetValue("ImagePath") as string;
    }

    private static bool OwnsService(TunnelSession session)
    {
        if (ServiceImage() is not { } command) return false;
        var argv = CommandLineToArgvW(command, out var count);
        if (argv == 0) return false;
        try
        {
            var executable = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv));
            if (!string.Equals(executable, session.EnginePath, StringComparison.OrdinalIgnoreCase)) return false;
            for (var i = 1; i < count - 1; i++)
            {
                var argument = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * nint.Size));
                if (argument != "-config") continue;
                var config = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, (i + 1) * nint.Size));
                return string.Equals(config, session.ConfigPath, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }
        finally { LocalFree(argv); }
    }

    private void RunTool(string executable, params string[] arguments)
    {
        var path = ValidateInstallation(executable);
        var info = new ProcessStartInfo(path)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(path)!
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException(T("WireSock could not start.", "O WireSock não iniciou.", "Не удалось запустить WireSock."));
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit((int)ServiceTimeout.TotalMilliseconds))
        {
            try { process.Kill(); } catch (InvalidOperationException) { }
            throw new System.TimeoutException(T("The WireSock command timed out.", "O comando do WireSock excedeu o tempo limite.", "Истекло время выполнения команды WireSock."));
        }
        foreach (var line in (output.GetAwaiter().GetResult() + Environment.NewLine + error.GetAwaiter().GetResult())
                     .Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var safe = Base64Key().Replace(line, "[key]");
            Output?.Invoke(safe.Length > 400 ? safe[..400] : safe);
        }
        if (process.ExitCode != 0)
            throw new InvalidOperationException(T($"WireSock returned exit code {process.ExitCode}. See Diagnostics.",
                $"O WireSock retornou o código {process.ExitCode}. Veja Diagnóstico.", $"WireSock завершился с кодом {process.ExitCode}. Подробности на вкладке «Диагностика»."));
    }

    private static Mutex LockOperations()
    {
        // Share the old lock while staged connections from the previous release can still be installed.
        var gate = new Mutex(false, @"Global\RotaProton.ServiceOperations");
        try
        {
            if (gate.WaitOne(TimeSpan.FromSeconds(1))) return gate;
        }
        catch (AbandonedMutexException) { return gate; }
        gate.Dispose();
        throw new InvalidOperationException(T("Another AppTunnel instance is changing the VPN service.",
            "Outra instância do AppTunnel está alterando o serviço da VPN.", "Другой экземпляр AppTunnel изменяет службу VPN."));
    }

    private static bool IsLiveProcess(Process process)
    {
        using (process) { return !process.HasExited; }
    }

    private static SecurityIdentifier CurrentUser()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User ?? throw new InvalidOperationException(T("Windows user identity is unavailable.",
            "A identidade do usuário do Windows está indisponível.", "Не удалось определить пользователя Windows."));
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern nint LocalFree(nint memory);

    [GeneratedRegex(@"[A-Za-z0-9+/]{43}=")]
    private static partial Regex Base64Key();

    public static string? Locate()
    {
        var roots = ProgramDirectories();
        var registryPaths = InstalledProductPaths().Where(x => roots.Any(root => IsUnder(x, root)));
        var names = new[]
        {
            "WireSock VPN Client", "WireSock Secure Connect", "WireSock VPN Client CLI",
            @"NT Kernel Resources\WireSock VPN Client", @"NT Kernel Resources\WireSock Secure Connect",
            @"NT Kernel Resources\WireSock VPN Client CLI", @"NT Kernel Resources\WireSock Secure Connect CLI"
        };
        var paths = registryPaths.Concat(roots.SelectMany(root => names.Select(name => Path.Combine(root, name))))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return paths.SelectMany(path => new[] {
                Path.Combine(path, "sdk", Executable),
                Path.Combine(path, Executable),
                Path.Combine(path, "bin", Executable)
            })
            .FirstOrDefault(File.Exists);
    }

    public static string ValidateInstallation(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Path.GetFileName(fullPath).Equals(Executable, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(fullPath) || !ProgramDirectories().Any(root => IsUnder(fullPath, root)))
        {
            throw new InvalidOperationException(T("Select wiresock-client.exe from the WireSock installation folder in Program Files.", "Selecione wiresock-client.exe na pasta em que o WireSock foi instalado em Arquivos de Programas.", "Выберите wiresock-client.exe в папке установки WireSock внутри Program Files."));
        }
        var version = FileVersionInfo.GetVersionInfo(fullPath);
        if (new Version(version.FileMajorPart, version.FileMinorPart, version.FileBuildPart, version.FilePrivatePart) < new Version(3, 8, 2, 1))
            throw new InvalidOperationException(T("Update WireSock Secure Connect CLI to version 3.8.2.1 or later.", "Atualize o WireSock Secure Connect CLI para a versão 3.8.2.1 ou superior.", "Обновите WireSock Secure Connect CLI до версии 3.8.2.1 или новее."));
        if (!string.Equals(version.OriginalFilename, Executable, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(version.CompanyName, "WireSock Foundation", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(T("Select the executable published by the WireSock Foundation.", "Selecione o executável publicado pela WireSock Foundation.", "Выберите исполняемый файл от WireSock Foundation."));
        Authenticode.Verify(fullPath);
        return fullPath;
    }

    private static string[] ProgramDirectories() =>
        new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }
            .Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static bool IsUnder(string path, string root) =>
        path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> InstalledProductPaths()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var uninstall = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) continue;
            foreach (var key in uninstall.GetSubKeyNames())
            {
                using var app = uninstall.OpenSubKey(key);
                if (app?.GetValue("DisplayName") is string name && name.StartsWith("WireSock", StringComparison.OrdinalIgnoreCase) &&
                    app.GetValue("InstallLocation") is string path && Path.IsPathFullyQualified(path))
                    yield return Path.GetFullPath(path);
            }
        }
    }

}

internal static class Authenticode
{
    public static void Verify(string path)
    {
        var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        var file = new FileInfoData { Size = (uint)Marshal.SizeOf<FileInfoData>(), Path = path };
        var data = new TrustData
        {
            Size = (uint)Marshal.SizeOf<TrustData>(),
            UiChoice = 2,
            UnionChoice = 1,
            ProviderFlags = 0x00001000,
            FileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfoData>())
        };
        try
        {
            Marshal.StructureToPtr(file, data.FileInfo, false);
            var result = WinVerifyTrust(new nint(-1), ref action, ref data);
            if (result != 0)
                throw new InvalidOperationException(T($"Windows could not validate the WireSock signature (0x{result:X8}). Reinstall the official WireSock client.", $"O Windows não validou a assinatura do WireSock (0x{result:X8}). Reinstale o WireSock oficial.", $"Windows не смогла проверить подпись WireSock (0x{result:X8}). Переустановите официальный клиент WireSock."));
        }
        finally
        {
            Marshal.DestroyStructure<FileInfoData>(data.FileInfo);
            Marshal.FreeHGlobal(data.FileInfo);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileInfoData
    {
        public uint Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string Path;
        public nint File, KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint Size;
        public nint PolicyCallback, SipClient;
        public uint UiChoice, RevocationChecks, UnionChoice;
        public nint FileInfo;
        public uint StateAction;
        public nint StateData, UrlReference;
        public uint ProviderFlags, UiContext;
        public nint SignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int WinVerifyTrust(nint window, ref Guid action, ref TrustData data);
}
