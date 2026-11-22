using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using AppTunnel.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using static AppTunnel.Core.Localized;

namespace AppTunnel.Proton;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length is < 2 or > 4 || !Guid.TryParseExact(args[0], "N", out var request)) return;
        // "api" runs one background request for the main window with no visible window.
        var api = args.Length == 4 && args[3] == "api";
        if (args.Length == 4 && !api) return;
        SetLanguage(args[1]);
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AppTunnel", "Proton");
        var inbox = Path.Combine(root, (api ? "api-" : "request-") + request.ToString("N"));
        if (!Directory.Exists(inbox)) return;

        try
        {
            using var guard = new FileStream(Path.Combine(inbox, "running"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            using var identity = WindowsIdentity.GetCurrent();
            if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            {
                File.WriteAllText(Path.Combine(inbox, "failed"), "");
                return;
            }

            ApplicationConfiguration.Initialize();
            if (api)
            {
                using var worker = new ApiWindow(root, inbox);
                Application.Run(worker);
                return;
            }
            using var window = new AccountWindow(root, inbox, dark: args.Length == 2 || args[2] == "dark");
            Application.Run(window);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or COMException or InvalidOperationException)
        {
            if (Directory.Exists(inbox)) File.WriteAllText(Path.Combine(inbox, "failed"), "");
        }
        finally
        {
            if (Directory.Exists(inbox)) File.WriteAllText(Path.Combine(inbox, "closed"), "");
        }
    }
}

internal sealed partial class AccountWindow : Form
{
    private static readonly string ScanScript = LoadScript("read-wireguard.js");
    private readonly string root;
    private readonly string inbox;
    private readonly Label status;
    private readonly WebView2 browser = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 850 };
    private readonly HashSet<string> submitted = new(StringComparer.Ordinal);
    private CoreWebView2? web;
    private bool syncing;
    private bool downloading;
    private bool redirected;
    private bool ready;
    private string? lastCatalog;

    public AccountWindow(string root, string inbox, bool dark)
    {
        this.root = root;
        this.inbox = inbox;
        Text = "AppTunnel · Proton VPN";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1040, 760);
        MinimumSize = new Size(680, 500);
        BackColor = dark ? Color.FromArgb(17, 24, 39) : Color.FromArgb(243, 245, 249);
        ForeColor = dark ? Color.FromArgb(235, 239, 247) : Color.FromArgb(24, 36, 54);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        status = new Label
        {
            Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(16, 0, 12, 0),
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Text = SignInHint()
        };
        layout.Controls.Add(status, 0, 0);
        layout.Controls.Add(BuildCreationPanel(dark), 0, 1);
        layout.Controls.Add(browser, 0, 2);
        Controls.Add(layout);
        Shown += async (_, _) => await StartWebView();
        timer.Tick += async (_, _) => await SyncPage();
    }

    private static string SignInHint() => T("account.protonvpn.com   /   Sign in to sync your WireGuard profiles.",
        "account.protonvpn.com   /   Entre para sincronizar seus perfis WireGuard.", "account.protonvpn.com   /   Войдите, чтобы синхронизировать профили WireGuard.");

    private async Task StartWebView()
    {
        try
        {
            var options = new CoreWebView2EnvironmentOptions { Language = System.Globalization.CultureInfo.CurrentUICulture.Name };
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(root, "WebView"), options);
            if (IsDisposed || Disposing) return;
            await browser.EnsureCoreWebView2Async(environment);
            if (IsDisposed || Disposing) return;
            web = browser.CoreWebView2;
            web.Settings.AreHostObjectsAllowed = false;
            web.Settings.IsWebMessageEnabled = false;
            web.Settings.AreDevToolsEnabled = false;
            web.Settings.AreDefaultContextMenusEnabled = false;
            web.Settings.IsPasswordAutosaveEnabled = false;
            web.Settings.IsGeneralAutofillEnabled = false;
            web.NavigationStarting += (_, args) =>
            {
                if (!ProtonPortal.IsOfficialUrl(args.Uri))
                {
                    args.Cancel = true;
                    return;
                }
                creationForm = default;
                accountProfiles = [];
                RefreshCreationButtons();
                if (IsSignInPage(args.Uri))
                {
                    ready = false;
                    redirected = false;
                    status.Text = SignInHint();
                }
            };
            web.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (ProtonPortal.IsOfficialUrl(args.Uri)) web.Navigate(args.Uri);
            };
            web.DownloadStarting += (_, args) => SaveDownload(web.Source, args);
            web.Navigate(ProtonPortal.Downloads);
            timer.Start();
        }
        catch (Exception error) when (error is WebView2RuntimeNotFoundException or COMException or IOException or InvalidOperationException)
        {
            if (IsDisposed || Disposing) return;
            File.WriteAllText(Path.Combine(inbox, "failed"), "");
            MessageBox.Show(this, T("Install Microsoft Edge WebView2 Runtime to open the Proton account window.",
                "Instale o Microsoft Edge WebView2 Runtime para abrir a janela da conta Proton.", "Установите Microsoft Edge WebView2 Runtime, чтобы открыть окно входа в Proton."),
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    private async Task SyncPage()
    {
        if (IsDisposed || Disposing) return;
        if (TakeSignal("close"))
        {
            Close();
            return;
        }
        if (syncing || web is null) return;
        syncing = true;
        try
        {
            if (TakeSignal("show"))
            {
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate();
            }
            if (TakeSignal("create")) ShowCreationPanel();
            CheckCreationTimeout();
            if (!ProtonPortal.IsAccountPage(web.Source)) return;
            if (IsSignInPage(web.Source))
            {
                Publish(new(false, []));
                return;
            }
            var response = await web.ExecuteScriptAsync(ScanScript);
            if (IsDisposed || Disposing) return;
            var scan = JsonSerializer.Deserialize(response, ProtonJson.Default.ProtonPageScan);
            if (scan is null) return;
            creationForm = (scan.Ready, scan.CanCreate, scan.CanChooseServer, scan.LoadingMore);
            if (!scan.Ready)
            {
                RefreshCreationButtons();
                if (!redirected && scan.DownloadsUrl is { } url && ProtonPortal.IsAccountPage(url))
                {
                    redirected = true;
                    web.Navigate(url.Split('#')[0] + "#wireguard-configuration");
                }
                return;
            }

            ready = true;
            var account = new List<ProtonAccountProfile>();
            foreach (var entry in scan.Profiles.Take(512))
            {
                var name = ProtonSync.SafeName(entry.Name);
                var state = ProtonSync.Inspect(entry.Configuration, out var fingerprint);
                if (fingerprint is not null && entry.Configuration is not null)
                    Queue(name, entry.Configuration, fingerprint);
                account.Add(new ProtonAccountProfile(name, entry.Server is null ? null : ProtonSync.SafeName(entry.Server),
                    state, fingerprint));
            }
            var profiles = account.ToArray();
            accountProfiles = profiles;
            Publish(new(true, profiles));
            FinishCreation();
            RefreshCreationButtons();
        }
        catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException or JsonException or
            InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            if (!IsDisposed && !Disposing)
                status.Text = T("Sync paused. Keep the Proton WireGuard page open to retry.",
                    "Sincronização pausada. Mantenha a página WireGuard da Proton aberta para tentar novamente.", "Синхронизация приостановлена. Оставьте страницу WireGuard в Proton открытой для повторной попытки.");
        }
        finally { syncing = false; }
    }

    private void Queue(string name, string configuration, string fingerprint)
    {
        if (submitted.Contains(fingerprint)) return;
        var target = Path.Combine(inbox, "profile-" + fingerprint + ".dpapi");
        ProtonSync.Export(target, new ProtonConfiguration(name, configuration));
        submitted.Add(fingerprint);
    }

    private void Publish(ProtonAccountSnapshot snapshot)
    {
        var json = JsonSerializer.Serialize(snapshot, ProtonJson.Default.ProtonAccountSnapshot);
        if (lastCatalog == json) return;
        ProtonSync.WriteAtomic(Path.Combine(root, "account.json"), System.Text.Encoding.UTF8.GetBytes(json));
        lastCatalog = json;
        if (ready && snapshot.Ready)
        {
            var usable = snapshot.Profiles.Count(profile => profile.State == ProtonProfileState.Available);
            status.Text = T($"Proton account synced   /   {snapshot.Profiles.Length} profiles   /   {usable} available in this session",
                $"Conta Proton sincronizada   /   {snapshot.Profiles.Length} perfis   /   {usable} disponíveis nesta sessão", $"Учётная запись Proton синхронизирована   /   профилей: {snapshot.Profiles.Length}   /   доступно в этом сеансе: {usable}");
        }
    }

    private bool TakeSignal(string name)
    {
        var path = Path.Combine(inbox, name);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    private void SaveDownload(string origin, CoreWebView2DownloadStartingEventArgs args)
    {
        args.Handled = true;
        var download = args.DownloadOperation;
        if (downloading || !ProtonPortal.IsWireGuardDownload(origin, download.Uri, args.ResultFilePath))
        {
            args.Cancel = true;
            return;
        }

        downloading = true;
        var filename = Path.GetFileNameWithoutExtension(args.ResultFilePath);
        var partial = Path.Combine(inbox, $"download-{Guid.NewGuid():N}.conf");
        args.ResultFilePath = partial;
        download.StateChanged += (_, _) =>
        {
            if (download.State == CoreWebView2DownloadState.InProgress) return;
            downloading = false;
            try
            {
                if (download.State == CoreWebView2DownloadState.Interrupted) return;
                var info = new FileInfo(partial);
                if (info.Length > 65536) return;
                var profile = File.ReadAllText(partial);
                var state = ProtonSync.Inspect(profile, out var fingerprint);
                if (state == ProtonProfileState.Available && fingerprint is not null)
                {
                    Queue(ProtonSync.GetDeviceName(profile) ?? ProtonSync.SafeName(filename), profile, fingerprint);
                    status.Text = T("WireGuard profile saved for AppTunnel.", "Perfil WireGuard salvo no AppTunnel.", "Профиль WireGuard сохранён в AppTunnel.");
                }
                else
                {
                    status.Text = state == ProtonProfileState.KeyRequired
                        ? T("This profile needs its original .conf or a newly created profile.",
                            "Para usar este perfil, importe o .conf original ou crie um novo perfil.", "Для этого профиля нужен исходный файл .conf. Можно также создать новый профиль.")
                        : T("This profile is not supported by AppTunnel.", "Este perfil não é compatível com o AppTunnel.", "AppTunnel не поддерживает этот профиль.");
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                status.Text = T("Profile export failed. Try downloading it again.", "Falha ao exportar o perfil. Tente novamente.", "Не удалось экспортировать профиль. Попробуйте скачать его снова.");
            }
            finally { File.Delete(partial); }
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }

    private static bool IsSignInPage(string url) => ProtonPortal.IsAccountPage(url) &&
        new Uri(url).AbsolutePath.Split('/').Contains("login", StringComparer.OrdinalIgnoreCase);

    private static string LoadScript(string file)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"AppTunnel.Proton.Scripts.{file}")
            ?? throw new InvalidOperationException(T("The Proton account integration is unavailable.",
                "A integração com a conta Proton está indisponível.", "Интеграция с учётной записью Proton недоступна."));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
