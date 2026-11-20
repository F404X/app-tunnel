using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AppTunnel.Core;

[Flags]
public enum TelemetryConsent { Off = 0, Statistics = 1, Errors = 2, Diagnostics = 4 }

public enum TelemetryStage { Startup, Connect, Disconnect, Recovery, Service }

public enum TelemetryEvent { Connected, Disconnected, HandshakeResponse, HandshakeTimeout, EndpointFailure, RoutingFailure, ServiceStopped }

public enum Delivery { Pending, Delivered, Unconfirmed }

public sealed record TelemetryHistory(DateTimeOffset At, TelemetryConsent Kind, string Json, Delivery Status);

public sealed class TelemetryPreview
{
    public string Json { get; }
    internal int Revision { get; }
    internal TelemetryPreview(string json, int revision) => (Json, Revision) = (json, revision);
}

public sealed class TelemetryState
{
    public DateTimeOffset? StatisticsDue { get; set; }
    public int Starts { get; set; }
    public Dictionary<VpnProtocol, ConnectionCounts> Connections { get; set; } = [];
    public StatisticsBatch? PreparedStatistics { get; set; }
    public Dictionary<string, DateTimeOffset> Errors { get; set; } = [];
    public List<TelemetryHistory> History { get; set; } = [];
}

public sealed record StatisticsBatch(string Version, string Os, int Starts, Dictionary<VpnProtocol, ConnectionCounts> Connections);

public sealed class ConnectionCounts
{
    public int Fast { get; set; }
    public int Normal { get; set; }
    public int Slow { get; set; }
    public int Failed { get; set; }
}

public sealed class Telemetry : IDisposable
{
    private const int CounterLimit = 1000;
    private const int HistoryLimit = 60;
    private readonly object gate = new();
    private readonly TimeProvider clock;
    private readonly HttpClient http;
    private readonly Uri? endpoint;
    private readonly string version;
    private readonly string os;
    private readonly Func<TelemetryState, bool> save;
    private readonly Queue<TelemetryEvent> events = new();
    private readonly Dictionary<TelemetryConsent, CancellationTokenSource> channels = [];
    private readonly TelemetryState state;
    private int diagnosticRevision;
    private bool disposed;

    public Telemetry(TelemetryConsent consent, TelemetryState state, Func<TelemetryState, bool> save,
        HttpClient http, Uri? endpoint, Version version, int windowsBuild, TimeProvider? clock = null)
    {
        this.state = state;
        this.save = save;
        this.clock = clock ?? TimeProvider.System;
        this.http = http;
        this.endpoint = endpoint;
        this.version = new Version(version.Major, version.Minor, Math.Max(0, version.Build)).ToString();
        os = windowsBuild >= 22000 ? "windows-11" : windowsBuild >= 10240 ? "windows-10" : "other";
        if (Trim()) save(state);
        ApplyConsent(consent);
    }

    public bool Configured => endpoint is not null;
    public static Uri? ParseEndpoint(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.HostNameType == UriHostNameType.Dns &&
        uri.Host.Contains('.') && uri.AbsolutePath == "/v1/reports" && uri.UserInfo.Length == 0 &&
        uri.Query.Length == 0 && uri.Fragment.Length == 0 ? uri : null;

    public void ApplyConsent(TelemetryConsent consent)
    {
        lock (gate)
        {
            if (disposed) return;
            var changed = false;
            foreach (var kind in new[] { TelemetryConsent.Statistics, TelemetryConsent.Errors, TelemetryConsent.Diagnostics })
            {
                if (consent.HasFlag(kind))
                {
                    if (channels.ContainsKey(kind)) continue;
                    channels[kind] = new CancellationTokenSource();
                    if (kind == TelemetryConsent.Statistics) state.StatisticsDue ??= clock.GetUtcNow().AddHours(24);
                }
                else
                {
                    if (channels.Remove(kind, out var source))
                    {
                        source.Cancel();
                        source.Dispose();
                        if (kind == TelemetryConsent.Diagnostics) diagnosticRevision++;
                        changed = true;
                    }
                    if (kind == TelemetryConsent.Statistics && HasStatistics())
                    {
                        state.Starts = 0;
                        state.Connections.Clear();
                        state.PreparedStatistics = null;
                        changed = true;
                    }
                    if (kind == TelemetryConsent.Diagnostics) events.Clear();
                }
            }
            if (changed) save(state);
        }
    }

    public void RecordStart()
    {
        lock (gate)
        {
            if (!channels.ContainsKey(TelemetryConsent.Statistics)) return;
            state.Starts = Increment(state.Starts);
            save(state);
        }
    }

    public void RecordConnection(VpnProtocol protocol, TimeSpan elapsed, bool success)
    {
        lock (gate)
        {
            if (!channels.ContainsKey(TelemetryConsent.Statistics) || !Enum.IsDefined(protocol)) return;
            if (!state.Connections.TryGetValue(protocol, out var counts)) state.Connections[protocol] = counts = new();
            if (!success) counts.Failed = Increment(counts.Failed);
            else if (elapsed < TimeSpan.FromSeconds(5)) counts.Fast = Increment(counts.Fast);
            else if (elapsed < TimeSpan.FromSeconds(20)) counts.Normal = Increment(counts.Normal);
            else counts.Slow = Increment(counts.Slow);
            save(state);
        }
    }

    public void RecordEvent(TelemetryEvent entry)
    {
        lock (gate)
        {
            if (!channels.ContainsKey(TelemetryConsent.Diagnostics) || !Enum.IsDefined(entry)) return;
            if (events.Count == 24) events.Dequeue();
            events.Enqueue(entry);
        }
    }

    public Task<bool> SendStatisticsAsync()
    {
        lock (gate)
        {
            if (!CanSend(TelemetryConsent.Statistics) || state.StatisticsDue is not { } due ||
                due > clock.GetUtcNow() || !HasStatistics()) return Task.FromResult(false);
            var json = UsageJson(PrepareStatistics());
            state.StatisticsDue = clock.GetUtcNow().AddHours(24);
            state.PreparedStatistics = null;
            return BeginDelivery(TelemetryConsent.Statistics, json);
        }
    }

    public Task<bool> ReportErrorAsync(TelemetryStage stage, Exception error)
    {
        lock (gate)
        {
            if (!Enum.IsDefined(stage) || !CanSend(TelemetryConsent.Errors)) return Task.FromResult(false);
            var category = error switch
            {
                TimeoutException => "timeout",
                FormatException or ArgumentException => "configuration",
                UnauthorizedAccessException => "permissions",
                IOException => "io",
                System.ComponentModel.Win32Exception => "windows",
                InvalidOperationException => "operation",
                _ => "unexpected"
            };
            var key = StageName(stage) + ":" + category;
            var now = clock.GetUtcNow();
            if (state.Errors.TryGetValue(key, out var last) && now - last < TimeSpan.FromHours(24))
                return Task.FromResult(false);
            state.Errors[key] = now;
            return BeginDelivery(TelemetryConsent.Errors, JsonSerializer.Serialize(new
            {
                schema = 1, kind = "error", version, os, stage = StageName(stage), error = category
            }));
        }
    }

    public string? PreviewStatistics()
    {
        lock (gate)
        {
            if (!channels.ContainsKey(TelemetryConsent.Statistics) || !HasStatistics()) return null;
            var batch = PrepareStatistics();
            save(state);
            return UsageJson(batch);
        }
    }

    public TelemetryPreview? PreviewDiagnostics()
    {
        lock (gate)
        {
            if (!channels.ContainsKey(TelemetryConsent.Diagnostics) || events.Count == 0) return null;
            var json = JsonSerializer.Serialize(new
            {
                schema = 1, kind = "diagnostics", version, os, events = events.Select(EventName).ToArray()
            });
            return new(json, diagnosticRevision);
        }
    }

    public Task<bool> SendApprovedDiagnosticsAsync(TelemetryPreview preview)
    {
        lock (gate)
        {
            if (preview.Revision != diagnosticRevision || !CanSend(TelemetryConsent.Diagnostics))
                return Task.FromResult(false);
            // Each approved snapshot is usable once. A new report requires preview and approval again.
            diagnosticRevision++;
            events.Clear();
            return BeginDelivery(TelemetryConsent.Diagnostics, preview.Json);
        }
    }

    public TelemetryHistory[] GetHistory()
    {
        lock (gate)
        {
            if (Trim()) save(state);
            return state.History.ToArray();
        }
    }

    public bool ClearLocalData()
    {
        lock (gate)
        {
            state.History.Clear();
            state.Connections.Clear();
            state.Starts = 0;
            state.PreparedStatistics = null;
            events.Clear();
            diagnosticRevision++;
            return save(state);
        }
    }

    private bool CanSend(TelemetryConsent kind) => endpoint is not null && !disposed && channels.ContainsKey(kind);

    private Task<bool> BeginDelivery(TelemetryConsent kind, string json)
    {
        var entry = new TelemetryHistory(clock.GetUtcNow(), kind, json, Delivery.Pending);
        state.History.Insert(0, entry);
        Trim();
        if (!save(state))
        {
            state.History.Remove(entry);
            return Task.FromResult(false);
        }
        return DeliverAsync(entry, channels[kind].Token);
    }

    private async Task<bool> DeliverAsync(TelemetryHistory entry, CancellationToken revoked)
    {
        var delivered = false;
        try
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(revoked);
            cancellation.CancelAfter(TimeSpan.FromSeconds(12));
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(entry.Json))
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/json") }
            };
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation.Token)
                .ConfigureAwait(false);
            delivered = response.StatusCode == System.Net.HttpStatusCode.Accepted;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or ObjectDisposedException) { }
        lock (gate)
        {
            var index = state.History.IndexOf(entry);
            if (index >= 0)
            {
                state.History[index] = entry with { Status = delivered ? Delivery.Delivered : Delivery.Unconfirmed };
                save(state);
            }
        }
        return delivered;
    }

    private bool Trim()
    {
        var cutoff = clock.GetUtcNow().AddDays(-30);
        var removed = state.History.RemoveAll(item => item.At < cutoff) > 0;
        if (state.History.Count > HistoryLimit)
        {
            state.History.RemoveRange(HistoryLimit, state.History.Count - HistoryLimit);
            removed = true;
        }
        foreach (var entry in state.Errors.Where(pair => pair.Value < cutoff).ToArray()) state.Errors.Remove(entry.Key);
        return removed;
    }

    private bool HasStatistics() => state.PreparedStatistics is not null || state.Starts != 0 || state.Connections.Count != 0;

    private StatisticsBatch PrepareStatistics()
    {
        if (state.PreparedStatistics is { Connections: not null } prepared) return prepared;
        var batch = new StatisticsBatch(version, os, state.Starts, state.Connections);
        state.PreparedStatistics = batch;
        state.Starts = 0;
        state.Connections = [];
        return batch;
    }

    private string UsageJson(StatisticsBatch batch) => JsonSerializer.Serialize(new
    {
        schema = 1, kind = "statistics",
        version = Version.TryParse(batch.Version, out var ver) && ver.Major <= 999 && ver.Minor <= 999 &&
            ver.Build is >= 0 and <= 999 && ver.Revision == -1 ? ver.ToString(3) : version,
        os = batch.Os is "windows-10" or "windows-11" or "other" ? batch.Os : os,
        starts = Bound(batch.Starts),
        protocols = batch.Connections.Where(pair => Enum.IsDefined(pair.Key) && pair.Value is not null).OrderBy(pair => pair.Key).Select(pair => new
        {
            protocol = pair.Key switch
            {
                VpnProtocol.WireGuardPadding => "wireguard-padding",
                VpnProtocol.AmneziaWG => "amneziawg",
                _ => "wireguard"
            },
            fast = Bound(pair.Value.Fast), normal = Bound(pair.Value.Normal), slow = Bound(pair.Value.Slow), failed = Bound(pair.Value.Failed)
        }).ToArray()
    });

    private static int Bound(int count) => Math.Clamp(count, 0, CounterLimit);
    private static int Increment(int count) => Math.Min(Bound(count) + 1, CounterLimit);

    private static string StageName(TelemetryStage stage) => stage switch
    {
        TelemetryStage.Connect => "connect",
        TelemetryStage.Disconnect => "disconnect",
        TelemetryStage.Recovery => "recovery",
        TelemetryStage.Service => "service",
        _ => "startup"
    };

    private static string EventName(TelemetryEvent entry) => entry switch
    {
        TelemetryEvent.Connected => "connected",
        TelemetryEvent.Disconnected => "disconnected",
        TelemetryEvent.HandshakeResponse => "handshake-response",
        TelemetryEvent.HandshakeTimeout => "handshake-timeout",
        TelemetryEvent.EndpointFailure => "endpoint-failure",
        TelemetryEvent.RoutingFailure => "routing-failure",
        _ => "service-stopped"
    };

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            foreach (var source in channels.Values)
            {
                source.Cancel();
                source.Dispose();
            }
            channels.Clear();
            events.Clear();
        }
    }
}
