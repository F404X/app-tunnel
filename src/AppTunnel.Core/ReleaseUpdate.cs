using System.Text.Json;

namespace AppTunnel.Core;

public sealed record ReleaseUpdate(Version Version, Uri Download, string Sha256, long Size)
{
    public const string InstallerName = "AppTunnel-Setup-x64.exe";
    private const long MaxInstallerBytes = 200 * 1024 * 1024;
    private const string Repository = "F404X/app-tunnel";
    public const string ReleasesApi = "https://api.github.com/repos/" + Repository + "/releases?per_page=100";

    public static ReleaseUpdate? FindNewest(JsonElement releases, Version installed)
    {
        ReleaseUpdate? newest = null;
        foreach (var release in releases.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!TryVersion(tag, out var version) || version <= installed ||
                (newest is not null && version <= newest.Version)) continue;

            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != InstallerName) continue;
                var expectedPath = $"/{Repository}/releases/download/{tag}/{InstallerName}";
                if (!Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var download) ||
                    !IsDownloadUri(download) || download.Host != "github.com" || download.AbsolutePath != expectedPath ||
                    download.Query.Length != 0 || download.Fragment.Length != 0) continue;

                var size = asset.GetProperty("size").GetInt64();
                var digest = asset.TryGetProperty("digest", out var property) ? property.GetString() : null;
                if (size <= 0 || size > MaxInstallerBytes || digest is null ||
                    !digest.StartsWith("sha256:", StringComparison.Ordinal) || digest.Length != 71 ||
                    !digest[7..].All(Uri.IsHexDigit)) continue;
                newest = new(version, download, digest[7..], size);
                break;
            }
        }
        return newest;
    }

    public static bool IsDownloadUri(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps &&
        uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
        uri.Host is "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com";

    private static bool TryVersion(string tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (!tag.StartsWith('v')) return false;
        var parts = tag[1..].Split('.');
        if (parts.Length != 3 || parts.Any(part => part.Length == 0 || !part.All(char.IsAsciiDigit)) ||
            !Version.TryParse(tag[1..], out var parsed) || tag != "v" + parsed.ToString(3))
            return false;
        version = parsed;
        return true;
    }
}
