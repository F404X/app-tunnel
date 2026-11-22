namespace AppTunnel.Core;

public sealed record CommonApp(string Name, params string[] Aliases)
{
    public IEnumerable<string> Names => Aliases.Prepend(Name);

    // Executable names another app also uses (Opera GX runs opera.exe), recognized only inside this app's folders.
    public string[] Executables { get; init; } = [];

    // Nested install folders below Program Files or AppData, such as Google\Chrome\Application.
    public string[] Folders { get; init; } = [];

    // Browsers are offered in Explore common apps, but not in the setup assistant, which keeps the browser direct.
    public bool Browser { get; init; }

    public IEnumerable<string> ExecutableNames => Names.Concat(Executables);
    public IEnumerable<string> FolderNames => Names.Concat(Folders);
}

public static class CommonApps
{
    private static readonly CommonApp Serein = new("Serein");

    public static IReadOnlyList<CommonApp> Catalog { get; } = MakeCatalog();

    public static CommonApp? Find(string name)
    {
        if (DiscordClients.IsKnown(name) && name.StartsWith("serein", StringComparison.OrdinalIgnoreCase))
            return Serein;
        var key = MatchKey(name);
        return Catalog.FirstOrDefault(app => app.Names.Any(alias => MatchKey(alias) == key));
    }

    // The app an executable belongs to, judged by its name and the folders on its path, so opera.exe inside
    // "Opera GX" is Opera GX and chrome.exe inside "Chromium" is Chromium.
    public static CommonApp? FindByPath(string path)
    {
        var key = MatchKey(Path.GetFileNameWithoutExtension(path));
        var folders = path.Replace('/', '\\');
        return Catalog.FirstOrDefault(app => app.ExecutableNames.Any(name => MatchKey(name) == key) &&
            app.FolderNames.Any(folder => folders.Contains('\\' + folder + '\\', StringComparison.OrdinalIgnoreCase)));
    }

    // Whether a running process with this name could be one of the catalog's apps.
    // Find keeps recognizing names it matches by rule, such as portable Serein builds.
    public static bool IsKnownExecutable(string name)
    {
        if (Find(name) is not null) return true;
        var key = MatchKey(name);
        return Catalog.Any(app => app.ExecutableNames.Any(executable => MatchKey(executable) == key));
    }

    private static string MatchKey(string name) =>
        string.Concat(name.Where(char.IsLetterOrDigit)).ToUpperInvariant();

    private static CommonApp[] MakeCatalog()
    {
        var common = new CommonApp[]
        {
            new("Discord"), new("Discord PTB", "DiscordPTB"), new("Discord Canary", "DiscordCanary"),
            Serein, new("Discord Development", "DiscordDevelopment", "Discord Dev", "DiscordDev"),
            new("Legcord", "ArmCord"), new("Vesktop", "VencordDesktop"),
            new("Telegram", "Telegram Desktop"), new("Signal", "Signal Desktop", "signal-desktop"),
            new("WhatsApp", "WhatsApp Desktop"), new("Element", "Element Desktop", "element-desktop"),
            new("Steam"), new("Spotify"), new("Viber"), new("Zoom", "Zoom Workplace", "Zoom Meetings")
        };
        // Browsers come last, after every Discord client, as their own group.
        var browsers = new CommonApp[]
        {
            new("Google Chrome", "Chrome") { Folders = [@"Google\Chrome\Application"], Browser = true },
            new("Microsoft Edge", "Edge", "msedge") { Folders = [@"Microsoft\Edge\Application"], Browser = true },
            new("Brave", "Brave Browser") { Folders = [@"BraveSoftware\Brave-Browser\Application"], Browser = true },
            new("Firefox", "Mozilla Firefox") { Browser = true },
            new("Opera", "Opera Stable") { Browser = true },
            new("Opera GX", "Opera GX Stable") { Executables = ["opera"], Browser = true },
            new("Vivaldi") { Folders = [@"Vivaldi\Application"], Browser = true },
            new("Yandex Browser", "Yandex") { Executables = ["browser"], Folders = [@"Yandex\YandexBrowser\Application"], Browser = true },
            new("Chromium") { Executables = ["chrome"], Folders = [@"Chromium\Application"], Browser = true },
            new("LibreWolf") { Browser = true },
            new("Waterfox") { Browser = true }
        };
        var keys = common.Concat(browsers).SelectMany(app => app.Names).Select(MatchKey).ToHashSet(StringComparer.Ordinal);
        return [.. common, .. DiscordClients.KnownNames.Where(name => keys.Add(MatchKey(name))).Select(name => new CommonApp(name)), .. browsers];
    }
}
