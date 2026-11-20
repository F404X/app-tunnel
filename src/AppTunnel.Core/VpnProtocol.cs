using static AppTunnel.Core.Localized;

namespace AppTunnel.Core;

public enum VpnProtocol { WireGuard, WireGuardPadding, AmneziaWG }

public enum ProtocolPreference { Profile, Automatic, WireGuard, WireGuardPadding, AmneziaWG }

public sealed record VpnRoute<T>(T Profile, string Endpoint, VpnProtocol Protocol);

public static class VpnProtocols
{
    public static string Name(VpnProtocol protocol) => protocol switch
    {
        VpnProtocol.WireGuardPadding => T("WireGuard with padding", "WireGuard com preenchimento", "WireGuard с дополнением пакетов"),
        VpnProtocol.AmneziaWG => "AmneziaWG",
        _ => "WireGuard"
    };

    public static string Name(ProtocolPreference preference) => preference switch
    {
        ProtocolPreference.Profile => T("From profile", "Do perfil", "Из профиля"),
        ProtocolPreference.Automatic => T("Automatic", "Automático", "Автоматически"),
        ProtocolPreference.WireGuardPadding => Name(VpnProtocol.WireGuardPadding),
        ProtocolPreference.AmneziaWG => Name(VpnProtocol.AmneziaWG),
        _ => "WireGuard"
    };

    public static IReadOnlyList<VpnRoute<T>> Plan<T>(ProtocolPreference preference, VpnRoute<T> selected,
        IEnumerable<VpnRoute<T>> alternatives)
    {
        if (preference != ProtocolPreference.Automatic)
        {
            var protocol = preference switch
            {
                ProtocolPreference.AmneziaWG => VpnProtocol.AmneziaWG,
                ProtocolPreference.WireGuardPadding => VpnProtocol.WireGuardPadding,
                ProtocolPreference.WireGuard => VpnProtocol.WireGuard,
                _ => selected.Protocol
            };
            return [selected with { Protocol = protocol }];
        }

        var routes = alternatives.Where(route => !EqualityComparer<T>.Default.Equals(route.Profile, selected.Profile))
            .GroupBy(route => (route.Endpoint.ToUpperInvariant(), route.Protocol)).Select(group => group.First()).ToArray();
        var nextProtocol = selected.Protocol == VpnProtocol.AmneziaWG
            ? routes.FirstOrDefault(candidate => candidate.Protocol != VpnProtocol.AmneziaWG)
            : routes.FirstOrDefault(candidate => candidate.Protocol == VpnProtocol.AmneziaWG);
        var plan = new List<VpnRoute<T>> { selected };
        if (selected.Protocol == VpnProtocol.WireGuard) plan.Add(selected with { Protocol = VpnProtocol.WireGuardPadding });
        if (nextProtocol is not null) plan.Add(nextProtocol);
        foreach (var route in routes)
        {
            if (plan.Count >= 4) break;
            if (plan.Any(candidate => candidate.Protocol == route.Protocol &&
                candidate.Endpoint.Equals(route.Endpoint, StringComparison.OrdinalIgnoreCase))) continue;
            plan.Add(route);
        }
        return plan;
    }
}
