using System.Globalization;
using System.Net;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace AppTunnel.Core;

// One Proton server that can carry a WireGuard connection, as listed by the Proton API.
public sealed record ProtonServerChoice(string LogicalId, string Name, string Country, string? City, int Tier, int Load,
    double Score, string EntryIp, string? EntryIpv6, string PublicKey, string? Label = null);

// A request from the main window to the background Proton helper.
public sealed record ProtonApiCommand(string Action, ProtonServerChoice? Server = null, string? DeviceName = null)
{
    public const string ListServers = "servers";
    public const string CreateRoute = "create";
}

public sealed record ProtonApiResult(bool SignInRequired = false, string? Error = null, int MaxTier = 0,
    ProtonServerChoice[]? Servers = null, string? Created = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ProtonApiCommand))]
[JsonSerializable(typeof(ProtonApiResult))]
public partial class ProtonRouteJson : JsonSerializerContext;

public static class ProtonRoutes
{
    // Rejects anything from the API that would not make a valid, safe WireGuard peer.
    public static bool IsUsable(ProtonServerChoice? server) =>
        server is not null && server.Name?.Length is > 0 and <= 100 && server.LogicalId?.Length is > 0 and <= 200 &&
        server.Country is { Length: <= 3 } && (server.City is null || server.City.Length <= 100) &&
        (server.Label is null || server.Label.Length <= 100 && server.Label.All(char.IsAsciiLetterOrDigit)) &&
        IPAddress.TryParse(server.EntryIp, out var entry) && entry.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
        (server.EntryIpv6 is null || IPAddress.TryParse(server.EntryIpv6, out var v6) && v6.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6) &&
        IsKey(server.PublicKey);

    // Proton returns registered keys as PEM (an SPKI block holding the raw 32-byte Ed25519 key); this compares
    // that against the raw base64 key that was sent.
    public static bool SameKey(string? returned, string sent)
    {
        if (string.IsNullOrWhiteSpace(returned) || !IsKey(sent)) return false;
        var body = string.Concat(returned.Split((char)10).Select(line => line.Trim()).Where(line => !line.StartsWith("-----", StringComparison.Ordinal)));
        byte[] bytes;
        try { bytes = Convert.FromBase64String(body); }
        catch (FormatException) { return false; }
        ReadOnlySpan<byte> spki = [0x30, 0x2a, 0x30, 0x05, 0x06, 0x03, 0x2b, 0x65, 0x70, 0x03, 0x21, 0x00];
        var raw = bytes.Length == 44 && bytes.AsSpan(0, 12).SequenceEqual(spki) ? bytes[12..] : bytes.Length == 32 ? bytes : null;
        return raw is not null && raw.AsSpan().SequenceEqual(Convert.FromBase64String(sent));
    }

    public static bool IsKey(string? value)
    {
        if (value is null || value.Length != 44) return false;
        Span<byte> bytes = stackalloc byte[33];
        return Convert.TryFromBase64String(value, bytes, out var written) && written == 32;
    }

    // Same template as Proton's own WireGuard configuration page.
    public static string BuildConfig(string deviceName, string privateKey, ProtonServerChoice server)
    {
        var ipv6 = server.EntryIpv6 is not null;
        var text = new StringBuilder();
        text.Append("[Interface]\n");
        text.Append("# Key for ").Append(ProtonSync.SafeName(deviceName)).Append('\n');
        text.Append("PrivateKey = ").Append(privateKey).Append('\n');
        text.Append("Address = 10.2.0.2/32").Append(ipv6 ? ", 2a07:b944::2:2/128" : "").Append('\n');
        text.Append("DNS = 10.2.0.1").Append(ipv6 ? ", 2a07:b944::2:1" : "").Append("\n\n");
        text.Append("[Peer]\n");
        text.Append("# ").Append(ProtonSync.SafeName(server.Name)).Append('\n');
        text.Append("PublicKey = ").Append(server.PublicKey).Append('\n');
        text.Append("AllowedIPs = 0.0.0.0/0, ::/0\n");
        text.Append("Endpoint = ").Append(server.EntryIp).Append(":51820\n");
        text.Append("PersistentKeepalive = 25\n");
        return text.ToString();
    }
}

// Key pair in the form Proton's web page uses: an Ed25519 public key is registered, and the WireGuard (X25519)
// private key is the clamped SHA-512 of the same seed, so both describe one secret scalar.
public sealed class ProtonKeyPair
{
    public string PublicKey { get; }
    public string WireGuardPrivateKey { get; }

    private ProtonKeyPair(string publicKey, string wireGuardPrivateKey)
    {
        PublicKey = publicKey;
        WireGuardPrivateKey = wireGuardPrivateKey;
    }

    public static ProtonKeyPair Create()
    {
        var seed = RandomNumberGenerator.GetBytes(32);
        try { return FromSeed(seed); }
        finally { CryptographicOperations.ZeroMemory(seed); }
    }

    public static ProtonKeyPair FromSeed(ReadOnlySpan<byte> seed)
    {
        if (seed.Length != 32) throw new ArgumentException("The seed must have 32 bytes.", nameof(seed));
        var hash = SHA512.HashData(seed);
        try
        {
            hash[0] &= 0xf8;
            hash[31] &= 0x7f;
            hash[31] |= 0x40;
            var scalar = new BigInteger(hash.AsSpan(0, 32), isUnsigned: true, isBigEndian: false);
            var publicKey = Ed25519.Encode(Ed25519.Multiply(scalar, Ed25519.Base));
            return new ProtonKeyPair(Convert.ToBase64String(publicKey), Convert.ToBase64String(hash, 0, 32));
        }
        finally { CryptographicOperations.ZeroMemory(hash); }
    }

    // Minimal Ed25519 point arithmetic (RFC 8032) used only to derive the public key; nothing is signed.
    private static class Ed25519
    {
        private static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;
        private static readonly BigInteger D = Mod(-121665 * Inverse(121666));
        public static readonly (BigInteger X, BigInteger Y, BigInteger Z, BigInteger T) Base = MakeBase();

        private static BigInteger Mod(BigInteger value)
        {
            var result = value % P;
            return result.Sign < 0 ? result + P : result;
        }

        private static BigInteger Inverse(BigInteger value) => BigInteger.ModPow(Mod(value), P - 2, P);

        private static (BigInteger, BigInteger, BigInteger, BigInteger) MakeBase()
        {
            var y = Mod(4 * Inverse(5));
            var xx = Mod((y * y - 1) * Inverse(D * y * y + 1));
            var x = BigInteger.ModPow(xx, (P + 3) / 8, P);
            if (Mod(x * x - xx) != 0) x = Mod(x * BigInteger.ModPow(2, (P - 1) / 4, P));
            if (!x.IsEven) x = P - x;
            return (x, y, 1, Mod(x * y));
        }

        private static (BigInteger X, BigInteger Y, BigInteger Z, BigInteger T) Add(
            (BigInteger X, BigInteger Y, BigInteger Z, BigInteger T) p, (BigInteger X, BigInteger Y, BigInteger Z, BigInteger T) q)
        {
            var a = Mod((p.Y - p.X) * (q.Y - q.X));
            var b = Mod((p.Y + p.X) * (q.Y + q.X));
            var c = Mod(2 * D * p.T * q.T);
            var d = Mod(2 * p.Z * q.Z);
            var e = b - a;
            var f = d - c;
            var g = d + c;
            var h = b + a;
            return (Mod(e * f), Mod(g * h), Mod(f * g), Mod(e * h));
        }

        public static (BigInteger X, BigInteger Y, BigInteger Z, BigInteger T) Multiply(BigInteger scalar,
            (BigInteger X, BigInteger Y, BigInteger Z, BigInteger T) point)
        {
            (BigInteger X, BigInteger Y, BigInteger Z, BigInteger T) result = (0, 1, 1, 0);
            while (scalar > 0)
            {
                if (!scalar.IsEven) result = Add(result, point);
                point = Add(point, point);
                scalar >>= 1;
            }
            return result;
        }

        public static byte[] Encode((BigInteger X, BigInteger Y, BigInteger Z, BigInteger T) point)
        {
            var inverse = Inverse(point.Z);
            var x = Mod(point.X * inverse);
            var y = Mod(point.Y * inverse);
            var bytes = new byte[32];
            y.TryWriteBytes(bytes, out _, isUnsigned: true, isBigEndian: false);
            if (!x.IsEven) bytes[31] |= 0x80;
            return bytes;
        }
    }
}

public static class ProtonServerNames
{
    // "NL-FREE#12" and "US-NY#3" start with the country; the badge uses those letters.
    public static string CountryOf(string name) =>
        name.Length >= 2 && char.IsAsciiLetter(name[0]) && char.IsAsciiLetter(name[1]) && (name.Length == 2 || !char.IsAsciiLetter(name[2]))
            ? name[..2].ToUpperInvariant() : "";

    private static readonly Lazy<Dictionary<string, string[]>> Names = new(LoadNames);

    // Country names follow the AppTunnel language, not the Windows display language.
    public static string CountryName(string code)
    {
        if (!Names.Value.TryGetValue(code, out var names)) return code;
        return Localized.Language switch { "pt" => names[1], "ru" => names[2], _ => names[0] };
    }

    private static Dictionary<string, string[]> LoadNames()
    {
        var names = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        using var stream = typeof(ProtonServerNames).Assembly.GetManifestResourceStream("AppTunnel.Core.CountryNames");
        if (stream is null) return names;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        for (var line = reader.ReadLine(); line is not null; line = reader.ReadLine())
        {
            var parts = line.Split('|');
            if (parts.Length == 4) names[parts[0]] = parts[1..];
        }
        return names;
    }
}
