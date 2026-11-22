using System.Text.Json;

namespace AppTunnel.Core;

public sealed record ReleaseUpdate(SemVersion Version, Uri Download, string Sha256, long Size)
{
    public const string InstallerName = "AppTunnel-Setup-x64.exe";
    private const long MaxInstallerBytes = 200 * 1024 * 1024;
    private const string Repository = "F404X/app-tunnel";
    public const string ReleasesApi = "https://api.github.com/repos/" + Repository + "/releases?per_page=100";

    // v0.13.0 is the first stable release; earlier numeric releases were previews and are never offered.
    public static readonly SemVersion FirstStable = new(0, 13, 0, []);

    public static string ReleasesPage(int page) => $"{ReleasesApi}&page={page}";

    // Stable follows only stable releases; Beta adds betas; Nightly adds nightly builds.
    public static bool Includes(UpdateChannel channel, SemVersion version) => version.Channel switch
    {
        UpdateChannel.Stable => true,
        UpdateChannel.Beta => channel is UpdateChannel.Beta or UpdateChannel.Nightly,
        _ => channel == UpdateChannel.Nightly
    };

    // The highest release on the channel, whether or not it is newer than the installed version.
    public static ReleaseUpdate? FindBest(JsonElement releases, UpdateChannel channel, ReleaseUpdate? best = null)
    {
        if (releases.ValueKind != JsonValueKind.Array) return best;
        foreach (var release in releases.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object || !release.TryGetProperty("draft", out var draft) ||
                draft.ValueKind != JsonValueKind.False) continue;
            if (!release.TryGetProperty("tag_name", out var tagValue) || tagValue.ValueKind != JsonValueKind.String ||
                tagValue.GetString() is not { } tag ||
                !tag.StartsWith('v') || SemVersion.Parse(tag[1..]) is not { } version || tag != "v" + version) continue;
            var prerelease = release.TryGetProperty("prerelease", out var flag) && flag.ValueKind == JsonValueKind.True;
            // The release flag must agree with the version: stables are full releases, betas and nightlies previews.
            if (version.IsStable ? prerelease || version < FirstStable : !prerelease) continue;
            if (!Includes(channel, version) || best is not null && version <= best.Version) continue;
            if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) continue;

            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object || !asset.TryGetProperty("name", out var name) ||
                    name.ValueKind != JsonValueKind.String || name.GetString() != InstallerName) continue;
                var expectedPath = $"/{Repository}/releases/download/{tag}/{InstallerName}";
                if (!asset.TryGetProperty("browser_download_url", out var url) || url.ValueKind != JsonValueKind.String ||
                    !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var download) ||
                    !IsDownloadUri(download) || download.Host != "github.com" || download.AbsolutePath != expectedPath ||
                    download.Query.Length != 0 || download.Fragment.Length != 0) continue;

                var size = asset.TryGetProperty("size", out var sizeValue) && sizeValue.TryGetInt64(out var bytes) ? bytes : 0;
                var digest = asset.TryGetProperty("digest", out var property) && property.ValueKind == JsonValueKind.String
                    ? property.GetString() : null;
                if (size <= 0 || size > MaxInstallerBytes || digest is null ||
                    !digest.StartsWith("sha256:", StringComparison.Ordinal) || digest.Length != 71 ||
                    !digest[7..].All(Uri.IsHexDigit)) continue;
                best = new(version, download, digest[7..], size);
                break;
            }
        }
        return best;
    }

    // The update to offer: the best release on the channel when it is newer than the installed version. The app
    // never moves to a lower version, so a build ahead of the channel waits until the channel catches up.
    public static ReleaseUpdate? FindNewest(JsonElement releases, SemVersion installed, UpdateChannel channel) =>
        FindBest(releases, channel) is { } best && best.Version > installed ? best : null;

    public static bool IsDownloadUri(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps &&
        uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
        uri.Host is "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com";
}
