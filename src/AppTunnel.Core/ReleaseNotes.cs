using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AppTunnel.Core;

public sealed record ReleaseNote(SemVersion Version, string Text);

// What changed between two versions, read from the GitHub release notes: a short list before updating and the
// complete notes after it.
public static partial class ReleaseNotes
{
    // The notes of every published release in one page of the GitHub releases list, cleaned for display.
    public static IEnumerable<ReleaseNote> Read(JsonElement releases)
    {
        if (releases.ValueKind != JsonValueKind.Array) yield break;
        foreach (var release in releases.EnumerateArray())
        {
            if (ReleaseUpdate.Published(release) is not { } version) continue;
            var body = release.TryGetProperty("body", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            var text = Clean(body ?? "");
            if (text.Length > 0) yield return new ReleaseNote(version, text);
        }
    }

    // Keeps what changed and drops what every release repeats: the title, download instructions, the nightly
    // preamble and the checksums. Markdown becomes plain text with bullets.
    public static string Clean(string body)
    {
        var text = Comment().Replace(body.Replace("\r\n", "\n"), "");
        var lines = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("## AppTunnel", StringComparison.Ordinal) || line.StartsWith("Download **AppTunnel", StringComparison.Ordinal) ||
                line.StartsWith("Installer SHA-256", StringComparison.Ordinal) || line.StartsWith("ZIP SHA-256", StringComparison.Ordinal) ||
                line.StartsWith("Automatic nightly build", StringComparison.Ordinal) || line == "### Changes")
                continue;
            if (line.StartsWith('#')) line = line.TrimStart('#').Trim();
            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal)) line = "• " + line[2..];
            line = Link().Replace(line, "$1").Replace("**", "").Replace("`", "");
            line = Emphasis().Replace(line, "$1");
            if (line.Length == 0 && (lines.Count == 0 || lines[^1].Length == 0)) continue;
            lines.Add(line);
        }
        return string.Join("\n", lines).Trim();
    }

    // The releases after one version up to and including another, on the given channel, newest first.
    // Where the notes of an update start: after the installed version, or, for a build from another channel (a
    // nightly moving to a beta), after the channel's last release with a lower version number, so every release of
    // the installed number and above is shown.
    public static SemVersion Since(IEnumerable<ReleaseNote> notes, SemVersion installed, SemVersion update, UpdateChannel channel) =>
        ReleaseUpdate.Includes(channel, installed) && installed < update ? installed
            : notes.Select(note => note.Version)
                .Where(version => ReleaseUpdate.Includes(channel, version) && version.Numeric < installed.Numeric)
                .DefaultIfEmpty(new SemVersion(0, 0, 0, [])).Max()!;

    public static IReadOnlyList<ReleaseNote> Between(IEnumerable<ReleaseNote> notes, SemVersion after, SemVersion upTo, UpdateChannel channel) =>
        notes.Where(note => note.Version > after && note.Version <= upTo && ReleaseUpdate.Includes(channel, note.Version))
            .DistinctBy(note => note.Version.ToString()).OrderByDescending(note => note.Version).ToArray();

    // A few short lines: each change cut to its title ("Updates install again" from "Updates install again: the
    // confirmation…") or to one short sentence. Only each release's first list counts, its changes; later lists,
    // such as an overview of everything the app does, and paragraphs that are not changes are left for Read more.
    public static string Summary(IReadOnlyList<ReleaseNote> notes, int count = 5)
    {
        var changes = notes.SelectMany(note => FirstList(note.Text)).Select(line => "• " + Shorten(line[2..])).ToList();
        if (changes.Count == 0)
            return notes.Select(note => note.Text.Split('\n')[0]).Select(Shorten).FirstOrDefault() ?? "";
        var shown = changes.Take(count).ToList();
        if (changes.Count > count) shown.Add(Localized.T($"…and {changes.Count - count} more", $"…e mais {changes.Count - count}",
            $"…и ещё {changes.Count - count}"));
        return string.Join("\n", shown);
    }

    // Every release in the range with its version as a heading, newest first.
    public static string Full(IReadOnlyList<ReleaseNote> notes)
    {
        var text = new StringBuilder();
        foreach (var note in notes)
        {
            if (text.Length > 0) text.Append("\n\n");
            text.Append("AppTunnel ").Append(note.Version).Append('\n').Append(note.Text);
        }
        return text.ToString();
    }

    private static IEnumerable<string> FirstList(string text) => text.Split('\n')
        .SkipWhile(line => !line.StartsWith("• ", StringComparison.Ordinal))
        .TakeWhile(line => line.StartsWith("• ", StringComparison.Ordinal));

    private static string Shorten(string line)
    {
        var colon = line.IndexOf(": ", StringComparison.Ordinal);
        if (colon is > 0 and <= 60) return line[..colon];
        const int limit = 110;
        if (line.Length <= limit) return line;
        var cut = line.LastIndexOf(' ', limit);
        return line[..(cut > 40 ? cut : limit)].TrimEnd(',', ';', ' ') + "…";
    }

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"(?<![\w*])\*([^*\s][^*]*?)\*(?![\w*])")]
    private static partial Regex Emphasis();
}
