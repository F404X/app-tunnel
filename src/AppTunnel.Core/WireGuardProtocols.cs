using System.Globalization;
using static AppTunnel.Core.Localized;

namespace AppTunnel.Core;

public sealed partial class WireGuardProfile
{
    private static readonly string[] PaddingFields = ["Jc", "Jmin", "Jmax", "Jd"];
    private static readonly string[] AmneziaFields = ["S1", "S2", "H1", "H2", "H3", "H4"];

    public VpnProtocol Protocol => local.ContainsKey("H1") ? VpnProtocol.AmneziaWG :
        local.TryGetValue("Jc", out var count) && count != "0" ? VpnProtocol.WireGuardPadding : VpnProtocol.WireGuard;

    private void ValidateProtocol()
    {
        if (AmneziaFields.Any(local.ContainsKey))
        {
            var headers = new HashSet<uint>();
            foreach (var key in AmneziaFields)
            {
                if (!local.TryGetValue(key, out var field) ||
                    !uint.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
                    ((key is "S1" or "S2") && value > 1300) ||
                    (key.StartsWith('H') && (!headers.Add(value) || value <= 4)))
                    throw new FormatException(T("AmneziaWG requires S1, S2 and four distinct integer headers greater than 4.",
                        "O AmneziaWG exige S1, S2 e quatro cabeçalhos inteiros distintos maiores que 4.",
                        "Для AmneziaWG нужны S1, S2 и четыре разных целочисленных заголовка больше 4."));
                local[key] = value.ToString(CultureInfo.InvariantCulture);
            }
        }

        CheckNumber(local, "Jc", 0, 10);
        CheckNumber(local, "Jmin", 0, 1300);
        CheckNumber(local, "Jmax", 0, 1300);
        CheckNumber(local, "Jd", 0, 1000);
        if (PaddingValue("Jmin", 50) > PaddingValue("Jmax", 1000))
            throw new FormatException(T("Jmin must be smaller than or equal to Jmax.",
                "Jmin deve ser menor ou igual a Jmax.", "Значение Jmin не должно превышать Jmax."));
        foreach (var key in PaddingFields)
        {
            if (local.ContainsKey(key)) local[key] = PaddingValue(key, 0).ToString(CultureInfo.InvariantCulture);
        }
        int PaddingValue(string key, int fallback) => local.TryGetValue(key, out var value)
            ? int.Parse(value, CultureInfo.InvariantCulture) : fallback;
    }

    private void WriteProtocol(List<string> lines, VpnProtocol protocol)
    {
        if ((Protocol == VpnProtocol.AmneziaWG) != (protocol == VpnProtocol.AmneziaWG))
            throw new FormatException(T("Choose an imported profile that matches the selected protocol. AmneziaWG requires its own server and parameters.",
                "Selecione um perfil importado compatível com o protocolo. AmneziaWG exige servidor e parâmetros próprios.",
                "Выберите профиль, соответствующий протоколу. Для AmneziaWG нужны совместимый сервер и параметры."));

        if (protocol == VpnProtocol.AmneziaWG)
        {
            foreach (var key in AmneziaFields)
                lines.Add($"{key} = {local[key]}");
        }

        if (protocol == VpnProtocol.WireGuardPadding && Protocol == VpnProtocol.WireGuard)
        {
            lines.AddRange(["Jc = 4", "Jmin = 50", "Jmax = 1000"]);
        }
        else if (protocol != VpnProtocol.WireGuard)
        {
            foreach (var key in PaddingFields)
            {
                if (local.TryGetValue(key, out var value)) lines.Add($"{key} = {value}");
            }
        }
    }
}
