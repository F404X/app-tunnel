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
    ("English and Portuguese profile diagnostics", () =>
    {
        try
        {
            Localized.SetLanguage("en");
            Require(Throws<FormatException>(() => WireGuardProfile.Parse("")).Message.StartsWith("PrivateKey is missing"));
            Localized.SetLanguage("pt");
            Require(Throws<FormatException>(() => WireGuardProfile.Parse("")).Message.StartsWith("O campo PrivateKey"));
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
