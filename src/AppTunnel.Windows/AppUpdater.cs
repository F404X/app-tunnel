using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using AppTunnel.Core;
using static AppTunnel.Core.Localized;

namespace AppTunnel.Windows;

internal static class AppUpdater
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    public static Version CurrentVersion
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version!;
            return new Version(version.Major, version.Minor, version.Build);
        }
    }

    public static async Task<ReleaseUpdate?> CheckAsync(CancellationToken cancellation)
    {
        using var request = NewRequest(new Uri(ReleaseUpdate.ReleasesApi));
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellation);
        using var releases = await JsonDocument.ParseAsync(body, cancellationToken: cancellation);
        return ReleaseUpdate.FindNewest(releases.RootElement, CurrentVersion);
    }

    public static async Task<string> DownloadAsync(ReleaseUpdate update, IProgress<int> progress, CancellationToken cancellation)
    {
        // Keep verified executable bytes outside user-writable directories before launching elevated Setup.
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AppTunnel Updates");
        Directory.CreateDirectory(folder);
        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
            throw new IOException(T("The update folder is a link.", "A pasta de atualizações é um link."));

        var temporary = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".download");
        try
        {
            using var response = await OpenDownloadAsync(update.Download, cancellation);
            await using var body = await response.Content.ReadAsStreamAsync(cancellation);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long size = 0;
            var lastPercent = -1;
            var buffer = new byte[64 * 1024];
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            {
                int count;
                while ((count = await body.ReadAsync(buffer, cancellation)) > 0)
                {
                    size += count;
                    if (size > update.Size) throw InvalidDownload();
                    hash.AppendData(buffer, 0, count);
                    await file.WriteAsync(buffer.AsMemory(0, count), cancellation);
                    var percent = (int)(100 * size / update.Size);
                    if (percent == lastPercent) continue;
                    progress.Report(percent);
                    lastPercent = percent;
                }
            }
            if (size != update.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                throw InvalidDownload();
            // Each check returns its own verified installer, including when another Windows user checks at the same time.
            var verified = Path.Combine(folder, $"AppTunnel-{update.Version}-{ReleaseUpdate.InstallerName}");
            File.Move(temporary, verified, overwrite: true);
            return verified;
        }
        finally { File.Delete(temporary); }
    }

    private static async Task<HttpResponseMessage> OpenDownloadAsync(Uri uri, CancellationToken cancellation)
    {
        for (var redirects = 0; redirects <= 4; redirects++)
        {
            if (!ReleaseUpdate.IsDownloadUri(uri)) break;
            using var request = NewRequest(uri);
            var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
            if (response.IsSuccessStatusCode) return response;
            using (response)
            {
                var next = response.Headers.Location;
                if ((int)response.StatusCode is < 300 or >= 400 || next is null) response.EnsureSuccessStatusCode();
                if (next is null) break;
                uri = next.IsAbsoluteUri ? next : new Uri(uri, next);
            }
        }
        throw InvalidDownload();
    }

    private static HttpRequestMessage NewRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd($"AppTunnel/{CurrentVersion}");
        return request;
    }

    private static IOException InvalidDownload() => new(T("The downloaded installer could not be verified.",
        "Não foi possível verificar o instalador baixado."));
}
