using System.Globalization;

namespace AppTunnel.Core;

public static class Localized
{
    public static bool Portuguese => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt";

    public static void SetLanguage(string? language)
    {
        var culture = CultureInfo.GetCultureInfo(language == "pt" ? "pt-BR" : "en-US");
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public static string T(string english, string portuguese) => Portuguese ? portuguese : english;
}
