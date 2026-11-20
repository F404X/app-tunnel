using System.ComponentModel;
using AppTunnel.Core;
using static AppTunnel.Core.Localized;

namespace AppTunnel.Windows;

internal interface ITunnelEngine
{
    event Action<string>? Output;
    bool Supports(VpnProtocol protocol);
    TunnelSession? GetActive();
    VpnSessionConfig Prepare(WireGuardProfile profile, string[] applications, RouteOptions options, VpnProtocol protocol);
    TunnelSession Start(string preferredPath, VpnSessionConfig config, TunnelSession request, bool verifyHandshake);
    void Stop();
    TunnelHealth ReadHealth(TunnelSession session);
}

internal sealed class TunnelEngines
{
    private readonly ITunnelEngine[] engines;

    public TunnelEngines(Action<string> output, params ITunnelEngine[] engines)
    {
        this.engines = engines;
        foreach (var engine in engines) engine.Output += output;
    }

    public TunnelSession? GetActive() => ActiveEngine()?.Session;

    public TunnelSession Connect(IReadOnlyList<VpnRoute<StoredProfile>> plan, Func<StoredProfile, WireGuardProfile> readProfile,
        ApplicationTarget[] routed, string[] requested, RouteOptions options, bool diagnostics,
        string preferredPath, ProtocolPreference preference, Action<string> output, Guid requestedProfile = default)
    {
        var automatic = preference == ProtocolPreference.Automatic;
        var paths = routed.Select(target => target.Match).ToArray();
        var attempted = new HashSet<(string Endpoint, VpnProtocol Protocol)>();
        var failures = new List<string>();
        foreach (var candidate in plan)
        {
            try
            {
                var profile = readProfile(candidate.Profile);
                if (!attempted.Add((profile.Endpoint.ToUpperInvariant(), candidate.Protocol))) continue;
                var active = ActiveEngine();
                var engine = engines.FirstOrDefault(item => item.Supports(candidate.Protocol)) ??
                    throw new InvalidOperationException(T("No installed engine supports this protocol.",
                        "Nenhum motor instalado oferece este protocolo.", "Ни один установленный движок не поддерживает этот протокол."));
                if (active is { } previous && previous.Engine != engine)
                    throw new InvalidOperationException(T("Disconnect the active engine to switch to another engine.",
                        "Desconecte o motor ativo para trocar de motor.", "Отключите активный движок, чтобы перейти на другой."));
                var config = engine.Prepare(profile, paths, options, candidate.Protocol);
                var protocol = VpnProtocols.Name(candidate.Protocol);
                output(T($"Trying {candidate.Profile.Name} · {protocol} · {profile.Endpoint}",
                    $"Tentando {candidate.Profile.Name} · {protocol} · {profile.Endpoint}",
                    $"Подключение к {candidate.Profile.Name} · {protocol} · {profile.Endpoint}"));
                var request = new TunnelSession(candidate.Profile with { Endpoint = profile.Endpoint }, paths.Length,
                    requested, options, diagnostics, Protocol: candidate.Protocol,
                    BlockedIpv6Applications: config.BlockPublicIpv6 ? paths : null,
                    RequestedProfile: requestedProfile == Guid.Empty ? plan[0].Profile.Id : requestedProfile,
                    RequestedProtocol: preference);
                return engine.Start(preferredPath, config, request, verifyHandshake: automatic);
            }
            catch (Exception error) when (automatic && error is (FormatException or IOException or Win32Exception or
                InvalidOperationException or TimeoutException or System.ServiceProcess.TimeoutException))
            {
                var description = $"{candidate.Profile.Name} · {VpnProtocols.Name(candidate.Protocol)} · {error.Message}";
                output(description);
                failures.Add(description);
            }
        }
        throw new InvalidOperationException(T("Could not connect using the available routes.", "Falha ao conectar pelas rotas disponíveis.",
            "Не удалось подключиться по доступным маршрутам.") + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    public void Stop()
    {
        if (ActiveEngine() is { } active) active.Engine.Stop();
        else
        {
            foreach (var engine in engines) engine.Stop();
        }
    }

    public TunnelHealth ReadHealth(TunnelSession session) =>
        ActiveEngine() is { } active && active.Session.ConfigPath == session.ConfigPath
            ? active.Engine.ReadHealth(session) : TunnelHealth.Unknown;

    private (ITunnelEngine Engine, TunnelSession Session)? ActiveEngine()
    {
        foreach (var engine in engines)
        {
            if (engine.GetActive() is { } session) return (engine, session);
        }
        return null;
    }
}
