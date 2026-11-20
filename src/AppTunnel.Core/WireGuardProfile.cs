using static AppTunnel.Core.Localized;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace AppTunnel.Core;

public sealed partial class WireGuardProfile
{
    private const int MaxContentSize = 65536;
    private static readonly string[] InterfaceKeys = ["PrivateKey", "Address", "DNS", "MTU"];
    private static readonly string[] PeerKeys = ["PublicKey", "PresharedKey", "AllowedIPs", "Endpoint", "PersistentKeepalive"];
    private readonly Dictionary<string, string> local = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> peer = new(StringComparer.OrdinalIgnoreCase);

    private WireGuardProfile() { }

    public string Endpoint => peer["Endpoint"];
    public string Host => Endpoint.StartsWith('[') ? Endpoint[1..Endpoint.IndexOf(']')] : Endpoint[..Endpoint.LastIndexOf(':')];
    public string[] Addresses => SplitList(local["Address"]);
    public string[] AllowedNetworks => SplitList(peer["AllowedIPs"]);
    public bool HasIpv6 => Addresses.Any(x => x.Contains(':')) && AllowedNetworks.Any(x => x.Contains(':'));

    public string Fingerprint()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(local);
        hash.AppendData([0]);
        Append(peer);
        return Convert.ToHexStringLower(hash.GetHashAndReset());

        void Append(Dictionary<string, string> section)
        {
            foreach (var field in section.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes($"{field.Key.ToLowerInvariant()}={field.Value}\n");
                try { hash.AppendData(bytes); }
                finally { CryptographicOperations.ZeroMemory(bytes); }
            }
        }
    }

    public static WireGuardProfile Parse(string contents)
    {
        if (contents.Length > MaxContentSize)
            throw new FormatException(T("The WireGuard profile exceeds 64 KiB.", "O perfil WireGuard excede 64 KiB.", "Размер профиля WireGuard превышает 64 КиБ."));

        var profile = new WireGuardProfile();
        Dictionary<string, string>? section = null;
        string[] validKeys = [];
        var hasInterface = false;
        var hasPeer = false;
        using var reader = new StringReader(contents.TrimStart('\uFEFF'));
        for (string? raw = reader.ReadLine(); raw != null; raw = reader.ReadLine())
        {
            var comment = raw.IndexOfAny(['#', ';']);
            var line = (comment < 0 ? raw : raw[..comment]).Trim();
            if (line.Length == 0) continue;
            if (line.Equals("[Interface]", StringComparison.OrdinalIgnoreCase) && !hasInterface && !hasPeer)
            {
                hasInterface = true;
                section = profile.local;
                validKeys = [.. InterfaceKeys, .. PaddingFields, .. AmneziaFields];
                continue;
            }
            if (line.Equals("[Peer]", StringComparison.OrdinalIgnoreCase) && hasInterface && !hasPeer)
            {
                hasPeer = true;
                section = profile.peer;
                validKeys = PeerKeys;
                continue;
            }

            var separator = line.IndexOf('=');
            if (section is null || separator <= 0)
                throw new FormatException(T("Use a WireGuard profile with one [Interface] and one [Peer] section.", "Use um perfil WireGuard com uma seção [Interface] e um único [Peer].", "В профиле WireGuard должно быть по одному разделу [Interface] и [Peer]."));

            var name = line[..separator].Trim();
            if (!validKeys.Contains(name, StringComparer.OrdinalIgnoreCase))
                throw new FormatException(T("The file contains an unsupported WireGuard setting.", "O arquivo contém uma opção fora do formato WireGuard suportado.", "Файл содержит неподдерживаемый параметр WireGuard."));
            if (!section.TryAdd(name, line[(separator + 1)..].Trim()))
                throw new FormatException(T("The file contains a duplicate WireGuard setting.", "O arquivo repete uma opção do WireGuard.", "В файле повторяется параметр WireGuard."));
        }

        NormalizeKey(profile.local, "PrivateKey");
        NormalizeKey(profile.peer, "PublicKey");
        if (profile.peer.ContainsKey("PresharedKey")) NormalizeKey(profile.peer, "PresharedKey");

        var addresses = SplitList(Required(profile.local, "Address"));
        if (addresses.Length == 0 || addresses.Any(x => !IsInterfaceAddress(x)))
            throw new FormatException(T("Address must contain IPv4 or IPv6 addresses with a prefix.", "Address deve conter endereços IPv4 ou IPv6 com prefixo.", "В Address укажите адреса IPv4 или IPv6 с длиной префикса."));
        profile.local["Address"] = string.Join(", ", addresses);

        if (profile.local.TryGetValue("DNS", out var dns))
        {
            var servers = SplitList(dns);
            if (servers.Length == 0 || servers.Any(x => !TryIp(x, out _)))
                throw new FormatException(T("DNS must contain IP addresses.", "DNS deve conter endereços IP.", "В DNS укажите IP-адреса."));
            profile.local["DNS"] = string.Join(", ", servers);
        }

        profile.peer["AllowedIPs"] = string.Join(", ", ParseNetworks(Required(profile.peer, "AllowedIPs"), false));
        CheckEndpoint(Required(profile.peer, "Endpoint"));
        CheckNumber(profile.local, "MTU", 576, 65535);
        CheckNumber(profile.peer, "PersistentKeepalive", 0, 65535);
        profile.ValidateProtocol();
        return profile;
    }

    public string BuildClientConfig(IEnumerable<string> applications, RouteOptions options) =>
        BuildSessionConfig(applications, options, Protocol).Text;

    public VpnSessionConfig BuildSessionConfig(IEnumerable<string> applications, RouteOptions options, VpnProtocol protocol)
    {
        var apps = applications.Select(ApplicationTarget.Validate).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (apps.Length == 0)
            throw new InvalidOperationException(T("Select at least one app to use the VPN.", "Selecione pelo menos um programa para usar a VPN.", "Выберите хотя бы одно приложение для VPN."));

        var imported = AllowedNetworks.Select(IPNetwork.Parse).ToArray();
        var routes = string.IsNullOrWhiteSpace(options.Include)
            ? imported
            : ParseNetworks(options.Include, true).Select(IPNetwork.Parse).ToArray();
        if (routes.Any(route => !imported.Any(allowed =>
            route.PrefixLength >= allowed.PrefixLength && allowed.Contains(route.BaseAddress))))
            throw new FormatException(T("VPN destinations must be within AllowedIPs in the imported profile.", "Os destinos da VPN precisam estar contidos em AllowedIPs do perfil importado.", "Адреса для VPN должны входить в AllowedIPs импортированного профиля."));

        var allowsV6 = options.UseIpv6 && HasIpv6;
        routes = routes.Where(x => allowsV6 || x.BaseAddress.AddressFamily == AddressFamily.InterNetwork).ToArray();
        if (routes.Length == 0)
            throw new FormatException(T("The profile and IP settings leave no VPN destinations.", "O perfil e as opções de IP não deixaram nenhum destino para a VPN.", "После применения настроек IP не осталось адресов для VPN."));

        var explicitExclusions = ParseNetworks(options.Exclude, true).Select(IPNetwork.Parse).ToArray();
        var excluded = explicitExclusions.ToList();
        if (options.DirectLan)
            excluded.AddRange(LanNetworks.Select(IPNetwork.Parse));
        if (routes.All(route => excluded.Any(block =>
            route.PrefixLength >= block.PrefixLength && block.Contains(route.BaseAddress))))
            throw new FormatException(T("The exclusions cover all VPN destinations.", "As exclusões abrangem todos os destinos da VPN.", "Исключения охватывают все адреса для VPN."));

        var lines = new List<string> { "[Interface]" };
        foreach (var key in InterfaceKeys)
        {
            if (key == "DNS" || !local.TryGetValue(key, out var value)) continue;
            lines.Add($"{key} = {value}");
        }

        if (options.TunnelDns)
        {
            var dns = local.TryGetValue("DNS", out var addresses) ? SplitList(addresses).Select(IPAddress.Parse).ToArray() : [];
            var tunnelDns = dns.Where(ip => allowsV6 || ip.AddressFamily == AddressFamily.InterNetwork).ToArray();
            if (tunnelDns.Length == 0 || tunnelDns.Any(ip => !routes.Any(network => network.Contains(ip)) ||
                explicitExclusions.Any(network => network.Contains(ip))))
                throw new FormatException(T("The profile must provide DNS addresses within the VPN destinations and outside explicit exclusions.",
                    "O perfil precisa fornecer DNS dentro dos destinos da VPN e fora das exclusões explícitas.",
                    "DNS-адреса профиля должны входить в маршруты VPN и не попадать в явные исключения."));
            lines.Add($"DNS = {string.Join(", ", tunnelDns.AsEnumerable())}");
        }

        WriteProtocol(lines, protocol);
        lines.Add("");
        lines.Add("[Peer]");
        foreach (var key in PeerKeys)
        {
            if (key == "AllowedIPs")
                lines.Add($"AllowedIPs = {string.Join(", ", routes.AsEnumerable())}");
            else if (peer.TryGetValue(key, out var value))
                lines.Add($"{key} = {value}");
        }
        lines.Add($"#@ws:AllowedApps = {string.Join(", ", apps)}");
        if (excluded.Count > 0)
            lines.Add($"#@ws:DisallowedIPs = {string.Join(", ", excluded.Distinct())}");
        var tunneledIpv6 = routes.Any(network => network.BaseAddress.AddressFamily == AddressFamily.InterNetworkV6);
        if (options.BlockPublicIpv6 && !tunneledIpv6 && apps.Any(path => path.EndsWith('\\')))
            throw new FormatException(T("IPv6 blocking requires individual .exe rules. Replace folder rules or turn off IPv6 blocking.",
                "O bloqueio de IPv6 exige regras por .exe. Substitua as regras de pasta ou desative o bloqueio de IPv6.",
                "Для блокировки IPv6 требуются правила для отдельных файлов .exe. Замените правила папок или отключите блокировку."));
        return new VpnSessionConfig(string.Join("\r\n", lines) + "\r\n", options.BlockPublicIpv6 && !tunneledIpv6);
    }

    private static readonly string[] LanNetworks =
        ["10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16", "172.16.0.0/12",
         "192.168.0.0/16", "224.0.0.0/4", "255.255.255.255/32",
         "::1/128", "fc00::/7", "fe80::/10", "ff00::/8"];

    private static string[] SplitList(string value) => value.Split([',', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    private static string Required(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && value.Length > 0
            ? value : throw new FormatException(T($"The profile must specify {key}.", $"O perfil precisa informar {key}.", $"В профиле необходимо указать {key}."));

    private static bool TryIp(string value, out IPAddress ip)
    {
        if (value.Contains(':') && !value.Contains('%') && IPAddress.TryParse(value, out var ipv6) &&
            ipv6.AddressFamily == AddressFamily.InterNetworkV6 && !ipv6.IsIPv4MappedToIPv6)
        {
            ip = ipv6;
            return true;
        }
        var bytes = new byte[4];
        var pieces = value.Split('.');
        if (pieces.Length == 4)
        {
            var valid = true;
            for (var i = 0; i < pieces.Length; i++)
            {
                if (!byte.TryParse(pieces[i], NumberStyles.None, CultureInfo.InvariantCulture, out bytes[i]) ||
                    bytes[i].ToString(CultureInfo.InvariantCulture) != pieces[i])
                    valid = false;
            }
            if (valid)
            {
                ip = new IPAddress(bytes);
                return true;
            }
        }
        ip = IPAddress.None;
        return false;
    }

    private static bool IsInterfaceAddress(string value)
    {
        var parts = value.Split('/');
        return parts.Length == 2 && TryIp(parts[0], out var ip) &&
            int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var prefix) &&
            prefix <= (ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128);
    }

    private static string[] ParseNetworks(string value, bool allowBareIp)
    {
        var list = SplitList(value);
        if (list.Length > 256 || (!allowBareIp && list.Length == 0))
            throw new FormatException(T("Specify between 1 and 256 networks in the profile.", "Informe entre 1 e 256 redes no perfil.", "Укажите в профиле от 1 до 256 сетей."));

        for (var i = 0; i < list.Length; i++)
        {
            var item = list[i];
            var parts = item.Split('/');
            if (!TryIp(parts[0], out var address))
                throw InvalidNetwork();
            if (parts.Length == 1 && allowBareIp)
                item = $"{address}/{(address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128)}";
            else if (!IsInterfaceAddress(item))
                throw InvalidNetwork();
            if (!IPNetwork.TryParse(item, out var parsed) || !parsed.BaseAddress.Equals(address))
                throw InvalidNetwork();
            list[i] = parsed.ToString();
        }
        return list.Distinct().ToArray();

        static FormatException InvalidNetwork() =>
            new(T("Enter an IP address or a valid CIDR range such as 203.0.113.0/24. Use the base address for a network.", "Use um endereço IP ou uma rede CIDR válida, como 203.0.113.0/24. Para uma rede, informe o endereço base.", "Введите IP-адрес или диапазон CIDR, например 203.0.113.0/24. Для сети укажите её базовый адрес."));
    }

    private static void NormalizeKey(Dictionary<string, string> values, string name)
    {
        const int keyBytes = 32;
        var downloadHint = T("Download the original .conf from Downloads → WireGuard Configuration in your Proton account.", "Baixe o .conf original em Downloads → Configuração WireGuard da sua conta Proton.", "Скачайте исходный файл .conf в личном кабинете Proton в разделе «Загрузки → Конфигурация WireGuard».");
        if (!values.TryGetValue(name, out var raw) || string.IsNullOrWhiteSpace(raw))
            throw new FormatException(T($"{name} is missing or empty. {downloadHint}", $"O campo {name} está ausente ou vazio. {downloadHint}", $"Поле {name} отсутствует или пусто. {downloadHint}"));

        var value = raw.AsSpan().Trim();
        if (value.Length >= 2 && (value[0] is '"' or '\'') && value[0] == value[^1])
            value = value[1..^1];

        Span<char> normalized = stackalloc char[44];
        Span<byte> decoded = stackalloc byte[33];
        try
        {
            var length = 0;
            foreach (var c in value)
            {
                if (char.IsWhiteSpace(c) || c is '\uFEFF' or '\u200B' or '\u2060') continue;
                if (length == normalized.Length) throw InvalidKey();
                normalized[length++] = c switch { '-' => '+', '_' => '/', _ => c };
            }

            if (length == normalized.Length - 1) normalized[length++] = '=';
            if (length != normalized.Length ||
                !Convert.TryFromBase64Chars(normalized, decoded, out var written) || written != keyBytes)
                throw InvalidKey();

            values[name] = Convert.ToBase64String(decoded[..written]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decoded);
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(normalized));
        }

        FormatException InvalidKey() =>
            new(T($"{name} must contain a 32-byte Base64 WireGuard key. {downloadHint}", $"O campo {name} precisa conter uma chave WireGuard de 32 bytes em Base64. {downloadHint}", $"В поле {name} должен быть 32-байтовый ключ WireGuard в Base64. {downloadHint}"));
    }

    private static void CheckNumber(Dictionary<string, string> values, string name, int min, int max)
    {
        if (values.TryGetValue(name, out var text) && (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ||
            number < min || number > max))
            throw new FormatException(T($"{name} is outside the allowed range.", $"O valor de {name} está fora do intervalo permitido.", $"Значение {name} выходит за допустимые пределы."));
    }

    private static void CheckEndpoint(string endpoint)
    {
        var separator = endpoint.LastIndexOf(':');
        if (separator <= 0 || !ushort.TryParse(endpoint[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var port) ||
            port == 0)
            throw new FormatException(T("Endpoint must contain a host or IP address and a port.", "Endpoint precisa conter um host ou IP e uma porta.", "В Endpoint укажите имя узла или IP-адрес и порт."));
        var host = endpoint[..separator];
        var valid = host.StartsWith('[') && host.EndsWith(']')
            ? TryIp(host[1..^1], out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6
            : host.Length <= 253 && !host.Contains(':') &&
              (TryIp(host, out _) || (DnsNamePattern().IsMatch(host) && host.Any(char.IsAsciiLetter)));
        if (!valid) throw new FormatException(T("The host in Endpoint is invalid.", "O host em Endpoint é inválido.", "В Endpoint указано недопустимое имя узла."));
    }

    [GeneratedRegex(@"^(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)*[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.?$")]
    private static partial Regex DnsNamePattern();
}

public sealed record RouteOptions(bool DirectLan = true, bool UseIpv6 = true, string Include = "", string Exclude = "",
    bool TunnelDns = false, bool BlockPublicIpv6 = false);
public sealed record VpnSessionConfig(string Text, bool BlockPublicIpv6);
