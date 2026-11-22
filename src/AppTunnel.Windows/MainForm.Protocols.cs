using AppTunnel.Core;
using static AppTunnel.Core.Localized;
using static AppTunnel.Windows.DesktopTheme;

namespace AppTunnel.Windows;

internal sealed partial class MainForm
{
    private readonly CheckBox tunnelDns = MakeCheck(T("Use the profile DNS", "Usar o DNS do perfil", "DNS из профиля"));
    private readonly CheckBox guardIpv6 = MakeCheck(T("Block IPv6 without a route (prevents leaks)", "Bloquear IPv6 sem rota (evita vazamento)",
        "Блокировать IPv6 без маршрута (против утечек)"));
    private readonly ThemeCombo protocolMode = new() { Margin = new Padding(0) };
    private readonly Label healthDetails = MakeLabel("", muted: true);
    private TunnelHealth lastHealth;
    private bool recoveryUsed;

    private ProtocolPreference SelectedProtocol => (ProtocolPreference)Math.Max(0, protocolMode.SelectedIndex);

    private TunnelHealth ReadActiveDiagnostics(TunnelSession session)
    {
        var state = engine.ReadHealth(session);
        if (state == lastHealth && healthDetails.Text.Length != 0) return state;
        lastHealth = state;
        healthDetails.Text = TunnelDiagnostics.Describe(state);
        if (state != TunnelHealth.Unknown) AddLog(healthDetails.Text);
        var ev = state switch
        {
            TunnelHealth.HandshakeReceived => TelemetryEvent.HandshakeResponse,
            TunnelHealth.HandshakeTimedOut => TelemetryEvent.HandshakeTimeout,
            TunnelHealth.EndpointResolutionFailed => TelemetryEvent.EndpointFailure,
            TunnelHealth.RoutingFailed => TelemetryEvent.RoutingFailure,
            _ => (TelemetryEvent?)null
        };
        if (ev is { } item) telemetry.RecordEvent(item);
        // The Home health card reads lastHealth, so a new state redraws it.
        if (!IsDisposed && !Disposing && !initializing) UpdateState();
        return state;
    }

    private async Task RecoverRoute(TunnelSession session)
    {
        recoveryUsed = true;
        busy = true;
        operation = T("Trying a fallback route…", "Tentando uma rota alternativa…", "Подбор резервного маршрута…");
        UpdateState();
        try
        {
            var route = new VpnRoute<StoredProfile>(session.Profile, session.Profile.Endpoint, session.Protocol);
            // Automatic protocol tries other protocols; automatic server mode also moves to the other servers.
            // A pinned server never leaves its endpoint.
            var others = settings.Profiles.Where(profile => profile.Id != session.Profile.Id &&
                    !profile.Endpoint.Equals(session.Profile.Endpoint, StringComparison.OrdinalIgnoreCase))
                .Select(profile => new VpnRoute<StoredProfile>(profile, profile.Endpoint, profile.Protocol)).ToArray();
            VpnRoute<StoredProfile>[] plan;
            if (session.RequestedProtocol == ProtocolPreference.Automatic)
            {
                plan = VpnProtocols.Plan(ProtocolPreference.Automatic, route, settings.AutoServer ? others : []).Skip(1).ToArray();
                if (settings.AutoServer && plan.Length == 0) plan = others.Take(3).ToArray();
            }
            // An explicit protocol (or the profile's own) is kept on every fallback server.
            else plan = settings.AutoServer
                ? others.Take(3).Select(other => VpnProtocols.Plan(session.RequestedProtocol, other, [])[0]).ToArray()
                : [];
            if (plan.Length == 0) return;
            var routed = SessionApplications(session);
            if (routed.Length == 0) return;
            activeSession = await Task.Run(() => engine.Connect(plan, profile => store.Read(profile.Id), routed,
                session.RequestedPrograms, session.Options, session.Diagnostics, session.EnginePath,
                session.RequestedProtocol, AddLog,
                session.RequestedProfile == Guid.Empty ? session.Profile.Id : session.RequestedProfile,
                tryEveryRoute: settings.AutoServer));
            ReadActiveDiagnostics(activeSession);
            footer.Text = T("Moved to a backup server. Reopen any affected calls.", "Mudou para um servidor reserva. Reabra chamadas afetadas.",
                "Переключено на резервный сервер. Возобновите прерванные звонки.");
        }
        catch (Exception error) when (Expected(error))
        {
            _ = telemetry.ReportErrorAsync(TelemetryStage.Recovery, error);
            AddLog(error.Message);
            footer.Text = T("Automatic recovery did not find a working alternative. See Diagnostics.",
                "A recuperação automática não encontrou uma alternativa. Consulte Diagnóstico.",
                "Автоматическое восстановление не нашло рабочую альтернативу. Откройте «Диагностику».");
        }
        finally
        {
            busy = false;
            UpdateState();
        }
    }

    private static ApplicationTarget[] SessionApplications(TunnelSession session) => session.RequestedPrograms
        .Select(path => new ApplicationTarget(path, path, Folder: Directory.Exists(path)))
        .Where(rule => rule.Folder || File.Exists(rule.Match)).ToArray();
}
