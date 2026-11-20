using System.Diagnostics;
using AppTunnel.Core;
using Microsoft.Win32;

namespace AppTunnel.Windows;

internal static class ApplicationDiscovery
{
    public static IEnumerable<ApplicationTarget> FindInstalled()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roots = new[]
        {
            local, Path.Combine(local, "Programs"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.OrdinalIgnoreCase);
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var client in DiscordClients.KnownNames)
        {
            foreach (var root in roots)
            {
                var folder = Path.Combine(root, client);
                if (Directory.Exists(folder) && found.Add(folder))
                    yield return new ApplicationTarget(client, folder + Path.DirectorySeparatorChar, Folder: true);
            }
        }

        foreach (var target in FindRegistered())
        {
            if (found.Add(target.Match.TrimEnd(Path.DirectorySeparatorChar))) yield return target;
        }

        foreach (var target in FindRunning(onlyDiscord: true))
        {
            if (found.Any(folder => target.Match.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (found.Add(target.Match)) yield return target;
        }
    }

    private static IEnumerable<ApplicationTarget> FindRegistered()
    {
        const string uninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(uninstallPath);
                if (uninstall is null) continue;
                foreach (var key in uninstall.GetSubKeyNames())
                {
                    using var entry = uninstall.OpenSubKey(key);
                    if (entry?.GetValue("DisplayName") is not string name || !DiscordClients.IsKnown(name) ||
                        entry.GetValue("InstallLocation") is not string folder || !Path.IsPathFullyQualified(folder) ||
                        !Directory.Exists(folder)) continue;
                    string match;
                    try { match = ApplicationTarget.Validate(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar); }
                    catch (FormatException) { continue; }
                    yield return new ApplicationTarget(name, match, Folder: true);
                }
            }
        }
    }

    public static IReadOnlyList<ApplicationTarget> FindRunning(bool onlyDiscord = false)
    {
        var results = new Dictionary<string, ApplicationTarget>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var name = process.ProcessName;
                    if ((onlyDiscord && !DiscordClients.IsKnown(name)) ||
                        process.Id == Environment.ProcessId) continue;
                    var path = process.MainModule?.FileName;
                    if (path is null || !File.Exists(path)) continue;
                    var validated = ApplicationTarget.Validate(path);
                    results.TryAdd(validated, new ApplicationTarget(Path.GetFileNameWithoutExtension(validated), validated));
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or
                    NotSupportedException or FormatException or UnauthorizedAccessException) { }
            }
        }
        return results.Values.OrderBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}
