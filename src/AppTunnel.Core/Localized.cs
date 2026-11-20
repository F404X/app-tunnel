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
    }

    public static string T(string english, string portuguese, string russian) => Language switch
    {
        "pt" => portuguese,
        "ru" => russian,
        _ => english
    };
}
