using static AppTunnel.Core.Localized;
namespace AppTunnel.Core;

public sealed record ApplicationTarget(string Label, string Match, bool Enabled = true, bool Folder = false)
{
    public static string Validate(string target)
    {
        target = target.Trim().Replace('/', '\\');
        if (target.Length is < 4 or > 1024 || target.Any(c => char.IsControl(c) || ",#;*?\"<>|".Contains(c)))
            throw new FormatException(T("The path contains characters that WireSock cannot use in its app list.", "O caminho contém caracteres incompatíveis com a lista do WireSock.", "Путь содержит символы, недопустимые в списке приложений WireSock."));

        var folder = target.EndsWith('\\');
        var path = target.TrimEnd('\\');
        if (path.Length < 4 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\' ||
            path[3..].Contains(':') ||
            path[3..].Split('\\').Any(segment => segment.Length == 0 || segment.EndsWith('.') || segment.EndsWith(' ')))
            throw new FormatException(T("Choose an executable or a local folder with a full path.", "Escolha um executável ou uma pasta local com caminho completo.", "Выберите исполняемый файл или локальную папку с полным путём."));
        if (!folder && !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new FormatException(T("Choose an .exe file.", "Escolha um arquivo .exe.", "Выберите файл .exe."));
        // Preserve the folder boundary so similarly named sibling folders are excluded.
        return folder ? path + "\\" : path;
    }
}
