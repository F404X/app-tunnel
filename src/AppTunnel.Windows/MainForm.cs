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
    // A reconnect on start counts from its first step to its last, so the gaps between them (after the latency
    // check, before connecting) never let the tray rebuild the window or start an update.
    public bool HasPendingOperation => busy || pingBusy || updateBusy || choosingRoute || restoring;
    public bool CanDisconnect => !busy && !updateBusy && activeSession is not null;
    public event Action<string>? LanguageRequested;

    private readonly ThemeButton apply = MakeButton(T("Apply settings", "Aplicar ajustes", "Применить"), ButtonKind.Primary);
    private readonly HeroCard hero;
    private readonly InfoCard serverCard;
    private readonly InfoCard healthCard;
    private readonly FlowLayoutPanel chips = new();
    private readonly StackPanel appList = new();
    private readonly StackPanel serverList = new();
    private readonly StackPanel serverTop = new(fitContent: true);
    private readonly StackPanel serverBottom = new(fitContent: true);
    private readonly DropZone dropZone = new(T("Drag an .exe, a shortcut or a folder here", "Arraste um .exe, um atalho ou uma pasta para cá",
        "Перетащите сюда .exe, ярлык или папку"), T("The app joins the list already switched on", "O app entra na lista já ligado",
        "Приложение появится в списке уже включённым"));
    private readonly Label protonAccount = MakeLabel("", muted: true);
    private readonly Label pinCaption = MakeLabel("", muted: true);
    private readonly Label emptyRoutes = MakeLabel("", muted: true);
    private readonly StatusToast footer = new();
    private readonly Label engineInfo = MakeLabel("", muted: true);
    private readonly CheckBox lan = MakeCheck(T("Keep the local network direct", "Rede local sempre direta", "Локальная сеть напрямую"));
    private readonly CheckBox ipv6 = MakeCheck(T("Include IPv6 when available", "Incluir IPv6 quando disponível", "Использовать IPv6 при наличии"));
    private readonly CheckBox diagnostics = MakeCheck(T("Enable detailed WireSock logs", "Detalhar registros do WireSock", "Подробные журналы WireSock"));
    private readonly TextBox include = MakeText(placeholder: T("Optional", "Opcional", "Необязательно"));
    private readonly TextBox exclude = MakeText(placeholder: T("Optional", "Opcional", "Необязательно"));
    private readonly TextBox log = MakeText(multiline: true);
    private readonly Panel pageHost = new() { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Background };
    private readonly List<Control> pages = [];
    private ThemeButton[] navigationButtons = [];
    private readonly Dictionary<Guid, long?> latencies = new();
    private ExpanderCard? advanced;

    public MainForm(ProfileStore store, ProtonLogin proton, Telemetry telemetry)
    {
        this.store = store;
        this.proton = proton;
        this.telemetry = telemetry;
        settings = store.Load();
        RememberCountries();
        engine = new TunnelEngines(AddLog, new WireSockEngine());
        activeSession = engine.GetActive();

        hero = new HeroCard(apply);
        serverCard = new InfoCard(T("Server", "Servidor", "Сервер"), MakeButton(T("Change", "Trocar", "Сменить")));
        healthCard = new InfoCard(T("Connection", "Conexão", "Подключение"), MakeButton(T("Test", "Testar", "Проверить")));
        // The version in the title, including a beta or nightly suffix, says which build is running at a glance.
        Text = $"AppTunnel {AppUpdater.CurrentRelease}";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1140, 780);
        MinimumSize = new Size(1000, 740);
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
        advanced!.Expanded = include.Text.Trim().Length > 0 || exclude.Text.Trim().Length > 0;
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
            if (NeedsSetup) BeginInvoke(RunSetup);
            BeginInvoke(() => _ = ShowWhatsNew());
        };
        // Every time the window opens, not only the first: closing it only hides it in the tray, and notes that could
        // not be read (offline) are tried again on the next open. During a first-time setup it only records the version.
        VisibleChanged += (_, _) =>
        {
            if (Visible && IsHandleCreated) BeginInvoke(() => _ = ShowWhatsNew());
        };
    }

    private void WireActions()
    {
        hero.Power.Click += async (_, _) => await TogglePower();
        apply.Click += async (_, _) => await ChangeConnection(starting: true);
        serverCard.Action.Click += (_, _) => OpenPage(ServersPage);
        healthCard.Action.Click += async (_, _) => await TestConnection();
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

    private void AddExecutable()
    {
        using var picker = new OpenFileDialog { Title = T("Choose apps for the VPN", "Escolha apps para a VPN", "Выберите приложения для VPN"), Filter = T("Windows executables (*.exe)|*.exe", "Executáveis do Windows (*.exe)|*.exe", "Исполняемые файлы Windows (*.exe)|*.exe"), Multiselect = true };
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

    private void ImportProfiles()
    {
        if (busy || pingBusy) return;
        using var picker = new OpenFileDialog { Title = T("Import WireGuard profiles", "Importar perfis WireGuard", "Импорт профилей WireGuard"), Filter = T("VPN profiles (*.conf)|*.conf", "Perfis VPN (*.conf)|*.conf", "Профили VPN (*.conf)|*.conf"), Multiselect = true };
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
        footer.Text = T($"Imported {added} profiles. Rename a profile to make it easier to identify.", $"{added} perfil(is) importado(s). Dê um nome à rota para identificá-la.", $"Импортировано профилей: {added}. Для удобства можно переименовать маршрут.");
    }

    private StoredProfile? SelectedRoute() => settings.Profiles.Find(profile => profile.Id == settings.SelectedProfile);

    private void RenameRoute(Guid id)
    {
        if (settings.Profiles.Find(x => x.Id == id) is not { } profile) return;
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

    private void RemoveRoute(Guid id)
    {
        if (busy || pingBusy || settings.Profiles.Find(x => x.Id == id) is not { } profile) return;
        Attempt(() =>
        {
            var index = settings.Profiles.FindIndex(x => x.Id == profile.Id);
            var selected = settings.SelectedProfile;
            settings.Profiles.RemoveAt(index);
            if (selected == profile.Id) settings.SelectedProfile = settings.Profiles.FirstOrDefault()?.Id ?? Guid.Empty;
            try { Save(); }
            catch
            {
                settings.Profiles.Insert(index, profile);
                settings.SelectedProfile = selected;
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
            // The user may have pinned a server while the measurement ran; that choice wins.
            if (chooseFastest && best.profile is not null && settings.AutoServer)
            {
                settings.SelectedProfile = best.profile.Id;
                Changed();
            }
            DrawRoutes();
            footer.Text = best.profile is null ? T("The servers did not answer the ping. Trying to connect anyway.", "Os servidores não responderam ao ping. Tentando conectar mesmo assim.", "Серверы не ответили на пинг. Пробуем подключиться.")
                : T($"Fastest server: {best.profile.Name} · {best.latency} ms.", $"Servidor mais rápido: {best.profile.Name} · {best.latency} ms.", $"Самый быстрый сервер: {best.profile.Name} · {best.latency} мс.");
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
            Title = T("Locate WireSock", "Localizar o WireSock", "Найти WireSock"),
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

    // Servers saved by older versions get their country once, so the flag no longer depends on the name.
    private void RememberCountries()
    {
        var changed = false;
        for (var i = 0; i < settings.Profiles.Count; i++)
        {
            if (settings.Profiles[i].Country is not null) continue;
            settings.Profiles[i] = settings.Profiles[i] with { Country = store.GuessCountry(settings.Profiles[i]) };
            changed = true;
        }
        if (!changed) return;
        try { store.Save(settings); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
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
                RememberConnected(false);
                telemetry.RecordEvent(TelemetryEvent.Disconnected);
                healthDetails.Text = "";
                AddLog(T("VPN disconnected. Apps return to their normal connection.", "VPN desligada. Os programas voltam à conexão normal.", "VPN отключён. Приложения используют обычное соединение."));
                footer.Text = T("VPN disconnected. Reopen the call or client to establish new connections.", "VPN desligada. Reabra a chamada ou o cliente para renovar as conexões.", "VPN отключён. Перезапустите приложение или заново подключитесь к звонку.");
                return;
            }

            // In automatic mode a running session keeps the server the app chose (or moved to); only a pinned
            // choice switches servers.
            var live = settings.AutoServer && activeSession is { } running ? settings.Profiles.Find(x => x.Id == running.Profile.Id) : null;
            var route = live ?? SelectedRoute() ?? throw new InvalidOperationException(T("Add a server on the Servers page.", "Adicione um servidor na página Servidores.", "Добавьте сервер на странице «Серверы»."));
            var requested = settings.Applications.Where(x => x.Enabled).ToArray();
            var routed = requested.Where(x => x.Folder ? Directory.Exists(x.Match) : File.Exists(x.Match)).ToArray();
            var missing = requested.Length - routed.Length;
            if (routed.Length == 0)
                throw new InvalidOperationException(T("Turn on at least one app that exists on this PC on the Apps page.", "Ligue na página Apps ao menos um app que exista neste PC.", "Включите на странице «Приложения» хотя бы одно приложение, которое есть на этом ПК."));
            var options = CurrentRoute();
            var verbose = diagnostics.Checked;
            var preference = SelectedProtocol;
            // A pinned server never falls back to another endpoint; automatic mode may use every saved server.
            var profiles = settings.AutoServer ? settings.Profiles.ToArray() : [route];
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
            RememberConnected(true);
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

    private void RememberConnected(bool connected)
    {
        if (settings.Connected == connected) return;
        settings.Connected = connected;
        try { Save(); }
        catch (Exception error) when (Expected(error)) { AddLog(error.Message); }
    }

    private bool restoring;
    private static bool restoreAttempted;

    // Brings back the connection the user left on: same apps and server (both already saved), connected again
    // after a restart of Windows or AppTunnel. The tray timer calls this on the current window every tick; it waits
    // out other work (such as the latency check when the window opens) by returning, and runs once per process.
    public async Task RestoreConnection()
    {
        if (restoreAttempted || IsDisposed || Disposing || HasPendingOperation) return;
        restoreAttempted = true;
        if (activeSession is not null) RememberConnected(true);
        if (!settings.Connected || activeSession is not null || NeedsSetup ||
            settings.Profiles.Count == 0 || !settings.Applications.Any(x => x.Enabled)) return;
        AddLog(T("Reconnecting, as the VPN was on when AppTunnel was last used.",
            "Reconectando, pois a VPN estava ligada no último uso do AppTunnel.",
            "Повторное подключение: VPN был включён при последнем использовании AppTunnel."));
        restoring = true;
        try { await Power(); }
        finally
        {
            restoring = false;
            if (!IsDisposed && !Disposing) UpdateState();
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
        // An unattended reconnect never asks; "Always" still restarts the clients.
        if (restoring && settings.DiscordRestart == DiscordRestartChoice.Ask) return;
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
        // In automatic mode the app chooses and switches servers itself, so the pick never counts as a manual switch.
        // A pin is compared with the live server, so pinning the original server after a failover offers the switch back.
        var changedServer = session is not null && pick is not null && session.Profile.Id != pick.Id && !settings.AutoServer;
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
        var (pending, changedServer, canApply) = PendingChanges();
        // With automatic apply on, the button only remains for a server switch or to retry a failed apply.
        var applyingSoon = canApply && !changedServer && settings.AutoApply && !autoApplyFailed;

        apply.Text = changedServer ? T("Switch server", "Trocar servidor", "Сменить сервер") : T("Apply changes", "Aplicar mudanças", "Применить изменения");
        apply.Visible = running && canApply && !applyingSoon && !busy;
        apply.Enabled = !HasPendingOperation;
        pageHost.Enabled = !busy;
        foreach (var button in navigationButtons) button.Enabled = !busy;
        ipv6.Enabled = pick?.HasIpv6 == true;
        if (!running) healthDetails.Text = "";
        UpdateHome(session, pending, canApply, applyingSoon);
        if (drawnLive != session?.Profile.Id) DrawRoutes();
    }

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

    // Ctrl+Z runs the toast's Undo from the keyboard, except inside text fields, which keep their own undo.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Z) && ActiveControl is not TextBoxBase && footer.TryRunAction()) return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

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
        layout.Controls.Add(MakeLabel(T("Use a profile already saved in Routes, or create one when you need a new server.",
            "Use um perfil já salvo em Rotas ou crie um se precisar de outro servidor.",
            "Используйте профиль из раздела «Маршруты» или создайте новый для другого сервера."), muted: true), 0, 1);
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
        var create = ActionButton(T("New WireGuard…", "Novo WireGuard…", "Новый профиль WireGuard…"), () => { window.Close(); BeginInvoke(async () => await ChooseRoute()); }, ButtonKind.Primary);
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
                if ((session.RequestedProtocol == ProtocolPreference.Automatic || settings.AutoServer) && !recoveryUsed &&
                    health is TunnelHealth.HandshakeTimedOut or TunnelHealth.EndpointResolutionFailed or TunnelHealth.RoutingFailed)
                {
                    _ = RecoverRoute(session);
                    return;
                }
            }
            if (session is not null) _ = MonitorRouteLatency(session);
            var note = proton.Poll(config =>
            {
                if (AddProtonProfile(config) is { } profile)
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

    // Saves a Proton profile unless the same key is already saved; returns the new profile.
    private StoredProfile? AddProtonProfile(ProtonConfiguration config)
    {
        var profile = store.ImportProton(config, settings.Profiles);
        if (profile is null) return null;
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
        return profile;
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
        // Reconnecting on start runs unattended, often from the tray, so its errors stay in the log and status line.
        if (!restoring) ThemedMessage.Warn(this, e.Message);
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
