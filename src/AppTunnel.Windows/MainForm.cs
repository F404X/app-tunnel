using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using AppTunnel.Core;
using static AppTunnel.Windows.DesktopTheme;
using static AppTunnel.Core.Localized;

namespace AppTunnel.Windows;

internal sealed partial class MainForm : Form
{
    private const string ProtonUrl = "https://protonvpn.com/support/wireguard-configurations";
    private const string InstallCommand = "winget install --id NTKERNEL.WireSockVPNClientCLI --exact --source winget";
    private readonly ProfileStore store;
    private readonly Preferences settings;
    private readonly WireSockEngine engine;
    private readonly ProtonLogin proton;
    private readonly ConcurrentQueue<string> queuedLogs = new();
    private readonly Queue<string> history = new();
    private readonly System.Windows.Forms.Timer monitor = new() { Interval = 450 };
    private TunnelSession? activeSession;
    private string operation = "";
    private bool busy;
    private bool pingBusy;
    private bool rendering;
    private bool initializing = true;
    private int pollTicks;
    private bool monitorFailed;

    public int CurrentPage { get; private set; }
    public bool HasPendingOperation => busy || pingBusy || updateBusy;
    public bool CanDisconnect => !busy && !updateBusy && activeSession is not null;
    public event Action<string>? LanguageRequested;

    private readonly StatusPill pill = new();
    private readonly Label liveTitle = MakeLabel("");
    private readonly Label liveDetails = MakeLabel("", muted: true);
    private readonly Label routeCaption = MakeLabel("", muted: true);
    private readonly Label routeTitle = MakeLabel("");
    private readonly Label routeDetails = MakeLabel("", muted: true);
    private readonly Label protonAccount = MakeLabel("", muted: true);
    private readonly Label appCount = MakeLabel("", muted: true);
    private readonly Label emptyApps = MakeLabel("", muted: true);
    private readonly Label emptyRoutes = MakeLabel("", muted: true);
    private readonly Label footer = MakeLabel("", muted: true);
    private readonly Label engineInfo = MakeLabel("", muted: true);
    private readonly ThemeButton start = MakeButton(T("Connect", "Conectar"), ButtonKind.Primary);
    private readonly ThemeButton apply = MakeButton(T("Apply settings", "Aplicar ajustes"), ButtonKind.Primary);
    private readonly ThemeButton stop = MakeButton(T("Disconnect", "Desconectar"));
    private readonly ThemeButton ping = MakeButton(T("Measure latency", "Medir latência"));
    private readonly ThemeButton fastest = MakeButton(T("Lowest ping", "Menor ping"));
    private readonly ThemeButton rename = MakeButton(T("Rename", "Renomear"), ButtonKind.Quiet);
    private readonly ThemeButton remove = MakeButton(T("Remove", "Remover"), ButtonKind.Quiet);
    private readonly DataGridView apps = MakeGrid();
    private readonly DataGridView routes = MakeGrid();
    private readonly CheckBox lan = MakeCheck(T("Keep local traffic on the direct connection", "Manter a rede local pela conexão direta"));
    private readonly CheckBox ipv6 = MakeCheck(T("Include IPv6 when available", "Incluir IPv6 quando disponível"));
    private readonly CheckBox diagnostics = MakeCheck(T("Enable detailed WireSock logs", "Detalhar registros do WireSock"));
    private readonly TextBox include = MakeText(placeholder: T("All destinations in the profile", "Todos os destinos do perfil"));
    private readonly TextBox exclude = MakeText(placeholder: T("IP addresses or CIDR ranges, comma-separated", "IPs ou redes CIDR, separados por vírgulas"));
    private readonly TextBox log = MakeText(multiline: true);
    private readonly Panel pageHost = new() { Dock = DockStyle.Fill, Margin = new Padding(0) };
    private readonly List<Control> pages = [];
    private ThemeButton[] navigationButtons = [];
    private readonly Dictionary<Guid, long?> latencies = new();

    public MainForm(ProfileStore store, ProtonLogin proton)
    {
        this.store = store;
        this.proton = proton;
        settings = store.Load();
        engine = new WireSockEngine();
        engine.Output += AddLog;
        activeSession = engine.GetActive();

        Text = "AppTunnel";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1140, 770);
        MinimumSize = new Size(985, 722);
        BackColor = Background;
        ForeColor = DesktopTheme.Text;
        Font = Body();
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        BuildLayout();
        lan.Checked = settings.Route.DirectLan;
        ipv6.Checked = settings.Route.UseIpv6;
        include.Text = settings.Route.Include;
        exclude.Text = settings.Route.Exclude;
        diagnostics.Checked = settings.DiagnosticLogs;
        DrawApps();
        DrawRoutes();
        UpdateEngineInfo();
        initializing = false;
        UpdateState();

        monitor.Tick += (_, _) => Poll();
        monitor.Start();
        Shown += (_, _) => UpdateState();
    }

    private void WireActions()
    {
        start.Click += async (_, _) =>
        {
            if (SelectedRoute() is null)
            {
                OpenPage(1);
                return;
            }
            if (!settings.Applications.Any(x => x.Enabled))
            {
                OpenPage(0);
                return;
            }
            await ChangeConnection(starting: true);
        };
        apply.Click += async (_, _) => await ChangeConnection(starting: true);
        stop.Click += async (_, _) => await ChangeConnection(starting: false);
        ping.Click += async (_, _) => await MeasureLatency(false);
        fastest.Click += async (_, _) => await MeasureLatency(true);
        rename.Click += (_, _) => RenameRoute();
        remove.Click += (_, _) => RemoveRoute();
        apps.CellValueChanged += (_, args) =>
        {
            if (rendering || args.RowIndex < 0 || args.ColumnIndex != 0 ||
                apps.Rows[args.RowIndex].Tag is not string match) return;
            var index = settings.Applications.FindIndex(x => x.Match.Equals(match, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return;
            var current = settings.Applications[index];
            var enabled = apps.Rows[args.RowIndex].Cells[0].Value is true;
            if (current.Enabled == enabled) return;
            settings.Applications[index] = current with { Enabled = enabled };
            Changed();
        };
        routes.SelectionChanged += (_, _) =>
        {
            if (rendering || routes.SelectedRows.Count == 0 ||
                routes.SelectedRows[0].Tag is not Guid id || id == settings.SelectedProfile) return;
            settings.SelectedProfile = id;
            Changed();
        };
        lan.CheckedChanged += (_, _) => Changed();
        ipv6.CheckedChanged += (_, _) => Changed();
        include.TextChanged += (_, _) => Changed(save: false);
        exclude.TextChanged += (_, _) => Changed(save: false);
        diagnostics.CheckedChanged += (_, _) => Changed();
    }

    private void AddKnown()
    {
        Attempt(() =>
        {
            var found = ApplicationDiscovery.FindInstalled().ToArray();
            foreach (var target in found) AddTarget(target);
            DrawApps();
            Changed();
            footer.Text = found.Length > 0
                ? T($"Detected {found.Length} matching installations or executables.", $"{found.Length} instalação(ões) ou executável(is) reconhecido(s).")
                : T("No clients detected. Add an executable, a folder, or a running app.",
                    "Nenhum cliente detectado. Adicione um executável, uma pasta ou um programa em execução.");
        });
    }

    private void AddExecutable()
    {
        using var picker = new OpenFileDialog { Filter = T("Windows executables (*.exe)|*.exe", "Executáveis do Windows (*.exe)|*.exe"), Multiselect = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        Attempt(() =>
        {
            var targets = picker.FileNames.Select(path => new ApplicationTarget(Path.GetFileNameWithoutExtension(path),
                ApplicationTarget.Validate(path))).ToArray();
            foreach (var target in targets) AddTarget(target);
            DrawApps();
            Changed();
        });
    }

    private void AddFolder()
    {
        using var picker = new FolderBrowserDialog
        {
            Description = T("Choose the app's installation folder. This includes all executables in the folder.", "Escolha a pasta de instalação do programa. Todos os executáveis nela entram na seleção."),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        Attempt(() =>
        {
            var path = ApplicationTarget.Validate(Path.TrimEndingDirectorySeparator(picker.SelectedPath) + "\\");
            AddTarget(new ApplicationTarget(Path.GetFileName(path.TrimEnd('\\')), path, Folder: true));
            DrawApps();
            Changed();
        });
    }

    private void AddRunning()
    {
        Attempt(() =>
        {
            var running = ApplicationDiscovery.FindRunning();
            using var window = new Form
            {
                Text = T("Running apps", "Programas em execução"),
                ClientSize = new Size(790, 505), MinimumSize = new Size(620, 350),
                StartPosition = FormStartPosition.CenterParent, BackColor = Background,
                ForeColor = DesktopTheme.Text, Font = Body(), Padding = new Padding(17, 12, 17, 10)
            };
            using var layout = Column(33, 0, 44);
            layout.Controls.Add(SectionTitle(T("Select apps", "Selecione os programas")), 0, 0);
            using var list = MakeGrid();
            AddCheckColumn(list);
            list.Columns[0].HeaderText = T("Include", "Incluir");
            AddColumn(list, T("App", "Programa"), 205, font: Body(bold: true));
            AddColumn(list, T("Executable", "Executável"), 280, fill: true, font: Mono);
            foreach (var target in running)
                list.Rows[list.Rows.Add(false, target.Label, target.Match)].Tag = target;
            list.ClearSelection();
            using var empty = MakeLabel("", muted: true);
            empty.Visible = list.Rows.Count == 0;
            using var frame = GridFrame(list, empty, T("No running apps found.", "Nenhum programa em execução foi encontrado."));
            layout.Controls.Add(frame, 0, 1);

            using var confirm = MakeButton(T("Add selected apps", "Adicionar marcados"), ButtonKind.Primary);
            confirm.DialogResult = DialogResult.OK;
            confirm.Enabled = false;
            list.CellValueChanged += (_, _) =>
                confirm.Enabled = list.Rows.Cast<DataGridViewRow>().Any(row => row.Cells[0].Value is true);
            using var actionBar = MakeBar(confirm);
            actionBar.Padding = new Padding(0, 4, 0, 0);
            layout.Controls.Add(actionBar, 0, 2);
            window.Controls.Add(layout);
            window.AcceptButton = confirm;
            if (window.ShowDialog(this) != DialogResult.OK) return;
            foreach (DataGridViewRow row in list.Rows)
                if (row.Cells[0].Value is true && row.Tag is ApplicationTarget target) AddTarget(target);
            DrawApps();
            Changed();
        });
    }

    private void AddTarget(ApplicationTarget target)
    {
        var match = ApplicationTarget.Validate(target.Match);
        if (!settings.Applications.Any(x => x.Match.Equals(match, StringComparison.OrdinalIgnoreCase)))
            settings.Applications.Add(target with { Match = match });
    }

    private void RemoveApps()
    {
        var remove = apps.SelectedRows.Cast<DataGridViewRow>().Select(x => (string)x.Tag!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (settings.Applications.RemoveAll(x => remove.Contains(x.Match)) == 0) return;
        DrawApps();
        Changed();
    }

    private void UpdateTargets(Func<ApplicationTarget, bool> predicate)
    {
        for (var i = 0; i < settings.Applications.Count; i++)
        {
            var target = settings.Applications[i];
            settings.Applications[i] = target with { Enabled = predicate(target) };
        }
    }

    private void SetChecks(bool enabled)
    {
        UpdateTargets(_ => enabled);
        DrawApps();
        Changed();
    }

    private void DrawApps()
    {
        rendering = true;
        apps.SuspendLayout();
        try
        {
            apps.Rows.Clear();
            foreach (var target in settings.Applications.OrderBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase))
            {
                var index = apps.Rows.Add(target.Enabled, target.Label, target.Folder ? T("Folder", "Pasta") : "EXE", target.Match);
                apps.Rows[index].Tag = target.Match;
                apps.Rows[index].Cells[3].ToolTipText = target.Match;
            }
            apps.ClearSelection();
            emptyApps.Visible = apps.Rows.Count == 0;
        }
        finally
        {
            apps.ResumeLayout();
            rendering = false;
        }
    }

    private void ImportProfiles()
    {
        if (busy || pingBusy) return;
        using var picker = new OpenFileDialog { Filter = T("WireGuard profiles (*.conf)|*.conf", "Perfis WireGuard (*.conf)|*.conf"), Multiselect = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        var added = 0;
        foreach (var file in picker.FileNames)
        {
            Attempt(() =>
            {
                var profile = store.Import(file);
                settings.Profiles.Add(profile);
                settings.SelectedProfile = profile.Id;
                added++;
            });
        }
        if (added == 0) return;
        DrawRoutes();
        Changed();
        footer.Text = T($"Imported {added} profiles. Rename a profile to make it easier to identify.", $"{added} perfil(is) importado(s). Dê um nome à rota para identificá-la.");
    }

    private StoredProfile? SelectedRoute() => settings.Profiles.Find(profile => profile.Id == settings.SelectedProfile);

    private void RenameRoute()
    {
        if (SelectedRoute() is not { } profile) return;
        using var window = new Form
        {
            Text = T("Rename profile", "Renomear perfil"), ClientSize = new Size(455, 131), FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, BackColor = Surface,
            ForeColor = DesktopTheme.Text, Font = Body(), Padding = new Padding(15, 10, 15, 9)
        };
        using var layout = Column(23, 34, 0);
        layout.Controls.Add(MakeLabel(T("Profile name", "Nome do perfil"), muted: true), 0, 0);
        using var name = MakeText();
        name.AccessibleName = T("Profile name", "Nome do perfil");
        name.Text = profile.Name;
        name.MaxLength = 100;
        using var frame = InputFrame(name);
        layout.Controls.Add(frame, 0, 1);
        using var confirm = MakeButton(T("Save", "Salvar"), ButtonKind.Primary);
        confirm.DialogResult = DialogResult.OK;
        using var bottom = MakeBar(confirm);
        bottom.Padding = new Padding(0, 5, 0, 0);
        layout.Controls.Add(bottom, 0, 2);
        window.Controls.Add(layout);
        window.AcceptButton = confirm;
        window.Shown += (_, _) => { name.Focus(); name.SelectAll(); };
        if (window.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(name.Text)) return;
        settings.Profiles[settings.Profiles.FindIndex(x => x.Id == profile.Id)] = profile with { Name = name.Text.Trim() };
        DrawRoutes();
        Changed();
    }

    private void RemoveRoute()
    {
        if (busy || pingBusy || SelectedRoute() is not { } profile) return;
        Attempt(() =>
        {
            var index = settings.Profiles.FindIndex(x => x.Id == profile.Id);
            settings.Profiles.RemoveAt(index);
            settings.SelectedProfile = settings.Profiles.FirstOrDefault()?.Id ?? Guid.Empty;
            try { Save(); }
            catch
            {
                settings.Profiles.Insert(index, profile);
                settings.SelectedProfile = profile.Id;
                throw;
            }
            DrawRoutes();
            Changed(save: false);
            store.Remove(profile.Id);
        });
    }

    private void DrawRoutes()
    {
        rendering = true;
        routes.SuspendLayout();
        try
        {
            routes.Rows.Clear();
            foreach (var profile in settings.Profiles)
            {
                var latency = latencies.TryGetValue(profile.Id, out var ms)
                    ? ms is { } value ? $"{value} ms" : T("No response", "Sem resposta") : "—";
                var index = routes.Rows.Add(profile.Name, profile.Endpoint, profile.HasIpv6 ? T("Available", "Disponível") : "—",
                    latency, "");
                var row = routes.Rows[index];
                row.Tag = profile.Id;
                row.Cells[0].ToolTipText = profile.Name;
                row.Cells[1].ToolTipText = profile.Endpoint;
                row.Cells[3].ToolTipText = T("Ping to the endpoint over the current connection", "Ping até o endpoint pela conexão atual");
            }
            routes.ClearSelection();
            foreach (DataGridViewRow row in routes.Rows)
            {
                if (row.Tag is not Guid id || id != settings.SelectedProfile) continue;
                routes.CurrentCell = row.Cells[0];
                row.Selected = true;
                break;
            }
            emptyRoutes.Visible = routes.Rows.Count == 0;
        }
        finally
        {
            routes.ResumeLayout();
            rendering = false;
        }
        DrawRouteStatuses(activeSession?.Profile.Id, settings.SelectedProfile);
    }

    private void DrawRouteStatuses(Guid? live, Guid selected)
    {
        foreach (DataGridViewRow row in routes.Rows)
        {
            var id = (Guid)row.Tag!;
            var cell = row.Cells[4];
            var color = id == live ? Green : id == selected ? Accent : Muted;
            cell.Style.ForeColor = color;
            cell.Style.SelectionForeColor = color;
            cell.Style.Font = Body(9, bold: true);
            cell.Value = id == live ? T("●  In use", "●  Em uso") : id == selected ? T("Selected", "Selecionado") : "—";
        }
    }

    private async Task MeasureLatency(bool chooseFastest)
    {
        var profiles = chooseFastest ? settings.Profiles.ToArray() :
            SelectedRoute() is { } selected ? [selected] : [];
        if (pingBusy || profiles.Length == 0) return;
        pingBusy = true;
        UpdateState();
        footer.Text = T("Measuring endpoint latency over the current Windows connection…", "Medindo ICMP para os endpoints pela conexão do Windows…");
        try
        {
            using var limiter = new SemaphoreSlim(6);
            var results = await Task.WhenAll(profiles.Select(async profile =>
            {
                await limiter.WaitAsync();
                try
                {
                    var host = store.Read(profile.Id).Host;
                    using var client = new Ping();
                    var data = new byte[24];
                    var times = new List<long>(2);
                    for (var attempt = 0; attempt < 2; attempt++)
                    {
                        var reply = await client.SendPingAsync(host, TimeSpan.FromSeconds(2), data, options: null);
                        if (reply.Status == IPStatus.Success) times.Add(reply.RoundtripTime);
                    }
                    return (profile, latency: times.Count > 0 ? (long?)Math.Round(times.Average()) : null);
                }
                catch (Exception e) when (e is PingException or SocketException or InvalidOperationException or
                    IOException or FormatException or Win32Exception or ArgumentException)
                {
                    return (profile, latency: (long?)null);
                }
                finally { limiter.Release(); }
            }));
            foreach (var result in results) latencies[result.profile.Id] = result.latency;
            var best = results.Where(x => x.latency.HasValue).OrderBy(x => x.latency).FirstOrDefault();
            if (chooseFastest && best.profile is not null)
            {
                settings.SelectedProfile = best.profile.Id;
                Changed();
            }
            DrawRoutes();
            footer.Text = best.profile is null ? T("The endpoints did not respond to ICMP. You can still try connecting.", "Os endpoints não responderam a ICMP. Você ainda pode tentar conectar.")
                : T($"{best.profile.Name} · {best.latency} ms to the server. Connect or apply settings to use this route.", $"{best.profile.Name} · {best.latency} ms até o servidor. A seleção só entra em vigor ao conectar ou aplicar.");
        }
        finally
        {
            pingBusy = false;
            UpdateState();
        }
    }

    private void ChooseEngine()
    {
        if (busy || activeSession is not null) return;
        using var picker = new OpenFileDialog
        {
            Filter = "WireSock (wiresock-client.exe)|wiresock-client.exe",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        Attempt(() =>
        {
            settings.EnginePath = WireSockEngine.ValidateInstallation(picker.FileName);
            Changed();
            UpdateEngineInfo();
        });
    }

    private void UpdateEngineInfo()
    {
        var located = settings.EnginePath.Length > 0 && File.Exists(settings.EnginePath)
            ? settings.EnginePath : WireSockEngine.Locate();
        engineInfo.Text = located is null ? T("WireSock CLI missing. Copy the installation command to install it.", "WireSock CLI pendente · copie o comando de instalação.")
            : $"WireSock CLI · {FileVersionInfo.GetVersionInfo(located).FileVersion}{Environment.NewLine}{located}";
    }

    private async Task ChangeConnection(bool starting)
    {
        if (busy || updateBusy || (starting && pingBusy)) return;
        operation = !starting ? T("Disconnecting…", "Desconectando…") :
            activeSession is not null ? T("Applying changes…", "Aplicando ajustes…") : T("Connecting…", "Conectando…");
        busy = true;
        UpdateState();
        try
        {
            if (!starting)
            {
                await Task.Run(engine.Stop);
                activeSession = null;
                AddLog(T("VPN disconnected. Apps return to their normal connection.", "VPN desligada. Os programas voltam à conexão normal."));
                footer.Text = T("VPN disconnected. Reopen the call or client to establish new connections.", "VPN desligada. Reabra a chamada ou o cliente para renovar as conexões.");
                return;
            }

            var route = SelectedRoute() ?? throw new InvalidOperationException(T("Add and select a profile in Routes.", "Importe e selecione um perfil na aba Rotas."));
            var requested = settings.Applications.Where(x => x.Enabled).ToArray();
            var routed = requested.Where(x => x.Folder ? Directory.Exists(x.Match) : File.Exists(x.Match)).ToArray();
            var missing = requested.Length - routed.Length;
            if (routed.Length == 0)
                throw new InvalidOperationException(T("Select at least one existing app or folder in Apps.", "Selecione ao menos um programa ou uma pasta que exista na aba Programas."));
            var path = settings.EnginePath.Length > 0 && File.Exists(settings.EnginePath) ? settings.EnginePath :
                WireSockEngine.Locate() ?? throw new InvalidOperationException(T("Install WireSock Secure Connect CLI using the command in Diagnostics.", "Instale o WireSock Secure Connect CLI. O comando está na aba Diagnóstico."));
            var options = CurrentRoute();
            var verbose = diagnostics.Checked;
            var profile = store.Read(route.Id);
            var config = profile.BuildClientConfig(routed.Select(x => x.Match), options);
            Save();
            await Task.Run(() => WireSockEngine.ValidateInstallation(path));
            AddLog(T($"Starting {route.Name} at {profile.Endpoint} with {routed.Length} app rules.", $"Iniciando {route.Name} por {profile.Endpoint} para {routed.Length} regra(s) de programa."));
            if (missing > 0) AddLog(T($"Skipped {missing} selected paths that no longer exist.", $"{missing} caminho(s) marcado(s) deixaram de existir e foram ignorados."));
            var request = new TunnelSession(route with { Endpoint = profile.Endpoint }, routed.Length,
                requested.Select(x => x.Match).ToArray(), options, verbose);
            activeSession = await Task.Run(() => engine.Start(path, config, request));
            AddLog(T("The WireSock VPN service is running.", "O serviço VPN do WireSock está em execução."));
            footer.Text = T("VPN route applied.", "Rota VPN aplicada.");
            operation = "";
            UpdateState();
            await OfferDiscordRestart(routed);
        }
        catch (Exception e) when (Expected(e))
        {
            Attempt(() => activeSession = engine.GetActive());
            Failure(e);
        }
        finally
        {
            busy = false;
            UpdateState();
        }
    }

    private async Task OfferDiscordRestart(ApplicationTarget[] routed)
    {
        var clients = await Task.Run(() => DiscordRestart.FindRunning(routed));
        if (clients.Length == 0) return;
        var names = string.Join(", ", clients.Select(client => client.Name).Distinct());
        var message = T($"Restart {names} to use the VPN? This will end any active calls or streams.",
            $"Reiniciar {names} para usar a VPN? Isso interrompe chamadas e transmissões em andamento.");
        if (MessageBox.Show(this, message, "AppTunnel", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            footer.Text = T("VPN route applied. Restart Discord when you're ready.",
                "Rota VPN aplicada. Reinicie o Discord quando puder.");
            return;
        }

        operation = T("Restarting Discord…", "Reiniciando o Discord…");
        UpdateState();
        var failed = new List<string>();
        foreach (var client in clients)
        {
            try
            {
                await DiscordRestart.CloseAsync(client);
                ExplorerLauncher.Open(client.Path);
            }
            catch (Exception error) when (Expected(error))
            {
                failed.Add(client.Name);
                AddLog($"{client.Name} · {error.Message}");
            }
        }
        if (failed.Count == 0) return;
        var retry = string.Join(", ", failed.Distinct());
        Failure(new InvalidOperationException(T($"Restart these clients manually: {retry}",
            $"Reinicie estes clientes manualmente: {retry}")));
    }

    private RouteOptions CurrentRoute() => new(lan.Checked, ipv6.Checked, include.Text.Trim(), exclude.Text.Trim());

    private void Changed(bool save = true)
    {
        if (initializing) return;
        if (save) Attempt(Save);
        UpdateState();
    }

    private void Save()
    {
        settings.Route = CurrentRoute();
        settings.DiagnosticLogs = diagnostics.Checked;
        store.Save(settings);
    }

    private void UpdateState()
    {
        var session = activeSession;
        var running = session is not null;
        var pick = SelectedRoute();
        var marked = settings.Applications.Where(x => x.Enabled).Select(x => x.Match).ToArray();
        var changedServer = session is not null && pick is not null && session.Profile.Id != pick.Id;
        var pending = session is not null && (changedServer || !session.RequestedPrograms.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(marked) ||
            session.Options != CurrentRoute() || session.Diagnostics != diagnostics.Checked);
        var canApply = pending && pick is not null && marked.Length > 0;

        start.Text = pick is null ? T("Add a server…", "Adicionar servidor…") : marked.Length == 0 ? T("Choose apps", "Escolher programas") : T("Connect", "Conectar");
        start.Visible = !running;
        start.Enabled = !HasPendingOperation;
        apply.Text = changedServer ? T("Switch server", "Trocar servidor") : T("Apply settings", "Aplicar ajustes");
        apply.Visible = running && canApply;
        apply.Enabled = !HasPendingOperation;
        stop.Visible = running;
        stop.Enabled = !busy && !updateBusy;
        pageHost.Enabled = !busy;
        foreach (var button in navigationButtons) button.Enabled = !busy;
        ping.Enabled = !busy && !pingBusy && pick is not null;
        fastest.Enabled = !busy && !pingBusy && settings.Profiles.Count > 1;
        rename.Enabled = !busy && !pingBusy && pick is not null;
        remove.Enabled = !busy && !pingBusy && pick is not null;
        ipv6.Enabled = pick?.HasIpv6 == true;

        if (busy && operation.Length > 0)
            pill.ShowStatus(operation, Amber, AmberWash);
        else if (running)
            pill.ShowStatus(T("WireSock VPN service is running", "Serviço VPN do WireSock em execução"), Green, GreenWash);
        else
            pill.ShowStatus(T("Offline", "Sem conexão"), Muted, Surface);

        if (session is not null)
        {
            var live = settings.Profiles.Find(x => x.Id == session.Profile.Id) ?? session.Profile;
            liveTitle.Text = live.Name;
            liveDetails.Text = session.Profile.Endpoint;
            liveDetails.Font = Mono;
        }
        else
        {
            liveTitle.Text = busy ? T("Preparing session", "Preparando sessão") : T("VPN disconnected", "VPN desligada");
            liveDetails.Text = busy ? T("Updating the VPN connection.", "Aguarde a atualização da conexão.") : T("No server in use.", "Nenhum servidor em uso.");
            liveDetails.Font = Body();
        }

        routeCaption.ForeColor = pending ? Amber : Muted;
        routeDetails.Font = Body(9);
        if (pick is null)
        {
            routeCaption.Text = T("Selected route", "Rota selecionada");
            routeTitle.Text = T("No profile selected", "Nenhum perfil escolhido");
            routeDetails.Text = T("Sign in to Proton or import a .conf file.", "Importe um arquivo .conf da Proton.");
        }
        else if (session is null || changedServer)
        {
            routeCaption.Text = changedServer ? T("Next route", "Próxima rota") : T("Selected route", "Rota selecionada");
            routeTitle.Text = pick.Name;
            routeDetails.Text = pick.Endpoint;
            routeDetails.Font = Mono;
        }
        else if (pending)
        {
            routeCaption.Text = T("Pending changes", "Ajustes pendentes");
            routeTitle.Text = FormatPrograms(marked.Length, T("selected", "marcado"), T("selected", "marcados"));
            routeDetails.Text = canApply ? T("Apply changes to update the session.", "Aplique para atualizar a sessão.") : T("Select an app to apply changes.", "Marque um programa para aplicar.");
        }
        else
        {
            routeCaption.Text = T("Current session", "Sessão atual");
            routeTitle.Text = FormatPrograms(session.ProgramCount, T("on the VPN", "pela VPN"), T("on the VPN", "pela VPN"));
            routeDetails.Text = "";
        }
        appCount.Text = FormatPrograms(marked.Length, T("selected for the VPN", "marcado para usar a VPN"), T("selected for the VPN", "marcados para usar a VPN"));
        DrawRouteStatuses(session?.Profile.Id, settings.SelectedProfile);
    }

    private static string FormatPrograms(int count, string singular, string plural) =>
        count == 1 ? T($"1 app {singular}", $"1 programa {singular}") : T($"{count} apps {plural}", $"{count} programas {plural}");

    private void AddLog(string line)
    {
        queuedLogs.Enqueue($"[{DateTime.Now:HH:mm:ss}] {line}");
        while (queuedLogs.Count > 220) queuedLogs.TryDequeue(out _);
    }

    private void DrainLogs()
    {
        if (queuedLogs.IsEmpty) return;
        while (queuedLogs.TryDequeue(out var line)) history.Enqueue(line);
        while (history.Count > 220) history.Dequeue();
        log.Lines = history.ToArray();
        log.SelectionStart = log.TextLength;
        log.ScrollToCaret();
    }

    private void ClearLog()
    {
        queuedLogs.Clear();
        history.Clear();
        log.Clear();
    }

    public Task Disconnect() => ChangeConnection(starting: false);

    public void ShowPage(int index) => OpenPage(index);

    public void SaveInterface() => Attempt(Save);

    public bool TrySaveLanguage(string language)
    {
        if (settings.Language == language) return false;
        var previous = settings.Language;
        try
        {
            settings.Language = language;
            Save();
            return true;
        }
        catch (Exception error) when (Expected(error))
        {
            settings.Language = previous;
            Failure(error);
            return false;
        }
    }

    private void OpenProton(bool create = false)
    {
        if (HasPendingOperation) return;
        Attempt(() =>
        {
            proton.Open(create);
            footer.Text = create
                ? T("Choose a server in Proton. New profiles are saved automatically.",
                    "Escolha um servidor na Proton. Os novos perfis são salvos automaticamente.")
                : T("Your Proton account profiles will appear here after you sign in.",
                    "Os perfis da sua conta Proton aparecerão aqui após o login.");
        });
    }

    private void RenderProtonAccount()
    {
        var label = proton.Catalog is { Ready: true } catalog
            ? T($"Proton account · {catalog.Profiles.Length} profiles synced",
                $"Conta Proton · {catalog.Profiles.Length} perfis sincronizados")
            : T("Sign in to sync your Proton WireGuard profiles.",
                "Entre para sincronizar os perfis WireGuard da Proton.");
        if (protonAccount.Text != label) protonAccount.Text = label;
    }

    private void ShowProtonProfiles()
    {
        if (proton.Catalog is not { Ready: true } account)
        {
            OpenProton();
            return;
        }

        using var window = new Form
        {
            Text = T("Proton WireGuard profiles", "Perfis WireGuard da Proton"),
            ClientSize = new Size(760, 432), MinimumSize = new Size(560, 320),
            StartPosition = FormStartPosition.CenterParent, BackColor = Background,
            ForeColor = DesktopTheme.Text, Font = Body(), Padding = new Padding(16)
        };
        using var layout = Column(44, 0, 48);
        layout.Controls.Add(SectionTitle(T("Proton WireGuard profiles", "Perfis WireGuard da Proton")), 0, 0);
        using var list = MakeGrid();
        AddColumn(list, T("Account profile", "Perfil da conta"), 175, fill: true, font: Body(bold: true));
        AddColumn(list, T("Server", "Servidor"), 145, font: Mono);
        AddColumn(list, T("Availability", "Disponibilidade"), 225);
        foreach (var item in account.Profiles)
        {
            var state = item.State switch
            {
                ProtonProfileState.KeyRequired => T("Original .conf required", "Requer o .conf original"),
                ProtonProfileState.Available => T("Compatible", "Compatível"),
                _ => T("Unsupported", "Incompatível")
            };
            if (item.Fingerprint is not null && settings.Profiles.Any(profile => profile.Fingerprint == item.Fingerprint))
                state = T("Saved on this PC", "Salvo neste PC");
            list.Rows.Add(item.Name, item.Server ?? "—", state);
        }
        list.ClearSelection();
        using var empty = MakeLabel("", muted: true);
        empty.Visible = list.Rows.Count == 0;
        using var frame = GridFrame(list, empty, T("Your Proton account has no WireGuard profiles.",
            "Sua conta Proton ainda não tem perfis WireGuard."));
        layout.Controls.Add(frame, 0, 1);
        var create = ActionButton(T("New WireGuard", "Novo WireGuard"), () => { window.Close(); OpenProton(create: true); }, ButtonKind.Primary);
        var import = ActionButton(T("Import .conf", "Importar .conf"), () => { window.Close(); ImportProfiles(); });
        layout.Controls.Add(MakeBar(create, import), 0, 2);
        window.Controls.Add(layout);
        window.ShowDialog(this);
    }

    private void Poll()
    {
        DrainLogs();
        if (++pollTicks % 2 != 0 || HasPendingOperation) return;
        try
        {
            var session = engine.GetActive();
            if (activeSession?.ConfigPath != session?.ConfigPath)
            {
                if (activeSession is not null && session is null)
                    footer.Text = T("The Windows service stopped.", "O serviço do Windows parou.");
                activeSession = session;
                UpdateState();
            }
            var note = proton.Poll(config =>
            {
                var profile = store.ImportProton(config, settings.Profiles);
                if (profile is null) return;
                var selected = settings.SelectedProfile;
                settings.Profiles.Add(profile);
                if (SelectedRoute() is null) settings.SelectedProfile = profile.Id;
                try { Save(); }
                catch
                {
                    settings.Profiles.Remove(profile);
                    settings.SelectedProfile = selected;
                    store.Remove(profile.Id);
                    throw;
                }
                DrawRoutes();
                UpdateState();
                footer.Text = T($"Saved {profile.Name} from Proton VPN.", $"Perfil {profile.Name} salvo pela Proton VPN.");
            });
            RenderProtonAccount();
            if (note is not null) footer.Text = note;
            monitorFailed = false;
        }
        catch (Exception error) when (Expected(error))
        {
            if (!monitorFailed) AddLog(error.Message);
            monitorFailed = true;
            footer.Text = error.Message;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            monitor.Dispose();
            foreach (var page in pages)
                if (page.Parent is null) page.Dispose();
        }
        base.Dispose(disposing);
    }

    private static bool Expected(Exception e) => e is IOException or UnauthorizedAccessException or Win32Exception or
        FormatException or InvalidOperationException or ArgumentException or NotSupportedException or System.Security.SecurityException or
        System.TimeoutException or System.ServiceProcess.TimeoutException or System.Text.Json.JsonException;

    private void Attempt(Action action)
    {
        try { action(); }
        catch (Exception e) when (Expected(e)) { Failure(e); }
    }

    private void Failure(Exception e)
    {
        AddLog(e.Message);
        footer.Text = e.Message;
        MessageBox.Show(this, e.Message, "AppTunnel", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void Copy(string value)
    {
        if (value.Length == 0) return;
        Attempt(() =>
        {
            Clipboard.SetText(value);
            footer.Text = T("Copied.", "Copiado.");
        });
    }
}
