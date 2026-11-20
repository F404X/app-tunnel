namespace AppTunnel.Core;

public static class ProfileMigration
{
    public static void CopyLegacyProfiles(string legacy, string root)
    {
        if (Directory.Exists(root) || !Directory.Exists(legacy) ||
            (File.GetAttributes(legacy) & FileAttributes.ReparsePoint) != 0) return;

        var staged = $"{root}.import-{Guid.NewGuid():N}";
        Directory.CreateDirectory(staged);
        try
        {
            foreach (var file in Directory.EnumerateFiles(legacy))
            {
                var name = Path.GetFileName(file);
                if (name != "settings.json" && !(Path.GetExtension(name) == ".dpapi" &&
                    Guid.TryParseExact(Path.GetFileNameWithoutExtension(name), "N", out _))) continue;
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                File.Copy(file, Path.Combine(staged, name));
            }
            Directory.Move(staged, root);
        }
        finally
        {
            if (Directory.Exists(staged)) Directory.Delete(staged, recursive: true);
        }
    }
}
