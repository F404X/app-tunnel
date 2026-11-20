using AppTunnel.Core;
using static AppTunnel.Core.Localized;
using static AppTunnel.Windows.DesktopTheme;

namespace AppTunnel.Windows;

internal sealed partial class MainForm
{
    private readonly CheckBox tunnelDns = MakeCheck(T("Use profile DNS (shared resolver)", "Usar DNS do perfil (resolvedor compartilhado)",
        "DNS профиля (общий резолвер)"));
    private readonly CheckBox guardIpv6 = MakeCheck(T("Block public IPv6 when outside VPN", "Bloquear IPv6 público sem rota IPv6",
        "Блокировать публичный IPv6 без маршрута"));
    private readonly ComboBox protocolMode = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Font = Body(),
        BackColor = Raised, ForeColor = DesktopTheme.Text, Width = 245, Margin = new Padding(8, 2, 0, 0)
    };
    private readonly Label healthDetails = MakeLabel("", muted: true);
    private TunnelHealth lastHealth;
    private bool recoveryUsed;

    private ProtocolPreference SelectedProtocol => (ProtocolPreference)Math.Max(0, protocolMode.SelectedIndex);

    private Control BuildProtocolControl()
    {
        var caption = T("VPN protocol", "Protocolo VPN", "Протокол VPN");
        var label = MakeLabel(caption, muted: true);
        label.Dock = DockStyle.None;
        label.AutoSize = true;
        label.Margin = new Padding(0, 6, 2, 0);
        protocolMode.AccessibleName = caption;
        foreach (var protocol in Enum.GetValues<ProtocolPreference>()) protocolMode.Items.Add(VpnProtocols.Name(protocol));
        return MakeBar(label, protocolMode);
    }

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
            var plan = VpnProtocols.Plan(ProtocolPreference.Automatic, route, settings.Profiles.Select(profile =>
                new VpnRoute<StoredProfile>(profile, profile.Endpoint, profile.Protocol))).Skip(1).ToArray();
            if (plan.Length == 0) return;
            var routed = session.RequestedPrograms.Select(path =>
                new ApplicationTarget(path, path, Folder: path.EndsWith('\\')))
                .Where(rule => rule.Folder ? Directory.Exists(rule.Match) : File.Exists(rule.Match)).ToArray();
            if (routed.Length == 0) return;
            activeSession = await Task.Run(() => engine.Connect(plan, profile => store.Read(profile.Id), routed,
                session.RequestedPrograms, session.Options, session.Diagnostics, session.EnginePath,
                ProtocolPreference.Automatic, AddLog,
                session.RequestedProfile == Guid.Empty ? session.Profile.Id : session.RequestedProfile));
            ReadActiveDiagnostics(activeSession);
            footer.Text = T("Fallback route applied. Reopen any affected calls.", "Rota alternativa aplicada. Reabra chamadas afetadas.",
                "Резервный маршрут применён. Возобновите прерванные звонки.");
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
}
