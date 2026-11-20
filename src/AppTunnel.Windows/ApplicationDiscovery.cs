using System.Diagnostics;
using AppTunnel.Core;
using Microsoft.Win32;

namespace AppTunnel.Windows;

internal static class ApplicationDiscovery
{
    public static IReadOnlyList<ApplicationTarget> FindCommonApps()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roots = new[]
        {
            local, Path.Combine(local, "Programs"), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(path => !string.IsNullOrEmpty(path)).Distinct(StringComparer.OrdinalIgnoreCase);

        var targets = new Dictionary<string, ApplicationTarget>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in CommonApps.Catalog)
        {
            foreach (var folder in roots.SelectMany(root => app.Names.Select(name => Path.Combine(root, name))))
                InspectFolder(app, folder, targets);
        }

        foreach (var (app, folder) in RegisteredFolders())
            InspectFolder(app, folder, targets);

        foreach (var target in FindRunning(onlyCommon: true))
        {
            if (targets.Values.Any(existing => existing.Folder &&
                target.Match.StartsWith(existing.Match, StringComparison.OrdinalIgnoreCase))) continue;
            var app = CommonApps.Find(target.Label);
            if (app is not null) targets.TryAdd(target.Match, target with { Label = app.Name });
        }

        return targets.Values.ToArray();
    }

    private static void InspectFolder(CommonApp app, string folder, Dictionary<string, ApplicationTarget> targets)
    {
        if (!Directory.Exists(folder)) return;
        try
        {
            var candidates = Directory.EnumerateDirectories(folder, "app-*", SearchOption.TopDirectoryOnly)
                .Take(30).Prepend(folder).Append(Path.Combine(folder, "bin"));
            foreach (var subfolder in candidates)
            {
                var executable = app.Names.Select(name => Path.Combine(subfolder, name + ".exe")).FirstOrDefault(File.Exists);
                if (executable is null) continue;
                var ownFolder = CommonApps.Find(Path.GetFileName(Path.TrimEndingDirectorySeparator(folder))) == app;
                var match = ApplicationTarget.Validate(ownFolder
                    ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar
                    : executable);
                targets.TryAdd(match, new ApplicationTarget(app.Name, match, Folder: ownFolder));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException) { }
    }

    private static IEnumerable<(CommonApp App, string Folder)> RegisteredFolders()
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
                    if (entry?.GetValue("DisplayName") is not string name || CommonApps.Find(name) is not { } app ||
                        entry.GetValue("InstallLocation") is not string folder || !Path.IsPathFullyQualified(folder))
                        continue;
                    yield return (app, folder);
                }
            }
        }
    }

    public static IReadOnlyList<ApplicationTarget> FindRunning(bool onlyCommon = false)
    {
        var results = new Dictionary<string, ApplicationTarget>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == Environment.ProcessId || (onlyCommon && CommonApps.Find(process.ProcessName) is null))
                        continue;
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
