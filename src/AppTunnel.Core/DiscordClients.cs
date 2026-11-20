namespace AppTunnel.Core;

public static class DiscordClients
{
    public static IReadOnlyList<string> KnownNames { get; } = Array.AsReadOnly(new[]
    {
        "Serein", "Discord", "DiscordPTB", "Discord PTB", "DiscordCanary", "Discord Canary",
        "DiscordDevelopment", "Discord Development", "DiscordDev", "Discord Dev", "Vesktop",
        "Equibop", "Equicord", "ArmCord", "Legcord", "WebCord", "Lightcord", "Dorion",
        "Dissent", "GoofCord", "Discordo", "DiscordRocks", "Ripcord", "VencordDesktop"
    });

    public static string? OfficialName(string executable) => executable.ToLowerInvariant() switch
    {
        "discord.exe" => "Discord",
        "discordptb.exe" => "Discord PTB",
        "discordcanary.exe" => "Discord Canary",
        _ => null
    };

    public static bool IsKnown(string name) => KnownNames.Contains(name, StringComparer.OrdinalIgnoreCase) ||
        (name.StartsWith("serein-", StringComparison.OrdinalIgnoreCase) &&
            (name.Contains("portable", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("windows", StringComparison.OrdinalIgnoreCase)));

    public static bool IsRouted(string path, IEnumerable<ApplicationTarget> rules) =>
        rules.Any(rule => rule.Enabled && (rule.Folder
            ? path.StartsWith(rule.Match.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)
            : path.Equals(rule.Match, StringComparison.OrdinalIgnoreCase)));
}
