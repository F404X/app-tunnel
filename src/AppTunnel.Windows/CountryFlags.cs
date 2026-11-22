using System.Reflection;

namespace AppTunnel.Windows;

// Country flags bundled with the app, keyed by ISO 3166 code; loaded once and kept for the session.
internal static class CountryFlags
{
    private static readonly Dictionary<string, Image?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Image? Get(string? code)
    {
        if (code is not { Length: 2 } || !code.All(char.IsAsciiLetter)) return null;
        if (Cache.TryGetValue(code, out var cached)) return cached;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"AppTunnel.Countries.{code.ToLowerInvariant()}.png");
        Image? image = null;
        if (stream is not null)
        {
            using var source = Image.FromStream(stream);
            image = new Bitmap(source);
        }
        Cache[code] = image;
        return image;
    }
}
