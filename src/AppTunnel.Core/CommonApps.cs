namespace AppTunnel.Core;

public sealed record CommonApp(string Name, params string[] Aliases)
{
    public IEnumerable<string> Names => Aliases.Prepend(Name);
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
        var keys = common.SelectMany(app => app.Names).Select(MatchKey).ToHashSet(StringComparer.Ordinal);
        return [.. common, .. DiscordClients.KnownNames.Where(name => keys.Add(MatchKey(name))).Select(name => new CommonApp(name))];
    }
}
