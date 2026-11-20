using System.ComponentModel;
using System.Diagnostics;
using AppTunnel.Core;

namespace AppTunnel.Windows;

internal sealed record DiscordClient(string Name, string Path);

internal static class DiscordRestart
{
    private static readonly string[] ProcessNames = ["Discord", "DiscordPTB", "DiscordCanary"];
    private static readonly int Session = Process.GetCurrentProcess().SessionId;

    public static DiscordClient[] FindRunning(IReadOnlyCollection<ApplicationTarget> rules)
    {
        var found = new Dictionary<string, DiscordClient>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in ProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    if (ProcessPath(process) is not { } path || found.ContainsKey(path) ||
                        !DiscordClients.IsRouted(path, rules)) continue;
                    var official = DiscordClients.OfficialName(Path.GetFileName(path));
                    if (official is not null && IsDiscordInstallation(path, name)) found.Add(path, new(official, path));
                }
            }
        }
        return found.Values.ToArray();
    }

    public static async Task CloseAsync(DiscordClient client)
    {
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(client.Path));
        try
        {
            var matching = processes.Where(process =>
                string.Equals(ProcessPath(process), client.Path, StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (var process in matching)
            {
                try { process.CloseMainWindow(); }
                catch (InvalidOperationException) { }
            }
            try
            {
                await Task.WhenAll(matching.Select(process => process.WaitForExitAsync()))
                    .WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                return;
            }
            catch (TimeoutException) { }

            foreach (var process in matching)
            {
                try { if (!process.HasExited) process.Kill(); }
                catch (InvalidOperationException) { }
                catch (Win32Exception) when (process.HasExited) { }
            }
            await Task.WhenAll(matching.Select(process => process.WaitForExitAsync()))
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    private static string? ProcessPath(Process process)
    {
        try { return process.SessionId == Session ? process.MainModule?.FileName : null; }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool IsDiscordInstallation(string path, string name)
    {
        var folder = Path.GetDirectoryName(path);
        var squirrelRoot = folder is not null ? Path.GetDirectoryName(folder) : null;
        if (squirrelRoot is not null && Path.GetFileName(squirrelRoot).Equals(name, StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(folder)!.StartsWith("app-", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(Path.Combine(squirrelRoot, "Update.exe"))) return true;

        try
        {
            var company = FileVersionInfo.GetVersionInfo(path).CompanyName?.Trim();
            return company is "Discord Inc." or "Discord Inc" or "Discord";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return false;
        }
    }
}
