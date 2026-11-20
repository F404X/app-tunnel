using static AppTunnel.Core.Localized;

namespace AppTunnel.Core;

public enum TunnelHealth { Unknown, HandshakeSent, HandshakeReceived, EndpointResolutionFailed, RoutingFailed, HandshakeTimedOut }

public static class TunnelDiagnostics
{
    public static TunnelHealth Observe(TunnelHealth previous, string line)
    {
        if (Contains("[TUN]: Handshake response received from")) return TunnelHealth.HandshakeReceived;
        if (Contains("Endpoint is either invalid") || Contains("No WireGuard server endpoint provided or failed to resolve") ||
            Contains("endpoint socket is invalid")) return TunnelHealth.EndpointResolutionFailed;
        if (Contains("Failed to figure out the route to") || Contains("WireGuard Server is unreachable") ||
            Contains("virtual adapter driver is not loaded") || Contains("Failed to activate WireSock virtual adapter"))
            return TunnelHealth.RoutingFailed;
        if (Contains("Handshake initiation sent to WireGuard server") || Contains("Sending Handshake packet to WireGuard Server success"))
            return previous == TunnelHealth.HandshakeTimedOut ? previous : TunnelHealth.HandshakeSent;
        return previous;
        bool Contains(string value) => line.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    public static string Describe(TunnelHealth health) => health switch
    {
        TunnelHealth.HandshakeReceived => T("WireSock recorded a handshake response.", "O WireSock registrou uma resposta ao handshake.",
            "WireSock зарегистрировал ответ на рукопожатие."),
        TunnelHealth.HandshakeSent => T("WireSock sent the handshake. Waiting for a response.",
            "O WireSock enviou o handshake. Aguardando resposta.", "WireSock отправил рукопожатие. Ожидание ответа."),
        TunnelHealth.HandshakeTimedOut => T("The endpoint did not reply to the WireGuard handshake. Filtering, an offline server or mismatched keys are possible.",
            "O endpoint não respondeu ao handshake. Pode haver filtragem, servidor indisponível ou chaves incompatíveis.",
            "Сервер не ответил на рукопожатие. Возможны фильтрация, недоступность сервера или несовпадение ключей."),
        TunnelHealth.EndpointResolutionFailed => T("WireSock reported an endpoint resolution or socket error.",
            "O WireSock registrou um erro na resolução ou no socket do endpoint.",
            "WireSock сообщил об ошибке разрешения адреса или сокета сервера."),
        TunnelHealth.RoutingFailed => T("WireSock reported a routing or tunnel adapter error.",
            "O WireSock registrou um erro de roteamento ou do adaptador do túnel.",
            "WireSock сообщил об ошибке маршрутизации или адаптера туннеля."),
        _ => T("Handshake status is unavailable. Generate traffic from a selected app and check the WireSock logs.",
            "O status do handshake está indisponível. Gere tráfego em um app selecionado e consulte os registros do WireSock.",
            "Статус рукопожатия недоступен. Создайте трафик в выбранном приложении и проверьте журналы WireSock.")
    };
}
