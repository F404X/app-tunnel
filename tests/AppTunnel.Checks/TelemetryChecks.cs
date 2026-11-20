using System.Net;
using System.Text.Json;
using AppTunnel.Core;

internal static class TelemetryChecks
{
    public static void Run()
    {
        var clock = new TestClock();
        var requests = new List<string>();
        var state = new TelemetryState();
        var saves = 0;
        using var client = new HttpClient(new TestHandler((request, _) =>
        {
            requests.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Require(request.Headers.Authorization is null);
            Require(request.RequestUri!.AbsolutePath == "/v1/reports");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted));
        }));
        Telemetry Build(TelemetryConsent consent, TelemetryState current) =>
            new(consent, current, _ => { saves++; return true; }, client, Telemetry.ParseEndpoint("https://testing.example/v1/reports"),
                new Version(0, 6, 0), 26200, clock);

        using (var telemetry = Build(TelemetryConsent.Off, state))
        {
            var before = saves;
            telemetry.RecordStart();
            telemetry.RecordConnection(VpnProtocol.WireGuard, TimeSpan.FromSeconds(2), true);
            telemetry.RecordEvent(TelemetryEvent.Connected);
            Require(!telemetry.ReportErrorAsync(TelemetryStage.Connect, new IOException("private-path")).GetAwaiter().GetResult());
            Require(telemetry.PreviewDiagnostics() is null && telemetry.PreviewStatistics() is null && saves == before);
            Require(requests.Count == 0);
            telemetry.ApplyConsent(TelemetryConsent.Diagnostics);
            telemetry.RecordEvent(TelemetryEvent.Connected);
            var diagnostic = telemetry.PreviewDiagnostics()!;
            Require(requests.Count == 0);
            Require(telemetry.SendApprovedDiagnosticsAsync(diagnostic).GetAwaiter().GetResult());
            Require(!telemetry.SendApprovedDiagnosticsAsync(diagnostic).GetAwaiter().GetResult());
            Require(requests.Single() == diagnostic.Json);
            telemetry.RecordEvent(TelemetryEvent.ServiceStopped);
            var invalidated = telemetry.PreviewDiagnostics()!;
            telemetry.ApplyConsent(TelemetryConsent.Off);
            telemetry.ApplyConsent(TelemetryConsent.Diagnostics);
            Require(!telemetry.SendApprovedDiagnosticsAsync(invalidated).GetAwaiter().GetResult());

            telemetry.ApplyConsent(TelemetryConsent.Statistics | TelemetryConsent.Errors);
            telemetry.RecordStart();
            telemetry.RecordConnection(VpnProtocol.WireGuard, TimeSpan.FromSeconds(2), true);
            var example = telemetry.PreviewStatistics()!;
            telemetry.RecordStart();
            Require(telemetry.PreviewStatistics() == example);
            Require(!telemetry.SendStatisticsAsync().GetAwaiter().GetResult());
            clock.Now = clock.Now.AddHours(24);
            Require(telemetry.SendStatisticsAsync().GetAwaiter().GetResult());
            Require(requests[^1] == example);
            Require(!telemetry.SendStatisticsAsync().GetAwaiter().GetResult());
            const string sensitive = "198.51.100.2 secret-key C:/Users/owner private@example.invalid";
            Require(telemetry.ReportErrorAsync(TelemetryStage.Connect, new IOException(sensitive)).GetAwaiter().GetResult());
            Require(!telemetry.ReportErrorAsync(TelemetryStage.Connect, new IOException(sensitive)).GetAwaiter().GetResult());
            Require(requests[^1] == "{\"schema\":1,\"kind\":\"error\",\"version\":\"0.6.0\",\"os\":\"windows-11\",\"stage\":\"connect\",\"error\":\"io\"}");
            var history = telemetry.GetHistory();
            Require(history.Select(item => item.Json).SequenceEqual(requests.AsEnumerable().Reverse()));
            Require(history.All(item => item.Status == Delivery.Delivered));
        }

        var persisted = JsonSerializer.Deserialize<TelemetryState>(JsonSerializer.Serialize(state))!;
        using (var reopened = Build(TelemetryConsent.Statistics | TelemetryConsent.Errors, persisted))
        {
            Require(reopened.GetHistory().Length == 3);
            Require(!reopened.SendStatisticsAsync().GetAwaiter().GetResult());
            Require(!reopened.ReportErrorAsync(TelemetryStage.Connect, new IOException()).GetAwaiter().GetResult());
            Require(reopened.ClearLocalData() && reopened.GetHistory().Length == 0);
            Require(!reopened.ReportErrorAsync(TelemetryStage.Connect, new IOException()).GetAwaiter().GetResult());
        }

        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stalled = new HttpClient(new TestHandler((_, cancellation) => {
            cancellation.Register(() => pending.TrySetCanceled(cancellation));
            return pending.Task;
        }));
        using var revoking = new Telemetry(TelemetryConsent.Errors, new(), _ => true, stalled,
            Telemetry.ParseEndpoint("https://testing.example/v1/reports"), new Version(0, 6, 0), 19045, clock);
        var sending = revoking.ReportErrorAsync(TelemetryStage.Recovery, new TimeoutException());
        revoking.ApplyConsent(TelemetryConsent.Off);
        Require(!sending.GetAwaiter().GetResult() && pending.Task.IsCanceled);
        Require(revoking.GetHistory().Single().Status == Delivery.Unconfirmed);

        using var unavailable = new HttpClient(new TestHandler((_, _) => throw new HttpRequestException()));
        using var failed = new Telemetry(TelemetryConsent.Errors, new(), _ => true, unavailable,
            Telemetry.ParseEndpoint("https://testing.example/v1/reports"), new Version(0, 6, 0), 19045, clock);
        Require(!failed.ReportErrorAsync(TelemetryStage.Service, new InvalidOperationException()).GetAwaiter().GetResult());
        Require(failed.GetHistory().Single().Status == Delivery.Unconfirmed);
        Require(Telemetry.ParseEndpoint("http://example.com/v1/reports") is null);
        Require(Telemetry.ParseEndpoint("https://user@example.com/v1/reports") is null);
        Require(Telemetry.ParseEndpoint("https://example.com/v1/reports?hidden=1") is null);
    }

    private static void Require(bool value)
    {
        if (!value) throw new InvalidOperationException("Telemetry consent, preview or delivery assertion failed.");
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.Parse("2026-01-01T12:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class TestHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> run) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            run(request, cancellationToken);
    }
}
