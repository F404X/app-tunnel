using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AppTunnel.Core;

public enum ProtonProfileState { KeyRequired, Available, Unsupported }

public sealed record ProtonConfiguration(string Name, string Text);

public sealed record ProtonAccountProfile(string Name, string? Server, ProtonProfileState State, string? Fingerprint);

public sealed record ProtonAccountSnapshot(bool Ready, ProtonAccountProfile[] Profiles);

public sealed record ProtonPageProfile(string Name, string? Server, string? Configuration);

public sealed record ProtonPageScan(bool Ready, ProtonPageProfile[] Profiles, string? DownloadsUrl = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ProtonConfiguration))]
[JsonSerializable(typeof(ProtonAccountSnapshot))]
[JsonSerializable(typeof(ProtonPageScan))]
public partial class ProtonJson : JsonSerializerContext;

public static class ProtonSync
{
    public static ProtonProfileState Inspect(string? config, out string? fingerprint)
    {
        fingerprint = null;
        if (string.IsNullOrWhiteSpace(config))
            return ProtonProfileState.KeyRequired;
        if (config.Length > 65536)
            return ProtonProfileState.Unsupported;

        using var reader = new StringReader(config);
        for (var line = reader.ReadLine(); line is not null; line = reader.ReadLine())
        {
            var equals = line.IndexOf('=');
            if (equals < 0 || !line[..equals].Trim().Equals("PrivateKey", StringComparison.OrdinalIgnoreCase)) continue;
            var key = line[(equals + 1)..].Trim();
            if (key.Length == 0 || key.All(letter => letter == '*')) return ProtonProfileState.KeyRequired;
            break;
        }

        try
        {
            fingerprint = WireGuardProfile.Parse(config).Fingerprint();
            return ProtonProfileState.Available;
        }
        catch (FormatException)
        {
            return ProtonProfileState.Unsupported;
        }
    }

    public static string SafeName(string? name)
    {
        var clean = new string((name ?? "").Where(c => !char.IsControl(c)).Take(100).ToArray()).Trim();
        return clean.Length == 0 ? "Proton VPN" : clean;
    }

    public static string? GetDeviceName(string config)
    {
        using var reader = new StringReader(config);
        for (var line = reader.ReadLine(); line is not null; line = reader.ReadLine())
        {
            const string prefix = "# Key for ";
            if (line.StartsWith(prefix, StringComparison.Ordinal)) return SafeName(line[prefix.Length..]);
        }
        return null;
    }

    [SupportedOSPlatform("windows")]
    public static void Export(string path, ProtonConfiguration profile)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(profile, ProtonJson.Default.ProtonConfiguration);
        try { WriteAtomic(path, UserProtection.Transform(plain, encrypt: true)); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    [SupportedOSPlatform("windows")]
    public static ProtonConfiguration ReadExport(string path)
    {
        if (new FileInfo(path).Length > 512 * 1024)
            throw new FormatException(Localized.T("The Proton profile is too large.", "O perfil da Proton é muito grande.", "Профиль Proton слишком большой."));
        var plain = UserProtection.Transform(File.ReadAllBytes(path), encrypt: false);
        try
        {
            return JsonSerializer.Deserialize(plain, ProtonJson.Default.ProtonConfiguration)
                ?? throw new FormatException(Localized.T("The Proton profile is empty.", "O perfil da Proton está vazio.", "Профиль Proton пуст."));
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public static void WriteAtomic(string path, byte[] bytes)
    {
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                file.Write(bytes);
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }
}
