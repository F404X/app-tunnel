using System.Diagnostics;
using static AppTunnel.Core.Localized;

namespace AppTunnel.Windows;

internal static class WireSockUpdater
{
    public const string PackageId = "NTKERNEL.WireSockVPNClientCLI";
    public const int NoUpdate = unchecked((int)0x8A15002B);
    public const int PackageNotFound = unchecked((int)0x8A150014);

    public static int Upgrade()
    {
        var winget = FindWinGet();
        return WireSockEngine.WithInactiveService(() =>
        {
            var info = new ProcessStartInfo(winget)
            {
                UseShellExecute = false, CreateNoWindow = true,
                WorkingDirectory = Environment.SystemDirectory
            };
            foreach (var argument in new[] { "upgrade", "--id", PackageId, "--exact", "--source", "winget",
                "--silent", "--disable-interactivity", "--accept-source-agreements" })
                info.ArgumentList.Add(argument);
            using var process = Process.Start(info) ?? throw WinGetUnavailable();
            process.WaitForExit();
            return process.ExitCode;
        });
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
