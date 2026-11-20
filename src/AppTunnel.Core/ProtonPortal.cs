namespace AppTunnel.Core;

public static class ProtonPortal
{
    public const string Downloads = "https://account.protonvpn.com/downloads#wireguard-configuration";

    public static bool IsOfficialUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0) return false;
        var host = uri.IdnHost;
        return host.Equals("proton.me", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".proton.me", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("protonvpn.com", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".protonvpn.com", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAccountPage(string? url) =>
        IsOfficialUrl(url) && new Uri(url!).IdnHost is "account.protonvpn.com" or "account.proton.me";

    public static bool IsWireGuardDownload(string page, string uri, string filename)
    {
        if (!IsAccountPage(page) || !Path.GetExtension(filename).Equals(".conf", StringComparison.OrdinalIgnoreCase)) return false;
        return IsOfficialUrl(uri.StartsWith("blob:", StringComparison.OrdinalIgnoreCase) ? uri[5..] : uri);
    }
}
