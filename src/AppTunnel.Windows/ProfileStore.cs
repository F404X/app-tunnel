using static AppTunnel.Core.Localized;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AppTunnel.Core;

namespace AppTunnel.Windows;

internal sealed record StoredProfile(Guid Id, string Name, string Endpoint, bool HasIpv6, string? Fingerprint = null, VpnProtocol Protocol = VpnProtocol.WireGuard)
{
    public override string ToString() => Name;
}

internal sealed class Preferences
{
    public List<ApplicationTarget> Applications { get; set; } = [];
    public List<StoredProfile> Profiles { get; set; } = [];
    public Guid SelectedProfile { get; set; }
    public RouteOptions Route { get; set; } = new();
    public string EnginePath { get; set; } = "";
    public bool DiagnosticLogs { get; set; }
    // A first run follows the Windows display language; saved settings keep the user's choice.
    public string Language { get; set; } = AppTunnel.Core.Localized.Language;
    public ProtocolPreference Protocol { get; set; }
    public ThemeMode Theme { get; set; }
    public bool AutoUpdate { get; set; }
    public DateTimeOffset? LastAutoUpdateCheck { get; set; }
    public bool AutoApply { get; set; } = true;
    public DiscordRestartChoice DiscordRestart { get; set; }
    // Connect through the route with the lowest measured latency instead of a fixed one.
    public bool AutoServer { get; set; }
    public bool SetupDone { get; set; }
}

internal enum DiscordRestartChoice
{
    Ask,
    Always,
    Never
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Preferences))]
[JsonSerializable(typeof(TunnelSession))]
internal partial class PreferencesJson : JsonSerializerContext;

internal sealed class ProfileStore
{
    private readonly string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AppTunnel");
    private static readonly UTF8Encoding Utf8 = new(false, true);

    private string SettingsFile => Path.Combine(root, "settings.json");
    private string ProfileFile(Guid id) => Path.Combine(root, id.ToString("N") + ".dpapi");
    internal string ProtonRoot => Path.Combine(root, "Proton");

    public ProfileStore()
    {
        ProfileMigration.CopyLegacyProfiles(Path.Combine(Path.GetDirectoryName(root)!, "RotaProton"), root);
        Directory.CreateDirectory(root);
        File.Delete(SettingsFile + ".tmp");
    }

    public Preferences Load()
    {
        if (!File.Exists(SettingsFile)) return new Preferences();
        return JsonSerializer.Deserialize(File.ReadAllBytes(SettingsFile), PreferencesJson.Default.Preferences)
            ?? throw new FormatException(T("AppTunnel settings are empty.", "As configurações do AppTunnel estão vazias.", "Файл настроек AppTunnel пуст."));
    }

    public void Save(Preferences preferences) =>
        WriteAtomic(SettingsFile, JsonSerializer.SerializeToUtf8Bytes(preferences, PreferencesJson.Default.Preferences));

    public StoredProfile Import(string source, string? name = null)
    {
        using var input = File.OpenRead(source);
        if (input.Length > 65536) throw new FormatException(T("The WireGuard profile exceeds 64 KiB.", "O perfil WireGuard excede 64 KiB.", "Размер профиля WireGuard превышает 64 КиБ."));
        using var reader = new StreamReader(input, Utf8, detectEncodingFromByteOrderMarks: true);
        return ImportText(reader.ReadToEnd(), name ?? Path.GetFileNameWithoutExtension(source));
    }

    public StoredProfile? ImportProton(ProtonConfiguration config, List<StoredProfile> profiles)
    {
        var profile = WireGuardProfile.Parse(config.Text);
        var fingerprint = profile.Fingerprint();
        foreach (var existing in profiles)
        {
            if (existing.Fingerprint == fingerprint) return null;
            if (existing.Fingerprint is not null || !File.Exists(ProfileFile(existing.Id))) continue;
            var previous = TryReadFingerprint(existing.Id);
            if (previous == fingerprint) return null;
        }
        return ImportText(config.Text, config.Name, profile, fingerprint);
    }

    private string? TryReadFingerprint(Guid id)
    {
        try { return Read(id).Fingerprint(); }
        catch (Exception error) when (error is IOException or FormatException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private StoredProfile ImportText(string raw, string name, WireGuardProfile? parsed = null, string? fingerprint = null)
    {
        var profile = parsed ?? WireGuardProfile.Parse(raw);
        var id = Guid.NewGuid();
        var plain = Utf8.GetBytes(raw);
        try
        {
            WriteAtomic(ProfileFile(id), UserProtection.Transform(plain, encrypt: true));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
        return new StoredProfile(id, name, profile.Endpoint, profile.HasIpv6, fingerprint ?? profile.Fingerprint(), profile.Protocol);
    }

    public WireGuardProfile Read(Guid id)
    {
        var raw = UserProtection.Transform(File.ReadAllBytes(ProfileFile(id)), encrypt: false);
        try { return WireGuardProfile.Parse(Utf8.GetString(raw)); }
        finally { CryptographicOperations.ZeroMemory(raw); }
    }

    public void Remove(Guid id) => File.Delete(ProfileFile(id));

    private static void WriteAtomic(string path, byte[] bytes)
    {
        var temporary = path + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                file.Write(bytes);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}

