using System.Globalization;

namespace AppTunnel.Core;

public static class Localized
{
    // The chosen language is kept for the whole process. The thread culture alone is not enough: Windows Forms runs
    // BeginInvoke callbacks in a captured execution context and .NET restores the culture when one ends, so a
    // language picked from a menu reverted on the UI thread and every window opened afterwards, such as Choose
    // route, came up in the old language.
    private static volatile string? chosen;

    public static string Language => chosen ?? Normalize(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

    private static string Normalize(string? language) => language?.Split('-', '_')[0].ToLowerInvariant() switch
    {
        "pt" => "pt",
        "ru" => "ru",
        _ => "en"
    };

    public static void SetLanguage(string? language)
    {
        chosen = Normalize(language);
        var culture = CultureInfo.GetCultureInfo(chosen switch
        {
            "pt" => "pt-BR",
            "ru" => "ru-RU",
            _ => "en-US"
        });
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        // Casing, sorting and number formatting follow the chosen language as well, not the Windows region.
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.CurrentCulture = culture;
    }

    // Locale code in the form Proton's API expects for translated messages.
    public static string ProtonLocale => Language switch { "pt" => "pt_BR", "ru" => "ru_RU", _ => "en_US" };

    // Language name used by the AppTunnel installer (/LANG=).
    public static string InstallerLanguage => Language switch { "pt" => "brazilianportuguese", "ru" => "russian", _ => "english" };

    public static string T(string english, string portuguese, string russian) => Language switch
    {
        "pt" => portuguese,
        "ru" => russian,
        _ => english
    };
}
