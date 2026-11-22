using System.Text.Json;
using AppTunnel.Core;

var key = Convert.ToBase64String(Enumerable.Repeat((byte)0x42, 32).ToArray());
var sample = $"""
    [Interface]
    PrivateKey = {key}
    Address = 10.2.0.2/32, fd00::2/128
    DNS = 10.2.0.1
    MTU = 1380

    [Peer]
    PublicKey = {key}
    AllowedIPs = 0.0.0.0/0, ::/0
    Endpoint = 192.0.2.20:51820
    PersistentKeepalive = 25
    """;
var scenarios = new (string Name, Action Check)[]
{
    ("telemetry consent, exact previews, deduplication, persistence and failure isolation", TelemetryChecks.Run),
    ("Proton key pairs match RFC 8032 and produce a WireGuard configuration AppTunnel accepts", () =>
    {
        static string Public(string seed) => Convert.ToHexString(Convert.FromBase64String(
            ProtonKeyPair.FromSeed(Convert.FromHexString(seed)).PublicKey)).ToLowerInvariant();
        Require(Public("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60") ==
            "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a");
        Require(Public("4ccd089b28ff96da9db6c346ec114e0f5b8a319f35aba624da8cf6ed4fb8a6fb") ==
            "3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c");
        var pair = ProtonKeyPair.Create();
        Require(ProtonRoutes.IsKey(pair.PublicKey) && ProtonRoutes.IsKey(pair.WireGuardPrivateKey));
        var server = new ProtonServerChoice("id", "NL-FREE#12", "NL", "Amsterdam", 0, 40, 1.5, "192.0.2.30", "2001:db8::30", key);
        Require(ProtonRoutes.IsUsable(server));
        Require(!ProtonRoutes.IsUsable(server with { EntryIp = "2001:db8::1" }));
        Require(!ProtonRoutes.IsUsable(server with { PublicKey = "x" }));
        var parsed = WireGuardProfile.Parse(ProtonRoutes.BuildConfig("AppTunnel NL-FREE#12", pair.WireGuardPrivateKey, server));
        Require(parsed.Endpoint == "192.0.2.30:51820" && parsed.HasIpv6);
        Require(ProtonServerNames.CountryOf("NL-FREE#12") == "NL" && ProtonServerNames.CountryOf("node") == "");
        // Proton answers with the key wrapped as PEM/SPKI, exactly as its certificate API returns it.
        var raw = Convert.FromBase64String(pair.PublicKey);
        var newline = ((char)10).ToString();
        var pem = "-----BEGIN PUBLIC KEY-----" + newline +
            Convert.ToBase64String([0x30, 0x2a, 0x30, 0x05, 0x06, 0x03, 0x2b, 0x65, 0x70, 0x03, 0x21, 0x00, .. raw]) + newline + "-----END PUBLIC KEY-----";
        Require(ProtonRoutes.SameKey(pem, pair.PublicKey) && ProtonRoutes.SameKey(pair.PublicKey, pair.PublicKey));
        Require(!ProtonRoutes.SameKey(pem, ProtonKeyPair.Create().PublicKey) && !ProtonRoutes.SameKey(null, pair.PublicKey));
    }),
    ("route latency requires five seconds of slow replies from the same live session", () =>
    {
        var latency = new RouteLatency();
        bool Observe(int second, long? ms, string session = "active") =>
            latency.ShouldSearch(session, TimeSpan.FromSeconds(second), ms);
        for (var second = 0; second < 5; second++) Require(!Observe(second, 401));
        Require(!Observe(5, 400));
        for (var second = 6; second < 11; second++) Require(!Observe(second, 450));
        Require(Observe(11, 450));
        Require(!Observe(12, 900));
        Require(!Observe(42, 500, "replacement"));
        Require(!Observe(43, null, "replacement"));
        Require(!Observe(44, 500, "replacement"));
        Require(!Observe(48, 500, "replacement"));
        for (var second = 49; second < 53; second++) Require(!Observe(second, 500, "replacement"));
        Require(Observe(53, 500, "replacement"));
    }),
    ("common app aliases preserve all recognized Discord clients and group alternate builds", () =>
    {
        Require(DiscordClients.KnownNames.All(name => CommonApps.Find(name) is not null));
        Require(CommonApps.Find("DiscordPTB")?.Name == "Discord PTB");
        Require(CommonApps.Find("DiscordCanary")?.Name == "Discord Canary");
        Require(CommonApps.Find("ArmCord")?.Name == "Legcord");
        Require(CommonApps.Find("VencordDesktop")?.Name == "Vesktop");
        Require(CommonApps.Find("Equicord") is null && CommonApps.Find("DiscordRocks") is null);
        Require(CommonApps.Find("Lightcord") is null);
        Require(CommonApps.Find("serein-portable-x64")?.Name == "Serein");
        Require(CommonApps.Find("Telegram Desktop")?.Name == "Telegram");
        Require(CommonApps.Find("signal-desktop")?.Name == "Signal");
        Require(CommonApps.Find("Discord log viewer") is null);
    }),
    ("automatic protocol planning uses real profile variants, distinct endpoints and bounded retries", () =>
    {
        var selected = new VpnRoute<int>(1, "one:51820", VpnProtocol.WireGuard);
        VpnRoute<int>[] alternatives =
        [
            new(2, "one:51820", VpnProtocol.WireGuard),
            new(3, "two:443", VpnProtocol.WireGuard),
            new(4, "three:443", VpnProtocol.AmneziaWG),
            new(5, "four:51820", VpnProtocol.WireGuard)
        ];
        var plan = VpnProtocols.Plan(ProtocolPreference.Automatic, selected, alternatives);
        Require(plan.Select(route => route.Profile).SequenceEqual([1, 1, 4, 3]));
        Require(plan.Select(route => route.Protocol).SequenceEqual(
            [VpnProtocol.WireGuard, VpnProtocol.WireGuardPadding, VpnProtocol.AmneziaWG, VpnProtocol.WireGuard]));
        Require(VpnProtocols.Plan(ProtocolPreference.WireGuardPadding, selected, alternatives).Single() ==
            selected with { Protocol = VpnProtocol.WireGuardPadding });
    }),
    ("WireGuard padding preserves app rules and AmneziaWG requires a compatible profile", () =>
    {
        string[] apps = [@"C:\Apps\Serein.exe"];
        var profile = WireGuardProfile.Parse(sample);
        var padded = profile.BuildSessionConfig(apps, new(), VpnProtocol.WireGuardPadding).Text;
        Require(padded.Contains("Jc = 4") && padded.Contains("Jmin = 50") && padded.Contains("Jmax = 1000"));
        Require(padded.Contains(@"#@ws:AllowedApps = C:\Apps\Serein.exe"));
        Require(!profile.BuildClientConfig(apps, new()).Contains("Jc"));
        Throws<FormatException>(() => profile.BuildSessionConfig(apps, new(), VpnProtocol.AmneziaWG));

        var amnezia = sample.Replace("[Peer]", "Jc = 3\nS1 = 20\nS2 = 25\nH1 = 7\nH2 = 8\nH3 = 9\nH4 = 10\n\n[Peer]");
        var imported = WireGuardProfile.Parse(amnezia);
        Require(imported.Protocol == VpnProtocol.AmneziaWG);
        Require(imported.BuildClientConfig(apps, new()).Contains("H4 = 10"));
        Throws<FormatException>(() => imported.BuildSessionConfig(apps, new(), VpnProtocol.WireGuard));
        Throws<FormatException>(() => WireGuardProfile.Parse(amnezia.Replace("H4 = 10", "H4 = 7")));
        Throws<FormatException>(() => WireGuardProfile.Parse(amnezia.Replace("H4 = 10", "H4 = 7-10")));
        Throws<FormatException>(() => WireGuardProfile.Parse(amnezia.Replace("Jc = 3", "Jc = 999")));
    }),
    ("privacy settings preserve split routes and require real VPN DNS and executable scopes", () =>
    {
        var profile = WireGuardProfile.Parse(sample);
        string[] apps = [@"C:\Apps\Serein.exe"];
        var secured = profile.BuildSessionConfig(apps, new(UseIpv6: false, TunnelDns: true, BlockPublicIpv6: true), VpnProtocol.WireGuard);
        Require(secured.BlockPublicIpv6 && secured.Text.Contains("DNS = 10.2.0.1"));
        Require(!secured.Text.Contains("AllowedIPs = 0.0.0.0/0, ::/0"));
        Require(!profile.BuildClientConfig(apps, new()).Contains("DNS ="));
        Throws<FormatException>(() => profile.BuildClientConfig(apps, new(TunnelDns: true, Exclude: "10.2.0.1")));
        Throws<FormatException>(() => profile.BuildClientConfig(apps, new(TunnelDns: true, Include: "192.0.2.0/24")));
        Throws<FormatException>(() => profile.BuildClientConfig([@"C:\Apps\"], new(UseIpv6: false, BlockPublicIpv6: true)));
        Require(!profile.BuildSessionConfig(apps, new(BlockPublicIpv6: true), VpnProtocol.WireGuard).BlockPublicIpv6);
    }),
    ("WireSock diagnostics require wire events and distinguish routing from handshake and DNS", () =>
    {
        Require(TunnelDiagnostics.Observe(TunnelHealth.Unknown, "service running") == TunnelHealth.Unknown);
        const string sent = "[TUN]: force_handshake: Handshake initiation sent to WireGuard server";
        const string received = "[TUN]: Handshake response received from 192.0.2.10 : 51820";
        Require(TunnelDiagnostics.Observe(TunnelHealth.Unknown, sent) == TunnelHealth.HandshakeSent);
        Require(TunnelDiagnostics.Observe(TunnelHealth.HandshakeSent, received) == TunnelHealth.HandshakeReceived);
        Require(TunnelDiagnostics.Observe(TunnelHealth.Unknown, "Received handshake_response") == TunnelHealth.Unknown);
        Require(TunnelDiagnostics.Observe(TunnelHealth.HandshakeTimedOut, sent) == TunnelHealth.HandshakeTimedOut);
        Require(TunnelDiagnostics.Observe(TunnelHealth.Unknown, "[TUN]: Failed to figure out the route to the VPN server") ==
            TunnelHealth.RoutingFailed);
        Require(TunnelDiagnostics.Observe(TunnelHealth.Unknown, "No WireGuard server endpoint provided or failed to resolve.") ==
            TunnelHealth.EndpointResolutionFailed);
    }),
    ("Discord detection includes Serein while restart matching respects app rules", () =>
    {
        Require(DiscordClients.IsKnown("Serein") && DiscordClients.IsKnown("Serein-portable-x64"));
        Require(DiscordClients.IsKnown("Vesktop") && DiscordClients.IsKnown("Discord PTB"));
        Require(DiscordClients.OfficialName("DISCORD.EXE") == "Discord");
        Require(DiscordClients.OfficialName("DiscordPTB.exe") == "Discord PTB");
        Require(DiscordClients.OfficialName("DiscordCanary.exe") == "Discord Canary");
        Require(DiscordClients.OfficialName("Serein.exe") is null);
        Require(DiscordClients.OfficialName("Vesktop.exe") is null);
        ApplicationTarget[] rules =
        [
            new("Discord", @"C:\App\Discord\", Folder: true),
            new("PTB", @"C:\App\DiscordPTB\DiscordPTB.exe", Enabled: false),
            new("Canary", @"C:\Other\DiscordCanary.exe")
        ];
        Require(DiscordClients.IsRouted(@"C:\App\discord\app-1\Discord.exe", rules));
        Require(DiscordClients.IsRouted(@"C:\Other\DiscordCanary.exe", rules));
        Require(!DiscordClients.IsRouted(@"C:\App\DiscordPTB\DiscordPTB.exe", rules));
        Require(!DiscordClients.IsRouted(@"C:\Other\DiscordCanary\DiscordCanary.exe", rules));
    }),
    ("updates choose the newest verified Windows installer by version", () =>
    {
        var digest = new string('b', 64);
        object Release(string tag, bool draft = false, string host = "https://github.com", string? hash = null) => new
        {
            tag_name = tag,
            draft,
            prerelease = true,
            assets = new[]
            {
                new
                {
                    name = ReleaseUpdate.InstallerName,
                    browser_download_url = $"{host}/F404X/app-tunnel/releases/download/{tag}/{ReleaseUpdate.InstallerName}",
                    size = 1024,
                    digest = hash ?? $"sha256:{digest}"
                }
            }
        };

        var listing = JsonSerializer.SerializeToElement(new[]
        {
            Release("v0.6.0"), Release("v0.8.0", draft: true), Release("v0.12.0"), Release("v01.0.0"),
            Release("v0.50.0", host: "https://github.com.example"), Release("v1.0.0", hash: "sha256:invalid")
        });
        var newest = ReleaseUpdate.FindNewest(listing, new Version(0, 5, 0));
        Require(newest?.Version == new Version(0, 12, 0) && newest.Sha256 == digest && newest.Size == 1024);
        Require(ReleaseUpdate.FindNewest(listing, new Version(0, 12, 0)) is null);
        Require(ReleaseUpdate.IsDownloadUri(new Uri("https://release-assets.githubusercontent.com/download")));
        Require(!ReleaseUpdate.IsDownloadUri(new Uri("http://release-assets.githubusercontent.com/download")));
        Require(!ReleaseUpdate.IsDownloadUri(new Uri("https://github.com@elsewhere.example/download")));
        Require(!ReleaseUpdate.IsDownloadUri(new Uri("https://github.com:444/download")));
    }),
    ("upgrade copies saved profiles and preferences once", () =>
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var legacy = Directory.CreateDirectory(Path.Combine(root.FullName, "previous"));
            var next = Path.Combine(root.FullName, "AppTunnel");
            var profile = Guid.NewGuid().ToString("N") + ".dpapi";
            File.WriteAllBytes(Path.Combine(legacy.FullName, profile), [1, 83, 42]);
            File.WriteAllText(Path.Combine(legacy.FullName, "settings.json"), "previous settings");
            File.WriteAllText(Path.Combine(legacy.FullName, "session.conf"), "temporary");
            ProfileMigration.CopyLegacyProfiles(legacy.FullName, next);
            Require(File.ReadAllBytes(Path.Combine(next, profile)).SequenceEqual(new byte[] { 1, 83, 42 }));
            Require(File.ReadAllText(Path.Combine(next, "settings.json")) == "previous settings");
            Require(!File.Exists(Path.Combine(next, "session.conf")));
            File.WriteAllText(Path.Combine(next, "settings.json"), "current settings");
            ProfileMigration.CopyLegacyProfiles(legacy.FullName, next);
            Require(File.ReadAllText(Path.Combine(next, "settings.json")) == "current settings");
        }
        finally { root.Delete(recursive: true); }
    }),
    ("Proton catalog distinguishes usable, unavailable and incompatible keys", () =>
    {
        var old = sample.Replace($"PrivateKey = {key}", "PrivateKey = *****");
        Require(ProtonSync.Inspect(old, out var masked) == ProtonProfileState.KeyRequired && masked is null);
        Require(ProtonSync.Inspect(null, out _) == ProtonProfileState.KeyRequired);
        Require(ProtonSync.Inspect(sample.Replace("MTU = 1380", "PostUp = script"), out var unsupported) ==
            ProtonProfileState.Unsupported && unsupported is null);
        var fresh = sample.Replace("[Interface]", "[Interface]\n# Key for Lab\n# Other comment");
        Require(ProtonSync.Inspect(fresh, out var available) == ProtonProfileState.Available && available is not null);
        Require(ProtonSync.GetDeviceName(fresh) == "Lab");
        Require(ProtonSync.SafeName("Line\r\nTwo") == "LineTwo");
        Require(WireGuardProfile.Parse(sample).Fingerprint() == available);
        Require(WireGuardProfile.Parse(sample.Replace("192.0.2.20", "192.0.2.35")).Fingerprint() != available);
    }),
    ("official Proton sign-in and WireGuard downloads", () =>
    {
        const string page = ProtonPortal.Downloads;
        Require(ProtonPortal.IsOfficialUrl(page));
        Require(ProtonPortal.IsOfficialUrl("https://account.proton.me/login"));
        Require(ProtonPortal.IsWireGuardDownload(page, "blob:https://account.protonvpn.com/123", "free-nl.conf"));
        Require(ProtonPortal.IsWireGuardDownload(page, "https://account.protonvpn.com/api/file", "NL.CONF"));
        Require(!ProtonPortal.IsWireGuardDownload(page, "https://elsewhere.example/replacement.conf", "free-nl.conf"));
        Require(!ProtonPortal.IsWireGuardDownload(page, "https://account.protonvpn.com/api/file", "setup.exe"));
        Require(!ProtonPortal.IsWireGuardDownload("https://mail.proton.me/", "https://account.protonvpn.com/api/file", "free-nl.conf"));
        Require(!ProtonPortal.IsOfficialUrl("http://account.protonvpn.com/downloads"));
        Require(!ProtonPortal.IsOfficialUrl("https://account.protonvpn.com.example.org/downloads"));
        Require(!ProtonPortal.IsOfficialUrl("https://account.protonvpn.com@elsewhere.example/downloads"));
        Require(!ProtonPortal.IsOfficialUrl("https://account.protonvpn.com:444/downloads"));
    }),
    ("English, Portuguese and Russian profile diagnostics", () =>
    {
        try
        {
            Localized.SetLanguage("en");
            Require(Throws<FormatException>(() => WireGuardProfile.Parse("")).Message.StartsWith("PrivateKey is missing"));
            Localized.SetLanguage("pt");
            Require(Throws<FormatException>(() => WireGuardProfile.Parse("")).Message.StartsWith("O campo PrivateKey"));
            Localized.SetLanguage("ru");
            Require(Localized.Language == "ru");
            Require(Throws<FormatException>(() => WireGuardProfile.Parse("")).Message.StartsWith("Поле PrivateKey"));
            Require(VpnProtocols.Name(ProtocolPreference.Automatic) == "Автоматически");
        }
        finally { Localized.SetLanguage("en"); }
    }),
    ("perfil Proton e endereço do servidor", () =>
    {
        var profile = WireGuardProfile.Parse(sample);
        Require(profile.HasIpv6 && profile.Host == "192.0.2.20");
        Require(WireGuardProfile.Parse(sample.Replace("192.0.2.20", "[2001:db8::1]")).Host == "2001:db8::1");
        Require(WireGuardProfile.Parse(sample.Replace("192.0.2.20", "wg.example.net")).Host == "wg.example.net");
    }),
    ("importação e normalização de chaves WireGuard", () =>
    {
        var source = new byte[32];
        var random = new Random(149);
        for (var i = 0; i < 96; i++)
        {
            random.NextBytes(source);
            var standard = Convert.ToBase64String(source);
            var unpadded = standard.TrimEnd('=');
            var urlSafe = unpadded.Replace('+', '-').Replace('/', '_');
            var copied = $"\"\uFEFF{urlSafe[..20]}\u200B {urlSafe[20..]}\"";
            var profile = WireGuardProfile.Parse(sample.Replace($"PrivateKey = {key}", $"PrivateKey = {copied}")
                .Replace($"PublicKey = {key}", $"PublicKey = '{unpadded}'\nPresharedKey = {standard}"));
            var config = Build(profile);
            Require(config.Contains($"PrivateKey = {standard}\r\n"));
            Require(config.Contains($"PublicKey = {standard}\r\n"));
            Require(config.Contains($"PresharedKey = {standard}\r\n"));
        }
    }),
    ("diagnóstico de chave inválida preserva seu conteúdo", () =>
    {
        var values = new[] { key[..42], key + "=", Convert.ToBase64String(new byte[33]),
            "<private-key>", "SEGREDO-NUNCA-ECOAR", key.Replace("Q", "%"), "\"" + key, "" };
        foreach (var invalid in values)
        {
            var error = Throws<FormatException>(() => WireGuardProfile.Parse(
                sample.Replace($"PrivateKey = {key}", $"PrivateKey = {invalid}")));
            Require(error.Message.Contains("PrivateKey") && !error.Message.Contains("SEGREDO-NUNCA-ECOAR"));
        }
    }),
    ("regra única, obrigatória e restrita em [Peer]", () =>
    {
        var profile = WireGuardProfile.Parse(sample.Replace("[Interface]", "[Interface]\n#@ws:AllowedApps = chrome"));
        var config = profile.BuildClientConfig([@"C:\Games\Serein\serein.exe", @"C:\Discord\\"], new());
        Require(!config[..config.IndexOf("[Peer]", StringComparison.Ordinal)].Contains("AllowedApps"));
        Require(config.Split("AllowedApps").Length == 2);
        Require(config.Contains(@"#@ws:AllowedApps = C:\Games\Serein\serein.exe, C:\Discord\"));
        Require(!config.Contains("chrome") && !config.Contains("DNS ="));
        Throws<InvalidOperationException>(() => profile.BuildClientConfig([], new()));
    }),
    ("arquivos personalizados e limites de caminho", () =>
    {
        Require(ApplicationTarget.Validate(@"D:/App customizado/Minha Build.exe") == @"D:\App customizado\Minha Build.exe");
        foreach (var name in new[] { "discord.exe", @"C:\", @"C:\Apps,Outros\x.exe",
            @"C:\Apps\..\x.exe", @"C:\Apps\x.exe" + "\r\nEndpoint = vpn", @"\\server\apps\serein.exe" })
            Throws<FormatException>(() => ApplicationTarget.Validate(name));
    }),
    ("IPv6 nativo e exclusões LAN opcionais", () =>
    {
        var dual = WireGuardProfile.Parse(sample);
        var v4 = WireGuardProfile.Parse(sample.Replace(", fd00::2/128", ""));
        Require(!v4.HasIpv6);
        Require(Build(dual).Contains("AllowedIPs = 0.0.0.0/0, ::/0"));
        Require(Build(v4).Contains("AllowedIPs = 0.0.0.0/0\r\n"));
        var config = Build(dual, new(DirectLan: false, UseIpv6: false));
        Require(config.Contains("AllowedIPs = 0.0.0.0/0\r\n") && !config.Contains("DisallowedIPs"));
    }),
    ("escopo e exceções CIDR", () =>
    {
        var profile = WireGuardProfile.Parse(sample);
        var config = Build(profile, new(DirectLan: false, Include: "203.0.113.0/24",
            Exclude: "203.0.113.12\n203.0.113.128/25"));
        Require(config.Contains("AllowedIPs = 203.0.113.0/24"));
        Require(config.Contains("#@ws:DisallowedIPs = 203.0.113.12/32, 203.0.113.128/25"));
        Require(Build(profile).Contains("192.168.0.0/16"));
        Throws<FormatException>(() => Build(profile, new(Include: "203.0.113.7/24")));
        Throws<FormatException>(() => Build(profile, new(Include: "203.0.113.0/25", Exclude: "203.0.113.0/24")));
    }),
    ("o usuário só pode estreitar os destinos do perfil original", () =>
    {
        var profile = WireGuardProfile.Parse(sample.Replace("0.0.0.0/0, ::/0", "203.0.113.0/24"));
        Require(Build(profile, new(Include: "203.0.113.4")).Contains("203.0.113.4/32"));
        Throws<FormatException>(() => Build(profile, new(Include: "0.0.0.0/0")));
        Throws<FormatException>(() => Build(profile, new(Include: "198.51.100.0/24")));
    }),
    ("bloqueio de perfis inválidos e comandos de shell", () =>
    {
        var variants = new[] { sample + "\n[Peer]", sample.Replace("PersistentKeepalive", "PostUp"),
            sample.Replace("192.0.2.20", "999.0.2.20"), sample.Replace("51820", "65536"),
            sample.Replace("10.2.0.2/32", "10.2.0.2/999"), sample.Replace(key, "chave inválida"),
            sample.Replace("MTU = 1380", "MTU = 1380\nMTU = 1420") };
        foreach (var invalid in variants) Throws<FormatException>(() => WireGuardProfile.Parse(invalid));
    })
};
foreach (var scenario in scenarios)
{
    scenario.Check();
    Console.WriteLine($"OK | {scenario.Name}");
}
Console.WriteLine($"{scenarios.Length} cenários validados.");

static string Build(WireGuardProfile profile, RouteOptions? route = null) =>
    profile.BuildClientConfig([@"C:\Apps\Serein.exe"], route ?? new());

static void Require(bool condition)
{
    if (!condition) throw new InvalidOperationException("A verificação falhou.");
}

static T Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T error) { return error; }
    throw new InvalidOperationException($"Esperava {typeof(T).Name}.");
}
