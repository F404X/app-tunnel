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
            foreach (var folder in roots.SelectMany(root => app.FolderNames.Select(name => Path.Combine(root, name))))
                InspectFolder(app, folder, targets);
        }

        foreach (var (app, folder) in RegisteredFolders())
            InspectFolder(app, folder, targets);

        foreach (var target in FindRunning(onlyCommon: true))
        {
            if (targets.Values.Any(existing => existing.Folder &&
                target.Match.StartsWith(existing.Match, StringComparison.OrdinalIgnoreCase))) continue;
            var app = CommonApps.FindByPath(target.Match) ?? CommonApps.Find(target.Label);
            if (app is null) continue;
            var rule = SurvivesUpdates(target with { Label = app.Name });
            if (targets.Values.Any(existing => existing.Folder &&
                rule.Match.StartsWith(existing.Match, StringComparison.OrdinalIgnoreCase))) continue;
            targets.TryAdd(rule.Match, rule);
        }

        // A launcher found in an install folder adds nothing once that whole folder is routed for the same app.
        var folders = targets.Values.Where(target => target.Folder).ToArray();
        return targets.Values.Where(target => target.Folder || !folders.Any(folder => folder.Label == target.Label &&
            target.Match.StartsWith(folder.Match, StringComparison.OrdinalIgnoreCase))).ToArray();
    }

    // Some apps, such as Opera and Opera GX, run from a folder named after their version (Opera\120.0.1\opera.exe),
    // which the next update replaces. A rule for that path would stop matching, so the install folder above the
    // version folder is routed instead.
    internal static ApplicationTarget SurvivesUpdates(ApplicationTarget target)
    {
        if (target.Folder || Path.GetDirectoryName(target.Match) is not { } directory ||
            !IsVersion(Path.GetFileName(directory)) || Path.GetDirectoryName(directory) is not { } install ||
            Path.GetPathRoot(install) == install)
            return target;
        var folder = ApplicationTarget.Validate(Path.TrimEndingDirectorySeparator(install) + Path.DirectorySeparatorChar);
        return target with { Match = folder, Folder = true };
    }

    private static bool IsVersion(string name) =>
        System.Text.RegularExpressions.Regex.IsMatch(name, @"^\d+(\.\d+)+$");

    internal static void InspectFolder(CommonApp app, string folder, Dictionary<string, ApplicationTarget> targets)
    {
        if (!Directory.Exists(folder)) return;
        try
        {
            // Version folders (OperaP.0.1) hold the real executable of apps whose root has only a launcher, or none.
            var versions = Directory.EnumerateDirectories(folder, "*", SearchOption.TopDirectoryOnly)
                .Where(path => IsVersion(Path.GetFileName(path))).Take(10);
            var candidates = Directory.EnumerateDirectories(folder, "app-*", SearchOption.TopDirectoryOnly)
                .Take(30).Concat(versions).Prepend(folder).Append(Path.Combine(folder, "bin"));
            foreach (var subfolder in candidates)
            {
                if (!Directory.Exists(subfolder)) continue;
                // The file's own spelling, so the rule shows chrome.exe rather than the catalog's "Chrome.exe".
                var executable = app.ExecutableNames
                    .Select(name => new DirectoryInfo(subfolder).EnumerateFiles(name + ".exe").FirstOrDefault()?.FullName)
                    .FirstOrDefault(path => path is not null);
                if (executable is null) continue;
                var ownFolder = CommonApps.Find(Path.GetFileName(Path.TrimEndingDirectorySeparator(folder))) == app;
                var match = ApplicationTarget.Validate(ownFolder
                    ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar
                    : executable);
                // An executable inside a version folder is routed by its install folder, which updates keep.
                var target = SurvivesUpdates(new ApplicationTarget(app.Name, match, Folder: ownFolder));
                if (targets.Values.Any(existing => existing.Folder && existing.Label == app.Name &&
                    target.Match.StartsWith(existing.Match, StringComparison.OrdinalIgnoreCase))) continue;
                targets.TryAdd(target.Match, target);
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
                    if (process.Id == Environment.ProcessId || (onlyCommon && !CommonApps.IsKnownExecutable(process.ProcessName)))
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
