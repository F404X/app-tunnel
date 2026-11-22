using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
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
    private readonly TunnelEngines engine;
    private readonly ProtonLogin proton;
    private readonly Telemetry telemetry;
    private readonly ConcurrentQueue<string> queuedLogs = new();
    private readonly Queue<string> history = new();
    private readonly System.Windows.Forms.Timer monitor = new() { Interval = 450 };
    // Waits for a short pause after the last change before restarting the session, so several clicks apply once.
    private readonly System.Windows.Forms.Timer autoApply = new() { Interval = 1500 };
    private bool autoApplyFailed;
    private TunnelSession? activeSession;
    private string operation = "";
    private bool busy;
    private bool pingBusy;
    private bool initializing = true;
    private int pollTicks;
    private bool monitorFailed;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CurrentPage { get; private set; }
    public bool HasPendingOperation => busy || pingBusy || updateBusy;
    public bool CanDisconnect => !busy && !updateBusy && activeSession is not null;
    public event Action<string>? LanguageRequested;

    private readonly StatusPill pill = new();
    private readonly Label protonAccount = MakeLabel("", muted: true);
    private readonly Label appCount = MakeLabel("", muted: true);
    private readonly Label footer = MakeLabel("", muted: true);
    private readonly Label engineInfo = MakeLabel("", muted: true);
    private readonly ThemeButton apply = MakeButton(T("Apply settings", "Aplicar ajustes", "Применить"), ButtonKind.Primary);
    private readonly ThemeButton fastest = MakeButton(T("Lowest ping", "Menor ping", "Лучший пинг"));
    private readonly ThemeButton rename = MakeButton(T("Rename", "Renomear", "Переименовать"), ButtonKind.Quiet);
    private readonly ThemeButton remove = MakeButton(T("Remove", "Remover", "Удалить"), ButtonKind.Quiet);
    private readonly CheckBox lan = MakeCheck(T("Keep local traffic on the direct connection", "Manter a rede local pela conexão direta", "Локальная сеть напрямую"));
    private readonly CheckBox ipv6 = MakeCheck(T("Include IPv6 when available", "Incluir IPv6 quando disponível", "Использовать IPv6 при наличии"));
    private readonly CheckBox diagnostics = MakeCheck(T("Enable detailed WireSock logs", "Detalhar registros do WireSock", "Подробные журналы WireSock"));
    private readonly TextBox include = MakeText(placeholder: T("All destinations in the profile", "Todos os destinos do perfil", "Все адреса из профиля"));
    private readonly TextBox exclude = MakeText(placeholder: T("IP addresses or CIDR ranges, comma-separated", "IPs ou redes CIDR, separados por vírgulas", "IP-адреса или сети CIDR через запятую"));
    private readonly TextBox log = MakeText(multiline: true);
    private readonly Panel pageHost = new() { Dock = DockStyle.Fill, Margin = new Padding(0) };
    private readonly List<Control> pages = [];
    private ThemeButton[] navigationButtons = [];
    private readonly Dictionary<Guid, long?> latencies = new();

    public MainForm(ProfileStore store, ProtonLogin proton, Telemetry telemetry)
    {
        this.store = store;
        this.proton = proton;
        this.telemetry = telemetry;
        settings = store.Load();
        engine = new TunnelEngines(AddLog, new WireSockEngine());
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
        ThemeScrollbars(this);
        lan.Checked = settings.Route.DirectLan;
        ipv6.Checked = settings.Route.UseIpv6;
        include.Text = settings.Route.Include;
        exclude.Text = settings.Route.Exclude;
        diagnostics.Checked = settings.DiagnosticLogs;
        tunnelDns.Checked = settings.Route.TunnelDns;
        guardIpv6.Checked = settings.Route.BlockPublicIpv6;
        protocolMode.SelectedIndex = Enum.IsDefined(settings.Protocol) ? (int)settings.Protocol : 0;
        SetAdvancedExpanded(include.Text.Trim().Length > 0 || exclude.Text.Trim().Length > 0);
        DrawApps();
        DrawRoutes();
        UpdateEngineInfo();
        initializing = false;
        UpdateState();

        monitor.Tick += (_, _) => Poll();
        monitor.Start();
        autoApply.Tick += async (_, _) => await RunAutoApply();
        Shown += (_, _) =>
        {
            UpdateState();
            _ = RefreshLatencyQuietly();
            if (!settings.SetupDone && settings.Profiles.Count == 0) BeginInvoke(new Action(ShowOnboarding));
        };
    }

    private void WireActions()
    {
        power.Click += async (_, _) =>
        {
            if (activeSession is not null)
            {
                await ChangeConnection(starting: false);
                return;
            }
            if (settings.AutoServer && SelectFastestRoute()) DrawRouteStatuses(null, settings.SelectedProfile);
            if (SelectedRoute() is null)
            {
                OpenPage(ServersPage);
                return;
            }
            if (!settings.Applications.Any(x => x.Enabled))
            {
                OpenPage(AppsPage);
                return;
            }
            await ChangeConnection(starting: true);
        };
        apply.Click += async (_, _) => await ChangeConnection(starting: true);
        switchServer.Click += async (_, _) => await ChangeConnection(starting: true);
        serverAction.Click += (_, _) => OpenPage(ServersPage);
        healthAction.Click += (_, _) => TestConnection();
        fastest.Click += async (_, _) => await MeasureLatency(true);
        rename.Click += (_, _) => RenameRoute();
        remove.Click += (_, _) => RemoveRoute();
        lan.CheckedChanged += (_, _) => Changed();
        ipv6.CheckedChanged += (_, _) => Changed();
        // Typing an address range applies when the field loses focus, never halfway through the value.
        include.TextChanged += (_, _) => Changed(save: false, apply: false);
        exclude.TextChanged += (_, _) => Changed(save: false, apply: false);
        include.Leave += (_, _) => ApplyEditedField(include);
        exclude.Leave += (_, _) => ApplyEditedField(exclude);
        diagnostics.CheckedChanged += (_, _) => Changed();
        tunnelDns.CheckedChanged += (_, _) => Changed();
        guardIpv6.CheckedChanged += (_, _) => Changed();
        protocolMode.SelectedIndexChanged += (_, _) => Changed();
    }

    private void AddExecutable(IWin32Window? owner = null)
    {
        using var picker = new OpenFileDialog { Filter = T("Windows executables (*.exe)|*.exe", "Executáveis do Windows (*.exe)|*.exe", "Исполняемые файлы Windows (*.exe)|*.exe"), Multiselect = true };
        if (picker.ShowDialog(owner ?? this) != DialogResult.OK) return;
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
            Description = T("Choose the app's installation folder. This includes all executables in the folder.", "Escolha a pasta de instalação do programa. Todos os executáveis nela entram na seleção.", "Выберите папку установки приложения. Правило охватит все исполняемые файлы в ней."),
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
                Text = T("Running apps", "Programas em execução", "Запущенные приложения"),
                ClientSize = new Size(790, 505), MinimumSize = new Size(620, 350),
                StartPosition = FormStartPosition.CenterParent, BackColor = Background,
                ForeColor = DesktopTheme.Text, Font = Body(), Padding = new Padding(17, 12, 17, 10)
            };
            PrepareDialog(window);
            using var layout = Column(33, 0, 44);
            layout.Controls.Add(SectionTitle(T("Select apps", "Selecione os programas", "Выберите приложения")), 0, 0);
            using var list = MakeGrid();
            AddCheckColumn(list);
            list.Columns[0].HeaderText = T("Include", "Incluir", "Включить");
            AddColumn(list, T("App", "Programa", "Приложение"), 205, font: Body(bold: true));
            AddColumn(list, T("Executable", "Executável", "Исполняемый файл"), 280, fill: true, font: Mono);
            foreach (var target in running)
                list.Rows[list.Rows.Add(false, target.Label, target.Match)].Tag = target;
            list.ClearSelection();
            using var empty = MakeLabel("", muted: true);
            empty.Visible = list.Rows.Count == 0;
            using var frame = GridFrame(list, empty, T("No running apps found.", "Nenhum programa em execução foi encontrado.", "Запущенные приложения не найдены."));
            layout.Controls.Add(frame, 0, 1);

            using var confirm = MakeButton(T("Add selected apps", "Adicionar marcados", "Добавить выбранные приложения"), ButtonKind.Primary);
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

    private void ImportProfiles(IWin32Window? owner = null)
    {
        if (busy || pingBusy) return;
        using var picker = new OpenFileDialog { Filter = T("VPN profiles (*.conf)|*.conf", "Perfis VPN (*.conf)|*.conf", "Профили VPN (*.conf)|*.conf"), Multiselect = true };
        if (picker.ShowDialog(owner ?? this) != DialogResult.OK) return;
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
        footer.Text = T($"Imported {added} profiles. Rename a profile to make it easier to identify.", $"{added} perfil(is) importado(s). Dê um nome à rota para identificá-la.", $"Импортировано профилей: {added}. Для удобства можно переименовать маршрут.");
    }

    private StoredProfile? SelectedRoute() => settings.Profiles.Find(profile => profile.Id == settings.SelectedProfile);

    private void RenameRoute()
    {
        if (SelectedRoute() is not { } profile) return;
        using var window = new Form
        {
            Text = T("Rename profile", "Renomear perfil", "Переименовать профиль"), ClientSize = new Size(455, 131), FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, BackColor = Surface,
            ForeColor = DesktopTheme.Text, Font = Body(), Padding = new Padding(15, 10, 15, 9)
        };
        PrepareDialog(window);
        using var layout = Column(23, 34, 0);
        layout.Controls.Add(MakeLabel(T("Profile name", "Nome do perfil", "Имя профиля"), muted: true), 0, 0);
        using var name = MakeText();
        name.AccessibleName = T("Profile name", "Nome do perfil", "Имя профиля");
        name.Text = profile.Name;
        name.MaxLength = 100;
        using var frame = InputFrame(name);
        layout.Controls.Add(frame, 0, 1);
        using var confirm = MakeButton(T("Save", "Salvar", "Сохранить"), ButtonKind.Primary);
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

    private async Task MeasureLatency(bool chooseFastest)
    {
        var profiles = chooseFastest ? settings.Profiles.ToArray() :
            SelectedRoute() is { } selected ? [selected] : [];
        if (pingBusy || profiles.Length == 0) return;
        pingBusy = true;
        UpdateState();
        footer.Text = T("Measuring endpoint latency over the current Windows connection…", "Medindo ICMP para os endpoints pela conexão do Windows…", "Измерение задержки до серверов через текущее подключение Windows…");
        try
        {
            var results = await PingRoutesAsync(profiles, minimum: 1, latencyLifetime.Token);
            if (IsDisposed || Disposing) return;
            foreach (var result in results) latencies[result.profile.Id] = result.latency;
            var best = results.Where(x => x.latency.HasValue).OrderBy(x => x.latency).FirstOrDefault();
            if (chooseFastest && best.profile is not null)
            {
                settings.SelectedProfile = best.profile.Id;
                Changed();
            }
            DrawRoutes();
            footer.Text = best.profile is null ? T("The endpoints did not respond to ICMP. You can still try connecting.", "Os endpoints não responderam a ICMP. Você ainda pode tentar conectar.", "Серверы не ответили на ICMP. Можно попробовать подключиться.")
                : T($"{best.profile.Name} · {best.latency} ms to the server. Connect or apply settings to use this route.", $"{best.profile.Name} · {best.latency} ms até o servidor. A seleção só entra em vigor ao conectar ou aplicar.", $"{best.profile.Name} · {best.latency} мс до сервера. Подключитесь или примените настройки, чтобы использовать маршрут.");
        }
        catch (OperationCanceledException) when (latencyLifetime.IsCancellationRequested) { }
        finally
        {
            pingBusy = false;
            if (!IsDisposed && !Disposing) UpdateState();
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
        engineInfo.Text = located is null ? T("WireSock CLI missing. Copy the installation command to install it.", "WireSock CLI pendente · copie o comando de instalação.", "WireSock CLI не найден. Скопируйте команду для его установки.")
            : $"WireSock CLI · {FileVersionInfo.GetVersionInfo(located).FileVersion}{Environment.NewLine}{located}";
    }

    private async Task ChangeConnection(bool starting)
    {
        if (busy || updateBusy || (starting && pingBusy)) return;
        operation = !starting ? T("Disconnecting…", "Desconectando…", "Отключение…") :
            activeSession is not null ? T("Applying changes…", "Aplicando ajustes…", "Применение настроек…") : T("Connecting…", "Conectando…", "Подключение…");
        busy = true;
        autoApply.Stop();
        var previous = activeSession;
        var attemptStarted = Stopwatch.GetTimestamp();
        VpnProtocol? attemptProtocol = null;
        UpdateState();
        try
        {
            if (!starting)
            {
                await Task.Run(engine.Stop);
                activeSession = null;
                telemetry.RecordEvent(TelemetryEvent.Disconnected);
                healthDetails.Text = "";
                AddLog(T("VPN disconnected. Apps return to their normal connection.", "VPN desligada. Os programas voltam à conexão normal.", "VPN отключён. Приложения используют обычное соединение."));
                footer.Text = T("VPN disconnected. Reopen the call or client to establish new connections.", "VPN desligada. Reabra a chamada ou o cliente para renovar as conexões.", "VPN отключён. Перезапустите приложение или заново подключитесь к звонку.");
                return;
            }

            var route = SelectedRoute() ?? throw new InvalidOperationException(T("Add and select a server in Servers.", "Adicione e selecione um servidor em Servidores.", "Добавьте и выберите сервер в разделе «Серверы»."));
            var requested = settings.Applications.Where(x => x.Enabled).ToArray();
            var routed = requested.Where(x => x.Folder ? Directory.Exists(x.Match) : File.Exists(x.Match)).ToArray();
            var missing = requested.Length - routed.Length;
            if (routed.Length == 0)
                throw new InvalidOperationException(T("Select at least one existing app or folder in Apps.", "Ative ao menos um app ou pasta que exista em Apps.", "Включите хотя бы одно существующее приложение или папку в разделе «Приложения»."));
            var options = CurrentRoute();
            var verbose = diagnostics.Checked;
            var preference = SelectedProtocol;
            var profiles = settings.Profiles.ToArray();
            var preferredPath = settings.EnginePath;
            Save();
            if (missing > 0)
                AddLog(T($"Skipped {missing} selected paths that no longer exist.", $"Ignorando {missing} caminho(s) selecionado(s) que não existem mais.",
                    $"Пропущено недоступных путей: {missing}."));
            var primary = new VpnRoute<StoredProfile>(route, route.Endpoint, route.Protocol);
            var plan = VpnProtocols.Plan(preference, primary,
                profiles.Select(profile => new VpnRoute<StoredProfile>(profile, profile.Endpoint, profile.Protocol)));
            attemptProtocol = plan[0].Protocol;
            activeSession = await Task.Run(() => engine.Connect(plan, profile => store.Read(profile.Id), routed,
                requested.Select(target => target.Match).ToArray(), options, verbose, preferredPath, preference, AddLog));
            telemetry.RecordConnection(activeSession.Protocol, Stopwatch.GetElapsedTime(attemptStarted), success: true);
            telemetry.RecordEvent(TelemetryEvent.Connected);
            attemptProtocol = null;
            recoveryUsed = false;
            ReadActiveDiagnostics(activeSession);
            AddLog(T("The WireSock VPN service is running.", "O serviço VPN do WireSock está em execução.", "Служба WireSock VPN запущена."));
            footer.Text = T("VPN route applied.", "Rota VPN aplicada.", "Маршрут VPN применён.");
            operation = "";
            autoApplyFailed = false;
            UpdateState();
            await OfferDiscordRestart(NeedsReconnect(previous, activeSession, routed));
        }
        catch (Exception e) when (Expected(e))
        {
            if (attemptProtocol is { } protocol) telemetry.RecordConnection(protocol, Stopwatch.GetElapsedTime(attemptStarted), success: false);
            if (!starting || attemptProtocol is not null)
                _ = telemetry.ReportErrorAsync(starting ? TelemetryStage.Connect : TelemetryStage.Disconnect, e);
            Attempt(() => activeSession = engine.GetActive());
            Failure(e);
        }
        finally
        {
            busy = false;
            UpdateState();
        }
    }

    // Apps already in the previous session on the same server keep their connections, so only
    // newly routed apps, or every app after a server change, need their open connections recreated.
    private static ApplicationTarget[] NeedsReconnect(TunnelSession? previous, TunnelSession? current, ApplicationTarget[] routed)
    {
        if (previous is null || current is null || previous.Profile.Id != current.Profile.Id ||
            previous.Profile.Endpoint != current.Profile.Endpoint) return routed;
        var before = previous.RequestedPrograms.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return routed.Where(target => !before.Contains(target.Match)).ToArray();
    }

    private async Task OfferDiscordRestart(ApplicationTarget[] routed)
    {
        if (routed.Length == 0 || settings.DiscordRestart == DiscordRestartChoice.Never) return;
        var clients = await Task.Run(() => DiscordRestart.FindRunning(routed));
        if (clients.Length == 0 || IsDisposed || Disposing) return;
        var names = string.Join(", ", clients.Select(client => client.Name).Distinct());
        if (!ConfirmDiscordRestart(names))
        {
            footer.Text = T("VPN route applied. Restart Discord when you're ready.",
                "Rota VPN aplicada. Reinicie o Discord quando puder.", "Маршрут VPN применён. Перезапустите Discord, когда будете готовы.");
            return;
        }

        operation = T("Restarting Discord…", "Reiniciando o Discord…", "Перезапуск Discord…");
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
            $"Reinicie estes clientes manualmente: {retry}", $"Перезапустите эти приложения вручную: {retry}")));
    }

    private RouteOptions CurrentRoute() => new(lan.Checked, ipv6.Checked, include.Text.Trim(), exclude.Text.Trim(),
        tunnelDns.Checked, guardIpv6.Checked);

    private void Changed(bool save = true, bool apply = true)
    {
        if (initializing) return;
        if (save) Attempt(Save);
        if (apply) ScheduleAutoApply();
        UpdateState();
    }

    private void ApplyEditedField(TextBox field)
    {
        if (!field.Modified) return;
        field.Modified = false;
        Changed();
    }

    private void ScheduleAutoApply()
    {
        if (initializing || !settings.AutoApply || activeSession is null) return;
        autoApplyFailed = false;
        autoApply.Stop();
        autoApply.Start();
    }

    // A different server stays an explicit choice: selecting a row to rename or remove it must not switch the tunnel.
    private async Task RunAutoApply()
    {
        autoApply.Stop();
        if (!settings.AutoApply || activeSession is null || IsDisposed || Disposing) return;
        if (HasPendingOperation)
        {
            autoApply.Start();
            return;
        }
        var changes = PendingChanges();
        if (!changes.CanApply || changes.ChangedServer) return;
        await ChangeConnection(starting: true);
        if (IsDisposed || Disposing) return;
        autoApplyFailed = !autoApply.Enabled && PendingChanges().CanApply;
        UpdateState();
    }

    private (bool Pending, bool ChangedServer, bool CanApply) PendingChanges()
    {
        var session = activeSession;
        var pick = SelectedRoute();
        var marked = settings.Applications.Where(x => x.Enabled).Select(x => x.Match).ToArray();
        var requestedProfile = session is { RequestedProfile: var id } && id != Guid.Empty ? id : session?.Profile.Id;
        var changedServer = session is not null && pick is not null && requestedProfile != pick.Id;
        var pending = session is not null && (changedServer || !session.RequestedPrograms.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(marked) ||
            session.Options != CurrentRoute() || session.Diagnostics != diagnostics.Checked ||
            session.RequestedProtocol != SelectedProtocol);
        return (pending, changedServer, pending && pick is not null && marked.Length > 0);
    }

    private void Save()
    {
        settings.Route = CurrentRoute();
        settings.Protocol = SelectedProtocol;
        settings.DiagnosticLogs = diagnostics.Checked;
        store.Save(settings);
    }

    private void UpdateState()
    {
        var session = activeSession;
        var running = session is not null;
        var pick = SelectedRoute();
        var enabledApps = settings.Applications.Where(x => x.Enabled).ToArray();
        var (pending, changedServer, canApply) = PendingChanges();
        // With automatic apply on, the button only remains for a server switch or to retry a failed apply.
        var applyingSoon = canApply && !changedServer && settings.AutoApply && !autoApplyFailed;

        power.On = running;
        power.Enabled = running ? !busy && !updateBusy : !HasPendingOperation;
        power.AccessibleName = running ? T("Disconnect", "Desconectar", "Отключить") : T("Connect", "Conectar", "Подключить");
        apply.Text = changedServer ? T("Switch server", "Trocar servidor", "Сменить сервер") : T("Apply settings", "Aplicar ajustes", "Применить настройки");
        apply.Visible = running && canApply && !applyingSoon;
        apply.Enabled = !HasPendingOperation;
        switchServer.Visible = running && changedServer && canApply;
        switchServer.Enabled = !HasPendingOperation;
        pageHost.Enabled = !busy;
        foreach (var button in navigationButtons) button.Enabled = !busy;
        fastest.Enabled = !busy && !pingBusy && settings.Profiles.Count > 1;
        rename.Enabled = !busy && !pingBusy && pick is not null;
        remove.Enabled = !busy && !pingBusy && pick is not null;
        ipv6.Enabled = pick?.HasIpv6 == true;

        if (busy && operation.Length > 0)
            pill.ShowStatus(operation, Amber, AmberWash);
        else if (running)
            pill.ShowStatus(T("Connected", "Conectado", "Подключено"), Green, GreenWash);
        else
            pill.ShowStatus(T("Disconnected", "Desconectado", "Не подключено"), Muted, Raised);

        var names = ListNames(enabledApps.Select(x => x.Label));
        var one = enabledApps.Length == 1;
        if (busy && operation.Length > 0)
        {
            heroTitle.Text = operation;
            heroDetail.Text = T("This takes a few seconds.", "Isso leva alguns segundos.", "Это займёт несколько секунд.");
        }
        else if (session is not null)
        {
            heroTitle.Text = T("Protected", "Protegido", "Защищено");
            heroDetail.Text = changedServer && pick is not null
                ? T($"Click Switch server to move to {pick.Name}.", $"Clique em Trocar servidor para passar para {pick.Name}.", $"Нажмите «Сменить сервер», чтобы перейти на {pick.Name}.")
                : applyingSoon ? T("Applying your changes in a moment…", "Aplicando suas mudanças em instantes…", "Изменения скоро будут применены…")
                : pending && canApply ? T("Apply settings to update the session.", "Clique em Aplicar ajustes para atualizar a sessão.", "Примените настройки, чтобы обновить сеанс.")
                : pending ? T("Turn on at least one app to apply the changes.", "Ligue ao menos um app para aplicar as mudanças.", "Включите хотя бы одно приложение, чтобы применить изменения.")
                : one ? T($"{names} is going through the VPN. The rest of the PC stays direct.", $"{names} está passando pela VPN. O resto do PC segue direto.", $"{names} работает через VPN. Остальной трафик идёт напрямую.")
                : T($"{names} are going through the VPN. The rest of the PC stays direct.", $"{names} estão passando pela VPN. O resto do PC segue direto.", $"{names} работают через VPN. Остальной трафик идёт напрямую.");
        }
        else if (pick is null)
        {
            heroTitle.Text = T("Add a server", "Adicione um servidor", "Добавьте сервер");
            heroDetail.Text = T("Sign in to Proton or import a .conf file in Servers.", "Entre na Proton ou importe um arquivo .conf em Servidores.", "Войдите в Proton или импортируйте файл .conf в разделе «Серверы».");
        }
        else if (enabledApps.Length == 0)
        {
            heroTitle.Text = T("Choose your apps", "Escolha os apps", "Выберите приложения");
            heroDetail.Text = T("Pick in Apps which programs use the VPN.", "Escolha em Apps quais programas usam a VPN.", "Выберите в разделе «Приложения», какие программы используют VPN.");
        }
        else
        {
            heroTitle.Text = T("Ready to connect", "Pronto para conectar", "Готово к подключению");
            heroDetail.Text = one
                ? T($"Click the button. {names} will use the VPN; the rest of the PC stays direct.", $"Clique no botão. {names} vai pela VPN; o resto do PC segue direto.", $"Нажмите кнопку. {names} будет работать через VPN, остальное — напрямую.")
                : T($"Click the button. {names} will use the VPN; the rest of the PC stays direct.", $"Clique no botão. {names} vão pela VPN; o resto do PC segue direto.", $"Нажмите кнопку. {names} будут работать через VPN, остальное — напрямую.");
        }

        var live = session is null ? null : settings.Profiles.Find(x => x.Id == session.Profile.Id) ?? session.Profile;
        var shown = live ?? pick;
        serverBadge.Text = shown is null ? "—" : CountryCode(shown.Name);
        serverTitle.Text = settings.AutoServer && shown is not null ? T("Automatic (fastest)", "Automático (mais rápido)", "Автоматически (быстрейший)")
            : shown?.Name ?? T("No server", "Nenhum servidor", "Нет сервера");
        serverDetail.Text = shown is null ? T("Add a Proton server to connect.", "Adicione um servidor da Proton para conectar.", "Добавьте сервер Proton для подключения.")
            : (live is null ? T("Next", "Próximo", "Следующий") : T("Now", "Agora", "Сейчас")) + $": {shown.Name}{LatencySuffix(shown.Id)}" +
                (session is null ? "" : $" · {VpnProtocols.Name(session.Protocol)}");
        serverAction.Text = settings.Profiles.Count == 0 ? T("Add", "Adicionar", "Добавить") : T("Change", "Trocar", "Сменить");

        if (session is null)
        {
            lastHealth = TunnelHealth.Unknown;
            healthDetails.Text = "";
            ShowHealth("—", Raised, Muted, T("VPN off", "VPN desligada", "VPN отключён"),
                T("Connect to check the tunnel.", "Conecte para verificar o túnel.", "Подключитесь, чтобы проверить туннель."));
        }
        else if (lastHealth == TunnelHealth.HandshakeReceived)
            ShowHealth("OK", GreenWash, Green, T("Everything working", "Tudo funcionando", "Всё работает"),
                T("The server answered the handshake.", "O servidor respondeu ao handshake.", "Сервер ответил на рукопожатие."));
        else if (lastHealth is TunnelHealth.HandshakeTimedOut or TunnelHealth.EndpointResolutionFailed or TunnelHealth.RoutingFailed)
            ShowHealth("!", AmberWash, Amber, T("Connection problem", "Problema na conexão", "Проблема с подключением"), healthDetails.Text);
        else
            ShowHealth("…", Raised, Muted, T("Checking…", "Verificando…", "Проверка…"),
                T("Waiting for the server to answer.", "Aguardando resposta do servidor.", "Ожидание ответа сервера."));
        healthAction.Text = session is null ? T("Logs", "Registros", "Журналы") : T("Test", "Testar", "Проверить");

        appCount.Text = FormatPrograms(enabledApps.Length, T("on the VPN", "pela VPN", "через VPN"), T("on the VPN", "pela VPN", "через VPN"));
        DrawRouteStatuses(session?.Profile.Id, settings.SelectedProfile);
    }

    private void ShowHealth(string mark, Color fill, Color ink, string title, string detail)
    {
        healthBadge.Text = mark;
        healthBadge.Fill = fill;
        healthBadge.ForeColor = ink;
        healthTitle.Text = title;
        healthDetail.Text = detail;
    }

    private void TestConnection()
    {
        if (activeSession is not { } session)
        {
            OpenPage(DiagnosticsPage);
            return;
        }
        Attempt(() =>
        {
            var state = ReadActiveDiagnostics(session);
            footer.Text = healthDetails.Text.Length > 0 ? healthDetails.Text
                : state == TunnelHealth.Unknown ? T("No handshake news in the logs yet.", "Ainda não há sinal do handshake nos registros.", "В журналах пока нет данных о рукопожатии.") : "";
        });
        UpdateState();
    }

    private static string ListNames(IEnumerable<string> labels)
    {
        var names = labels.Distinct(StringComparer.CurrentCultureIgnoreCase).ToArray();
        var and = T(" and ", " e ", " и ");
        return names.Length switch
        {
            0 => T("No app", "Nenhum app", "Ни одно приложение"),
            1 => names[0],
            <= 3 => string.Join(", ", names[..^1]) + and + names[^1],
            _ => string.Join(", ", names[..3]) + $" +{names.Length - 3}"
        };
    }

    private static string FormatPrograms(int count, string singular, string plural) =>
        count == 1 ? T($"1 app {singular}", $"1 programa {singular}", $"Приложений {singular} — 1") : T($"{count} apps {plural}", $"{count} programas {plural}", $"Приложений {plural} — {count}");

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

    private async void OpenProton(bool create = false)
    {
        if (HasPendingOperation) return;
        try
        {
            await proton.OpenAsync(create);
            if (IsDisposed || Disposing) return;
            footer.Text = create
                ? T("Follow the guide in the Proton window to create a profile.",
                    "Siga o assistente na janela da Proton para criar um perfil.", "Откройте мастер в окне Proton, чтобы создать профиль.")
                : T("Your Proton account profiles will appear here after you sign in.",
                    "Os perfis da sua conta Proton aparecerão aqui após o login.", "Профили учётной записи Proton появятся здесь после входа.");
        }
        catch (Exception error) when (Expected(error)) { if (!IsDisposed && !Disposing) Failure(error); }
    }

    private void RenderProtonAccount()
    {
        var label = proton.Catalog is { Ready: true } catalog
            ? T($"Proton account · {catalog.Profiles.Length} profiles found",
                $"Conta Proton · {catalog.Profiles.Length} perfis encontrados", $"Учётная запись Proton · найдено профилей: {catalog.Profiles.Length}")
            : T("Sign in to sync your Proton WireGuard profiles.",
                "Entre para sincronizar os perfis WireGuard da Proton.", "Войдите, чтобы синхронизировать профили WireGuard из Proton.");
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
            Text = T("Proton account profiles", "Perfis da conta Proton", "Профили учётной записи Proton"),
            ClientSize = new Size(760, 432), MinimumSize = new Size(560, 320),
            StartPosition = FormStartPosition.CenterParent, BackColor = Background,
            ForeColor = DesktopTheme.Text, Font = Body(), Padding = new Padding(16)
        };
        PrepareDialog(window);
        using var layout = Column(34, 36, 0, 48);
        layout.Controls.Add(SectionTitle(window.Text), 0, 0);
        layout.Controls.Add(MakeLabel(T("Use a profile already saved in Servers, or create one when you need a new server.",
            "Use um perfil já salvo em Servidores ou crie um se precisar de outro servidor.",
            "Используйте профиль из раздела «Серверы» или создайте новый для другого сервера."), muted: true), 0, 1);
        using var list = MakeGrid();
        AddColumn(list, T("Account profile", "Perfil da conta", "Профиль учётной записи"), 175, fill: true, font: Body(bold: true));
        AddColumn(list, T("Server", "Servidor", "Сервер"), 145, font: Mono);
        AddColumn(list, T("Availability", "Disponibilidade", "Доступность"), 225);
        foreach (var item in account.Profiles)
        {
            var state = item.State switch
            {
                ProtonProfileState.KeyRequired => T("Original .conf required", "Requer o .conf original", "Нужен исходный файл .conf"),
                ProtonProfileState.Available => T("Compatible", "Compatível", "Совместим"),
                _ => T("Unsupported", "Incompatível", "Не поддерживается")
            };
            if (item.Fingerprint is not null && settings.Profiles.Any(profile => profile.Fingerprint == item.Fingerprint))
                state = T("Saved on this PC", "Salvo neste PC", "Сохранён на этом ПК");
            list.Rows.Add(item.Name, item.Server ?? "—", state);
        }
        list.ClearSelection();
        using var empty = MakeLabel("", muted: true);
        empty.Visible = list.Rows.Count == 0;
        using var frame = GridFrame(list, empty, T("Your Proton account has no WireGuard profiles.",
            "Sua conta Proton ainda não tem perfis WireGuard.", "В учётной записи Proton пока нет профилей WireGuard."));
        layout.Controls.Add(frame, 0, 2);
        var create = ActionButton(T("New WireGuard…", "Novo WireGuard…", "Новый профиль WireGuard…"), () => { window.Close(); OpenProton(create: true); }, ButtonKind.Primary);
        var import = ActionButton(T("Import .conf", "Importar .conf", "Импорт .conf"), () => { window.Close(); ImportProfiles(); });
        var accountLink = ActionButton(T("Open in Proton", "Abrir na Proton", "Открыть в Proton"), () => { window.Close(); OpenProton(); });
        layout.Controls.Add(MakeBar(create, import, accountLink), 0, 3);
        window.Controls.Add(layout);
        window.ShowDialog(this);
    }

    // Config path of a session whose service stopped on its own and still needs its leftovers removed.
    private string? pendingCleanup;

    private async Task CleanUpStoppedService()
    {
        if (pendingCleanup is not { } configPath) return;
        busy = true;
        operation = T("Cleaning up the stopped service…", "Limpando o serviço interrompido…", "Очистка остановленной службы…");
        UpdateState();
        try
        {
            await Task.Run(() => engine.RemoveStopped(configPath));
            if (pendingCleanup == configPath) pendingCleanup = null;
            AddLog(T("Removed the stopped WireSock service and its session files.",
                "O serviço WireSock interrompido e os arquivos da sessão foram removidos.",
                "Остановленная служба WireSock и файлы сеанса удалены."));
        }
        // Keep pendingCleanup set so Poll retries while no session is active.
        catch (Exception error) when (Expected(error)) { AddLog(error.Message); }
        finally
        {
            busy = false;
            operation = "";
            if (!IsDisposed && !Disposing) UpdateState();
        }
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
                {
                    telemetry.RecordEvent(TelemetryEvent.ServiceStopped);
                    _ = telemetry.ReportErrorAsync(TelemetryStage.Service, new InvalidOperationException());
                    footer.Text = T("The Windows service stopped.", "O serviço do Windows parou.", "Служба Windows остановилась.");
                    pendingCleanup = activeSession.ConfigPath;
                    activeSession = null;
                    UpdateState();
                    // The Disconnect button disappears with the session, so remove the leftover service,
                    // session files and IPv6 block rules here instead of leaving them behind.
                    _ = CleanUpStoppedService();
                    return;
                }
                activeSession = session;
                UpdateState();
            }
            if (session is null && pendingCleanup is not null && pollTicks % 30 == 0)
            {
                _ = CleanUpStoppedService();
                return;
            }
            if (session is not null) pendingCleanup = null;
            if (session is not null && pollTicks % 6 == 0)
            {
                var health = ReadActiveDiagnostics(session);
                if (session.RequestedProtocol == ProtocolPreference.Automatic && !recoveryUsed &&
                    health is TunnelHealth.HandshakeTimedOut or TunnelHealth.EndpointResolutionFailed or TunnelHealth.RoutingFailed)
                {
                    _ = RecoverRoute(session);
                    return;
                }
            }
            if (session is not null) _ = MonitorRouteLatency(session);
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
                footer.Text = T($"Saved {profile.Name} from Proton VPN.", $"Perfil {profile.Name} salvo pela Proton VPN.", $"Профиль {profile.Name} из Proton VPN сохранён.");
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
            autoApply.Dispose();
            latencyLifetime.Cancel();
            latencyLifetime.Dispose();
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
            footer.Text = T("Copied.", "Copiado.", "Скопировано.");
        });
    }
}
