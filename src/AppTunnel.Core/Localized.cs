using System.Globalization;

namespace AppTunnel.Core;

public static class Localized
{
    public static string Language => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
    {
        "pt" => "pt",
        "ru" => "ru",
        _ => "en"
    };

    public static void SetLanguage(string? language)
    {
        var culture = CultureInfo.GetCultureInfo(language switch
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
