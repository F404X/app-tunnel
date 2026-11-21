using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using AppTunnel.Core;
using static AppTunnel.Core.Localized;

namespace AppTunnel.Windows;

internal sealed partial class MainForm
{
    private readonly RouteLatency routeLatency = new();
    private readonly Stopwatch latencyClock = Stopwatch.StartNew();
    private readonly CancellationTokenSource latencyLifetime = new();
    private bool probingActiveRoute;

    private static string RouteHost(StoredProfile profile) =>
        Uri.TryCreate($"udp://{profile.Endpoint}", UriKind.Absolute, out var uri)
            ? uri.IdnHost.TrimEnd('.') : "";

    private static async Task<long?> PingRouteAsync(StoredProfile profile, int samples, int minimum, CancellationToken token)
    {
        var host = RouteHost(profile);
        if (host.Length == 0) return null;
        try
        {
            using var client = new Ping();
            var data = new byte[24];
            long total = 0;
            var replies = 0;
            for (var sample = 0; sample < samples; sample++)
            {
                var reply = await client.SendPingAsync(host, TimeSpan.FromMilliseconds(1600), data, options: null, token);
                if (reply.Status != IPStatus.Success) continue;
                total += reply.RoundtripTime;
                replies++;
            }
            return replies < minimum ? null : (long)Math.Round((double)total / replies);
        }
        catch (Exception error) when (error is PingException or SocketException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private static async Task<(StoredProfile profile, long? latency)[]> PingRoutesAsync(StoredProfile[] profiles,
        int minimum, CancellationToken token)
    {
        using var limiter = new SemaphoreSlim(6);
        return await Task.WhenAll(profiles.Select(async profile =>
        {
            await limiter.WaitAsync(token);
            try { return (profile, latency: await PingRouteAsync(profile, 2, minimum, token)); }
            finally { limiter.Release(); }
        }));
    }

    private async Task MonitorRouteLatency(TunnelSession session)
    {
        if (probingActiveRoute || settings.Profiles.Count < 2) return;
        probingActiveRoute = true;
        var token = latencyLifetime.Token;
        try
        {
            var latency = await PingRouteAsync(session.Profile, 1, 1, token);
            if (!CanSwitchRoute(session) || !routeLatency.ShouldSearch(session.ConfigPath, latencyClock.Elapsed, latency)) return;

            var activeHost = RouteHost(session.Profile);
            var alternatives = settings.Profiles.Where(profile => profile.Id != session.Profile.Id &&
                (profile.Protocol == VpnProtocol.AmneziaWG) == (session.Protocol == VpnProtocol.AmneziaWG) &&
                (!session.Options.UseIpv6 || !session.Profile.HasIpv6 || profile.HasIpv6))
                .GroupBy(RouteHost, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Key.Length > 0 && !group.Key.Equals(activeHost, StringComparison.OrdinalIgnoreCase))
                .Select(group => group.First()).ToArray();
            if (alternatives.Length == 0) return;

            var measured = await PingRoutesAsync(alternatives, minimum: 2, token);
            if (!CanSwitchRoute(session)) return;
            var best = measured.Where(item => item.latency.HasValue && settings.Profiles.Any(profile =>
                    profile.Id == item.profile.Id && profile.Endpoint == item.profile.Endpoint))
                .OrderBy(item => item.latency).FirstOrDefault();
            if (best.profile is null) return;

            var current = await PingRouteAsync(session.Profile, 1, 1, token);
            if (!CanSwitchRoute(session) || current is null or <= RouteLatency.SlowMilliseconds ||
                best.latency is not { } better || better >= current.Value ||
                !settings.Profiles.Any(profile => profile.Id == best.profile.Id && profile.Endpoint == best.profile.Endpoint))
                return;

            await SwitchSlowRoute(session, best.profile, better);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { probingActiveRoute = false; }
    }

    private bool CanSwitchRoute(TunnelSession session) => !IsDisposed && !Disposing && Enabled &&
        !HasPendingOperation && activeSession?.ConfigPath == session.ConfigPath;

    private async Task SwitchSlowRoute(TunnelSession session, StoredProfile next, long latency)
    {
        var routed = SessionApplications(session);
        if (routed.Length == 0) return;
        busy = true;
        operation = T("Switching to a faster route…", "Trocando para uma rota mais rápida…", "Переключение на более быстрый маршрут…");
        UpdateState();
        try
        {
            var route = new VpnRoute<StoredProfile>(next, next.Endpoint, session.Protocol);
            await Task.Run(() =>
            {
                if (engine.GetActive()?.ConfigPath != session.ConfigPath) return;
                engine.Connect([route], profile => store.Read(profile.Id), routed, session.RequestedPrograms,
                    session.Options, session.Diagnostics, session.EnginePath, session.RequestedProtocol, AddLog,
                    session.RequestedProfile == Guid.Empty ? session.Profile.Id : session.RequestedProfile, verifyHandshake: true);
            });
            activeSession = engine.GetActive();
            if (activeSession?.Profile.Id != next.Id) return;
            ReadActiveDiagnostics(activeSession);
            footer.Text = T($"Switched to {next.Name} · {latency} ms.", $"Rota alterada para {next.Name} · {latency} ms.",
                $"Маршрут переключён на {next.Name} · {latency} мс.");
            AddLog(footer.Text);
        }
        catch (Exception error) when (Expected(error))
        {
            _ = telemetry.ReportErrorAsync(TelemetryStage.Recovery, error);
            AddLog(error.Message);
            try { activeSession = engine.GetActive(); }
            catch (Exception statusError) when (Expected(statusError)) { AddLog(statusError.Message); }
            footer.Text = T("Automatic route change failed. See Diagnostics.", "Falha na troca automática de rota. Consulte Diagnóstico.",
                "Ошибка автоматической смены маршрута. Откройте «Диагностику».");
        }
        finally
        {
            busy = false;
            if (!IsDisposed && !Disposing) UpdateState();
        }
    }
}
