using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using static AppTunnel.Core.Localized;

namespace AppTunnel.Windows;

internal static class WireSockUpdater
{
    public const string PackageId = "NTKERNEL.WireSockVPNClientCLI";
    public const int NoUpdate = unchecked((int)0x8A15002B);
    public const int PackageNotFound = unchecked((int)0x8A150014);
    public const int AlreadyInstalled = unchecked((int)0x8A150061);
    private static readonly TimeSpan UpgradeTimeout = TimeSpan.FromMinutes(10);

    public sealed record Result(int ExitCode, Version? Installed, Version? Latest, bool Updated);

    // WinGet often cannot link the installed SDK to this package (it lists it as "WireSock Secure Connect SDK"
    // under an ARP id), so "winget upgrade" reports no package. The versions are compared here instead and the
    // newer release is installed over the current one.
    public static Result Upgrade()
    {
        var winget = FindWinGet();
        return WireSockEngine.WithInactiveService(() =>
        {
            var installed = InstalledVersion();
            var (code, output) = Run(winget, TimeSpan.FromMinutes(2), "show", "--id", PackageId, "--exact", "--source", "winget",
                "--disable-interactivity", "--accept-source-agreements");
            if (code != 0) return new Result(code, installed, null, false);
            var latest = LatestVersion(output);
            if (latest is null)
                throw new InvalidOperationException(T("WinGet did not report the latest WireSock version.",
                    "O WinGet não informou a versão mais recente do WireSock.", "WinGet не сообщил последнюю версию WireSock."));
            if (installed is not null && installed >= latest) return new Result(NoUpdate, installed, latest, false);
            (code, _) = Run(winget, UpgradeTimeout, "install", "--id", PackageId, "--exact", "--source", "winget", "--silent",
                "--disable-interactivity", "--accept-source-agreements", "--accept-package-agreements");
            var after = InstalledVersion();
            var updated = after is not null && (installed is null || after > installed);
            return new Result(updated ? 0 : code == 0 ? AlreadyInstalled : code, after ?? installed, latest, updated);
        });
    }

    private static Version? InstalledVersion()
    {
        var path = WireSockEngine.Locate();
        if (path is null) return null;
        var info = FileVersionInfo.GetVersionInfo(path);
        return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
    }

    // "winget show" labels follow the Windows language, so the first "label: 1.2.3" line is taken as the version.
    internal static Version? LatestVersion(string output)
    {
        foreach (var line in output.Split('\n'))
        {
            var match = Regex.Match(line, @"^\s*[^\s:][^:]*:\s*v?(\d+(?:\.\d+){1,3})\s*$");
            if (match.Success && Version.TryParse(match.Groups[1].Value, out var version)) return version;
        }
        return null;
    }

    private static (int ExitCode, string Output) Run(string winget, TimeSpan timeout, params string[] arguments)
    {
        var info = new ProcessStartInfo(winget)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8, WorkingDirectory = Environment.SystemDirectory
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw WinGetUnavailable();
        var output = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        // Bound the wait: the service-operation mutex is held for the whole upgrade.
        if (!process.WaitForExit(timeout))
        {
            try { process.Kill(entireProcessTree: true); }
            catch (Exception error) when (error is InvalidOperationException or Win32Exception or NotSupportedException) { }
            throw new TimeoutException(T("WinGet did not finish the WireSock update in time and was stopped.",
                "O WinGet não concluiu a atualização do WireSock a tempo e foi interrompido.",
                "WinGet не завершил обновление WireSock вовремя и был остановлен."));
        }
        process.WaitForExit();
        return (process.ExitCode, output.GetAwaiter().GetResult());
    }

    private static string FindWinGet()
    {
        var system = Environment.SystemDirectory;
        var executable = Path.Combine(system, @"WindowsPowerShell\v1.0\powershell.exe");
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            WorkingDirectory = system
        };
        const string query = """
            $ErrorActionPreference = 'Stop'
            Import-Module (Join-Path $PSHOME 'Modules\Appx\Appx.psd1')
            Appx\Get-AppxPackage -Name Microsoft.DesktopAppInstaller |
                Where-Object PublisherId -eq '8wekyb3d8bbwe' |
                Sort-Object Version -Descending | Select-Object -First 1 -ExpandProperty InstallLocation
            """;
        foreach (var argument in new[] { "-NoProfile", "-NoLogo", "-NonInteractive", "-Command", query })
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw WinGetUnavailable();
        var location = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(15000))
        {
            try { process.Kill(); } catch (InvalidOperationException) { }
            throw WinGetUnavailable();
        }
        var packageRoot = location.GetAwaiter().GetResult().Trim();
        if (process.ExitCode != 0 || !Path.IsPathFullyQualified(packageRoot)) throw WinGetUnavailable();
        var candidate = Path.GetFullPath(Path.Combine(packageRoot, "winget.exe"));
        var windowsApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
        var directory = Path.GetDirectoryName(candidate)!;
        var package = Path.GetFileName(directory);
        if (package is null || !package.StartsWith("Microsoft.DesktopAppInstaller_", StringComparison.OrdinalIgnoreCase) ||
            !package.EndsWith("_8wekyb3d8bbwe", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetDirectoryName(directory), windowsApps, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate))
            throw WinGetUnavailable();
        Authenticode.Verify(candidate, "WinGet");
        return candidate;
    }

    private static InvalidOperationException WinGetUnavailable() => new(T(
        "Install or update Microsoft App Installer to use WinGet.", "Instale ou atualize o Instalador de Aplicativo da Microsoft para usar o WinGet.",
        "Установите или обновите «Установщик приложений» Microsoft, чтобы использовать WinGet."));
}
