using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using AppTunnel.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using static AppTunnel.Core.Localized;

namespace AppTunnel.Proton;

// Runs one Proton API request for the main window without showing anything: an off-screen, never-activated
// window hosts the signed-in Proton page, borrows the session headers from the page's own authenticated API
// calls and makes the request from inside that page. The password and session cookies never leave WebView2.
internal sealed class ApiWindow : Form
{
    // The main window waits longer than this, so the helper always finishes (and writes its result) first.
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(80);
    private readonly string root;
    private readonly string inbox;
    private readonly WebView2 browser = new() { Dock = DockStyle.Fill };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private CoreWebView2? web;
    private string? uid;
    private string? appVersion;
    private string? apiBase;
    private System.Threading.Timer? watchdog;
    private volatile bool creating;

    public ApiWindow(string root, string inbox)
    {
        this.root = root;
        this.inbox = inbox;
        Text = "AppTunnel · Proton";
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-32000, -32000);
        Size = new Size(1024, 768);
        Opacity = 0;
        Controls.Add(browser);
        Shown += async (_, _) => await Run();
    }

    protected override bool ShowWithoutActivation => true;

    protected override void Dispose(bool disposing)
    {
        if (disposing) watchdog?.Dispose();
        base.Dispose(disposing);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x00000080 | 0x08000000; // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
            return parameters;
        }
    }

    private TimeSpan Remaining => Budget - clock.Elapsed;

    private async Task Run()
    {
        watchdog = new System.Threading.Timer(_ =>
        {
            // A key whose registration never finished is marked uncertain so the main window keeps it.
            try
            {
                if (creating && File.Exists(Path.Combine(inbox, "profile.dpapi")) && !File.Exists(Path.Combine(inbox, "registered")))
                    ProtonSync.WriteAtomic(Path.Combine(inbox, "uncertain"), []);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            Environment.Exit(3);
        }, null, Budget + TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);
        ProtonApiResult result = new(Error: T("Proton did not answer.", "A Proton não respondeu.", "Proton не ответил."));
        try
        {
            var command = ReadCommand();
            await Open();
            result = command.Action switch
            {
                ProtonApiCommand.ListServers => await ListServers(),
                ProtonApiCommand.CreateRoute when command.Server is { } server && ProtonRoutes.IsUsable(server) =>
                    await CreateRoute(server, command.DeviceName),
                _ => new ProtonApiResult(Error: T("Unknown request.", "Pedido desconhecido.", "Неизвестный запрос."))
            };
        }
        catch (SignInException)
        {
            result = new ProtonApiResult(SignInRequired: true);
        }
        catch (Exception error) when (error is WebView2RuntimeNotFoundException or COMException or IOException or
            InvalidOperationException or JsonException or FormatException or TimeoutException or UnauthorizedAccessException or
            KeyNotFoundException or System.ComponentModel.Win32Exception)
        {
            result = new ProtonApiResult(Error: error is WebView2RuntimeNotFoundException
                ? T("Install Microsoft Edge WebView2 Runtime to reach Proton.", "Instale o Microsoft Edge WebView2 Runtime para acessar a Proton.",
                    "Установите Microsoft Edge WebView2 Runtime для доступа к Proton.")
                : error is KeyNotFoundException or JsonException
                    ? T("Proton sent an unexpected answer.", "A Proton enviou uma resposta inesperada.", "Proton прислал неожиданный ответ.")
                    : error.Message);
        }
        finally
        {
            try
            {
                ProtonSync.WriteAtomic(Path.Combine(inbox, "result.json"),
                    JsonSerializer.SerializeToUtf8Bytes(result, ProtonRouteJson.Default.ProtonApiResult));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            Close();
        }
    }

    private ProtonApiCommand ReadCommand()
    {
        var path = Path.Combine(inbox, "command.json");
        if (new FileInfo(path).Length > 64 * 1024) throw new FormatException("command");
        return JsonSerializer.Deserialize(File.ReadAllBytes(path), ProtonRouteJson.Default.ProtonApiCommand)
            ?? throw new FormatException("command");
    }

    private async Task Open()
    {
        var options = new CoreWebView2EnvironmentOptions { Language = System.Globalization.CultureInfo.CurrentUICulture.Name };
        var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(root, "WebView"), options);
        await browser.EnsureCoreWebView2Async(environment);
        web = browser.CoreWebView2;
        web.Settings.AreHostObjectsAllowed = false;
        web.Settings.IsWebMessageEnabled = false;
        web.Settings.AreDevToolsEnabled = false;
        web.Settings.IsPasswordAutosaveEnabled = false;
        web.Settings.IsGeneralAutofillEnabled = false;
        web.IsMuted = true;
        web.NavigationStarting += (_, args) =>
        {
            if (!ProtonPortal.IsOfficialUrl(args.Uri)) args.Cancel = true;
        };
        web.NewWindowRequested += (_, args) => args.Handled = true;
        web.DownloadStarting += (_, args) => args.Cancel = true;
        // Only a successful answer to an account request proves the session is signed in; a signed-out
        // visitor also gets a UID. The headers are taken from that request.
        web.WebResourceResponseReceived += (_, args) =>
        {
            var url = args.Request.Uri;
            var marker = url.IndexOf("/api/", StringComparison.Ordinal);
            if (marker < 0 || !ProtonPortal.IsAccountPage(url) || args.Response.StatusCode != 200) return;
            var path = url[(marker + 5)..];
            if (!path.StartsWith("core/v4/users", StringComparison.Ordinal) && !path.StartsWith("vpn/", StringComparison.Ordinal)) return;
            var headers = args.Request.Headers;
            if (!headers.Contains("x-pm-uid") || !headers.Contains("x-pm-appversion")) return;
            uid = headers.GetHeader("x-pm-uid");
            appVersion = headers.GetHeader("x-pm-appversion");
            apiBase = url[..(marker + 4)];
        };
        web.Navigate(ProtonPortal.Downloads);

        while (Remaining > TimeSpan.Zero)
        {
            await Task.Delay(250);
            if (IsSignInPage(web.Source)) throw new SignInException();
            if (uid is not null && ProtonPortal.IsAccountPage(web.Source)) return;
        }
        throw new TimeoutException(T("Proton did not respond. Check your internet connection and try again.",
            "A Proton não respondeu. Verifique sua conexão e tente novamente.", "Proton не отвечает. Проверьте подключение и попробуйте снова."));
    }

    private async Task<ProtonApiResult> ListServers()
    {
        using var vpn = await Call("GET", "vpn/v2");
        var maxTier = vpn.RootElement.TryGetProperty("VPN", out var info) && info.TryGetProperty("MaxTier", out var tier) &&
            tier.TryGetInt32(out var value) ? value : 0;
        using var logicals = await Call("GET", "vpn/v1/logicals?WithIpV6=1");
        var servers = new List<ProtonServerChoice>();
        foreach (var logical in logicals.RootElement.GetProperty("LogicalServers").EnumerateArray())
        {
            if (logical.ValueKind != JsonValueKind.Object) continue;
            if (Int(logical, "Tier") > maxTier || (Int(logical, "Features") & 3) != 0 || Int(logical, "Status", 1) != 1) continue;
            if (!logical.TryGetProperty("Servers", out var list) || list.ValueKind != JsonValueKind.Array) continue;
            var entry = list.EnumerateArray().FirstOrDefault(server => server.ValueKind == JsonValueKind.Object &&
                Int(server, "Status", 1) == 1 && Field(server, "X25519PublicKey") is not null && Field(server, "EntryIP") is not null);
            if (entry.ValueKind != JsonValueKind.Object) continue;
            var choice = new ProtonServerChoice(Field(logical, "ID") ?? "", ProtonSync.SafeName(Field(logical, "Name")),
                (Field(logical, "ExitCountry") ?? "").ToUpperInvariant(), Field(logical, "City") is { } city ? ProtonSync.SafeName(city) : null,
                Int(logical, "Tier"), Int(logical, "Load"),
                logical.TryGetProperty("Score", out var score) && score.TryGetDouble(out var rank) ? rank : double.MaxValue,
                Field(entry, "EntryIP")!, Field(entry, "EntryIPv6"), Field(entry, "X25519PublicKey")!, Field(entry, "Label"));
            if (ProtonRoutes.IsUsable(choice)) servers.Add(choice);
        }
        return new ProtonApiResult(MaxTier: maxTier, Servers: servers.OrderBy(server => server.Score).Take(2000).ToArray());
    }

    private async Task<ProtonApiResult> CreateRoute(ProtonServerChoice server, string? deviceName)
    {
        var pair = ProtonKeyPair.Create();
        var name = ProtonSync.SafeName(deviceName ?? $"AppTunnel {server.Name}");
        var config = ProtonRoutes.BuildConfig(name, pair.WireGuardPrivateKey, server);
        if (ProtonSync.Inspect(config, out var fingerprint) != ProtonProfileState.Available || fingerprint is null)
            throw new FormatException(T("The new profile is not valid.", "O novo perfil não é válido.", "Новый профиль недействителен."));
        // The key is saved before Proton registers it, so a registered route is never left without its key.
        var profile = Path.Combine(inbox, "profile.dpapi");
        ProtonSync.Export(profile, new ProtonConfiguration(server.Name, config));
        // Until Proton confirms or refuses, the key is uncertain and must survive whatever happens next.
        var uncertain = Path.Combine(inbox, "uncertain");
        ProtonSync.WriteAtomic(uncertain, []);
        creating = true;
        var features = new Dictionary<string, object>
        {
            ["peerName"] = server.Name, ["peerIp"] = server.EntryIp, ["peerPublicKey"] = server.PublicKey, ["platform"] = "Windows"
        };
        if (server.Label is { Length: > 0 } label) features["Bouncing"] = label;
        var body = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["ClientPublicKey"] = pair.PublicKey, ["Mode"] = "persistent", ["DeviceName"] = name, ["Features"] = features
        });
        bool confirmed;
        try
        {
            using var response = await Call("POST", "vpn/v1/certificate", body);
            var root = response.RootElement;
            confirmed = root.TryGetProperty("Code", out var code) && code.GetInt32() == 1000 && ProtonRoutes.SameKey(Field(root, "ClientKey"), pair.PublicKey);
        }
        catch (ProtonRefusedException)
        {
            // Proton answered and did not register the key, so the saved copy is useless.
            creating = false;
            File.Delete(profile);
            File.Delete(uncertain);
            throw;
        }
        catch (Exception error) when (error is InvalidOperationException or TimeoutException or JsonException or COMException or SignInException)
        {
            // The answer was lost (or the session ended mid-request), so Proton's own list says whether the key was
            // registered; if that cannot be read either, the key stays marked uncertain.
            confirmed = await IsRegistered(pair.PublicKey);
        }
        if (!confirmed)
        {
            creating = false;
            throw new InvalidOperationException(T("Proton did not confirm the new route. Check the WireGuard list in your Proton account.",
                "A Proton não confirmou a nova rota. Confira a lista WireGuard na sua conta Proton.",
                "Proton не подтвердил новый маршрут. Проверьте список WireGuard в учётной записи Proton."));
        }
        ProtonSync.WriteAtomic(Path.Combine(inbox, "registered"), []);
        File.Delete(uncertain);
        creating = false;
        return new ProtonApiResult(Created: server.Name);
    }


    private async Task<bool> IsRegistered(string publicKey)
    {
        try
        {
            for (var offset = 0; offset < 51 * 10 && Remaining > TimeSpan.FromSeconds(5); offset += 51)
            {
                using var page = await Call("GET", $"vpn/v1/certificate/all?Mode=persistent&Offset={offset}&Limit=51");
                if (!page.RootElement.TryGetProperty("Certificates", out var list) || list.ValueKind != JsonValueKind.Array) return false;
                var count = 0;
                foreach (var certificate in list.EnumerateArray())
                {
                    count++;
                    if (certificate.ValueKind == JsonValueKind.Object && ProtonRoutes.SameKey(Field(certificate, "ClientKey"), publicKey)) return true;
                }
                if (count < 51) return false;
            }
        }
        catch (Exception error) when (error is InvalidOperationException or TimeoutException or JsonException or COMException or SignInException) { }
        return false;
    }

    // Sends the request from inside the Proton page so it carries the page's cookies, and polls for the answer
    // because ExecuteScriptAsync does not wait for promises.
    private async Task<JsonDocument> Call(string method, string path, string? body = null)
    {
        if (web is null || uid is null || appVersion is null || apiBase is null) throw new SignInException();
        var id = Guid.NewGuid().ToString("N");
        static string Js(string? value) => JsonSerializer.Serialize(value);
        var script = $$"""
            (() => {
                window.__appTunnel = window.__appTunnel || {};
                window.__appTunnel[{{Js(id)}}] = null;
                const headers = { 'accept': 'application/vnd.protonmail.v1+json', 'x-pm-uid': {{Js(uid)}}, 'x-pm-appversion': {{Js(appVersion)}} };
                const body = {{Js(body)}};
                if (body !== null) headers['content-type'] = 'application/json';
                fetch({{Js(apiBase + "/" + path)}}, { method: {{Js(method)}}, credentials: 'include', headers, body: body ?? undefined })
                    .then(async r => { window.__appTunnel[{{Js(id)}}] = JSON.stringify({ status: r.status, body: await r.text() }); })
                    .catch(e => { window.__appTunnel[{{Js(id)}}] = JSON.stringify({ status: 0, body: String(e) }); });
                return true;
            })()
            """;
        await web.ExecuteScriptAsync(script);
        // A reloaded document no longer has the pending slot; that is reported instead of waiting for the deadline.
        var read = $"(() => {{ const b = window.__appTunnel; if (!b || !({Js(id)} in b)) return '__gone__'; const v = b[{Js(id)}]; if (v) delete b[{Js(id)}]; return v || null; }})()";
        while (Remaining > TimeSpan.Zero)
        {
            await Task.Delay(150);
            if (IsSignInPage(web.Source)) throw new SignInException();
            var raw = await web.ExecuteScriptAsync(read);
            if (JsonSerializer.Deserialize<string?>(raw) is not { } packed) continue;
            if (packed == "__gone__")
                throw new InvalidOperationException(T("The Proton page reloaded during the request. Try again.",
                    "A página da Proton recarregou durante o pedido. Tente novamente.", "Страница Proton перезагрузилась во время запроса. Попробуйте снова."));
            using var envelope = JsonDocument.Parse(packed);
            var status = envelope.RootElement.GetProperty("status").GetInt32();
            var text = envelope.RootElement.GetProperty("body").GetString() ?? "";
            if (status is 401 or 403) throw new SignInException();
            JsonDocument? document = null;
            try { document = JsonDocument.Parse(text); }
            catch (JsonException) { }
            if (status is >= 200 and < 300 && document is not null &&
                document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("Code", out var code) && code.TryGetInt32(out var value) && value is 1000 or 1001)
                return document;
            var message = document is not null && document.RootElement.ValueKind == JsonValueKind.Object &&
                Field(document.RootElement, "Error") is { } error ? error : $"HTTP {status}";
            document?.Dispose();
            if (status == 0)
                throw new InvalidOperationException(T("Proton could not be reached. Check your internet connection.",
                    "Não foi possível acessar a Proton. Verifique sua conexão.", "Не удалось связаться с Proton. Проверьте подключение."));
            // Only a client error with Proton's own error body is a definite refusal; server errors or unreadable
            // answers leave the outcome unknown.
            if (status is < 400 or >= 500 || message.StartsWith("HTTP ", StringComparison.Ordinal))
                throw new InvalidOperationException(T($"Proton answered unexpectedly: {message}", $"A Proton respondeu de forma inesperada: {message}",
                    $"Proton ответил неожиданно: {message}"));
            throw new ProtonRefusedException(T($"Proton refused the request: {message}", $"A Proton recusou o pedido: {message}",
                $"Proton отклонил запрос: {message}"));
        }
        throw new TimeoutException(T("Proton did not respond. Try again.", "A Proton não respondeu. Tente novamente.", "Proton не отвечает. Попробуйте снова."));
    }

    private static int Int(JsonElement element, string name, int fallback = 0) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : fallback;

    private static string? Field(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;

    private static bool IsSignInPage(string url) => ProtonPortal.IsAccountPage(url) &&
        new Uri(url).AbsolutePath.Split('/').Contains("login", StringComparer.OrdinalIgnoreCase);

    private sealed class SignInException : Exception;

    private sealed class ProtonRefusedException(string message) : InvalidOperationException(message);
}
