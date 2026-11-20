using System.Diagnostics;
using AppTunnel.Core;

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

        foreach (var target in FindRunning(onlyDiscord: true))
        {
            if (found.Any(folder => target.Match.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (found.Add(target.Match)) yield return target;
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
