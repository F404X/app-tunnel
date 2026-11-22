using System.Diagnostics;
using System.Net.Http;
using AppTunnel.Core;
using static AppTunnel.Windows.DesktopTheme;
using static AppTunnel.Core.Localized;

namespace AppTunnel.Windows;

internal sealed partial class MainForm
{
    private readonly CheckBox startup = MakeCheck(T("Start with Windows", "Iniciar com o Windows", "Запускать вместе с Windows"));
    private readonly CheckBox autoUpdate = MakeCheck(T("Update automatically", "Atualizar automaticamente", "Автоматически обновлять"));
    private readonly ThemeButton checkUpdates = MakeButton(T("Check for updates", "Verificar atualizações", "Проверить обновления"));
    private readonly Label updateStatus = MakeLabel("", muted: true);
    private readonly ThemeButton updateWireSock = MakeButton(T("Update WireSock", "Atualizar WireSock", "Обновить WireSock"));
    private readonly Label wireSockUpdateStatus = MakeLabel("", muted: true);
    private bool updateBusy;

    public bool StartsWithWindows => startup.Checked;
    public event Action? StartupRequested;
    public event Action<ThemeMode>? ThemeRequested;
    public ThemeMode PreferredTheme => settings.Theme;

    private Control BuildSettingsPage()
    {
        var page = Column(84, 18, 0);
        page.BackColor = Background;
        page.Controls.Add(new PageHeader(T("Settings", "Configurações", "Настройки"),
            T("Startup, appearance, updates and privacy.", "Inicialização, aparência, atualizações e privacidade.",
                "Автозапуск, оформление, обновления и конфиденциальность.")) { Dock = DockStyle.Fill }, 0, 0);
        // Two columns so every setting fits the window without scrolling.
        var content = Column(34, 32, 32, 40, 14, 34, 44, 14, 34, 32, 44, 14, 46, 0);
        content.BackColor = Background;
        content.Margin = new Padding(0, 0, 24, 0);
        var side = Column(34, 44, 46, 28, 28, 10, 34, 32, 32, 32, 46, 28, 0);
        side.BackColor = Background;
        side.Margin = new Padding(24, 0, 0, 0);
        content.Controls.Add(SectionTitle(T("Startup", "Inicialização", "Автозапуск")), 0, 0);
        startup.AutoCheck = false;
        startup.Click += (_, _) => StartupRequested?.Invoke();
        content.Controls.Add(startup, 0, 1);
        autoUpdate.Checked = settings.AutoUpdate;
        autoUpdate.Enabled = WindowsStartup.IsInstalled;
        autoUpdate.CheckedChanged += (_, _) =>
        {
            var previous = settings.AutoUpdate;
            settings.AutoUpdate = autoUpdate.Checked;
            try { Save(); }
            catch (Exception error) when (Expected(error))
            {
                settings.AutoUpdate = previous;
                Failure(error);
            }
        };
        content.Controls.Add(autoUpdate, 0, 2);
        content.Controls.Add(MakeLabel(autoUpdate.Enabled
            ? T("Automatic updates check daily and install verified releases in the background.",
                "A atualização automática verifica diariamente e instala versões verificadas em segundo plano.",
                "Автообновление ежедневно проверяет и устанавливает проверенные версии в фоне.")
            : T("Automatic installation is available in the installed edition.",
                "A atualização automática está disponível na edição instalada.",
                "Автоматическая установка доступна в установленной версии."), muted: true), 0, 3);
        if (content.GetControlFromPosition(0, 3) is Label note)
        {
            note.AutoEllipsis = false;
            note.TextAlign = ContentAlignment.TopLeft;
        }

        content.Controls.Add(SectionTitle(T("Appearance", "Aparência", "Оформление")), 0, 5);
        var themes = new ThemeCombo
        {
            Width = 240, Margin = new Padding(0, 2, 0, 0), AccessibleName = T("Color theme", "Tema de cores", "Цветовая тема")
        };
        themes.Items.AddRange([
            T("Follow Windows", "Seguir o Windows", "Как в Windows"),
            T("Light", "Claro", "Светлая"),
            T("Dark", "Escuro", "Тёмная")]);
        themes.Glyphs.AddRange([Glyph.Monitor, Glyph.Sun, Glyph.Moon]);
        themes.SelectedIndex = Enum.IsDefined(settings.Theme) ? (int)settings.Theme : 0;
        themes.SelectedIndexChanged += (_, _) => BeginInvoke(() => ThemeRequested?.Invoke((ThemeMode)themes.SelectedIndex));
        content.Controls.Add(MakeBar(themes), 0, 6);
        AddConnectionBehavior(content, 8);

        side.Controls.Add(SectionTitle(T("Updates", "Atualizações", "Обновления")), 0, 0);
        checkUpdates.Click += async (_, _) => await CheckForUpdates();
        var installed = MakeLabel($"AppTunnel {AppUpdater.CurrentRelease}", muted: true);
        installed.Dock = DockStyle.None;
        installed.AutoSize = true;
        installed.Margin = new Padding(10, 11, 0, 0);
        updateWireSock.Click += async (_, _) => await CheckWireSockUpdate();
        side.Controls.Add(BuildChannelSelector(), 0, 1);
        var updateButtons = MakeBar(checkUpdates, updateWireSock, installed);
        // Keeps the buttons clear of the channel selector once both rows shrink to fit their content.
        updateButtons.Margin = new Padding(0, 14, 0, 0);
        side.Controls.Add(updateButtons, 0, 2);
        side.Controls.Add(updateStatus, 0, 3);
        side.Controls.Add(wireSockUpdateStatus, 0, 4);
        AddPrivacyControls(side, 6);
        content.Controls.Add(MakeBar(ActionButton(TermsWindow.Caption, () => TermsWindow.Show(this)),
            ActionButton(T("Run first-time setup", "Refazer configuração inicial", "Запустить первоначальную настройку"), RunSetup)), 0, 12);
        WrapBars(content);
        WrapBars(side);
        var columns = Pair(50);
        columns.BackColor = Background;
        columns.Controls.Add(content, 0, 0);
        columns.Controls.Add(side, 1, 0);
        page.Controls.Add(columns, 0, 2);
        return page;
    }

    // Button rows wrap onto a second line in a narrow window instead of being cut off; their rows grow to fit.
    private static void WrapBars(TableLayoutPanel table)
    {
        foreach (var bar in table.Controls.OfType<FlowLayoutPanel>().ToArray())
        {
            var row = table.GetRow(bar);
            bar.WrapContents = true;
            bar.AutoSize = true;
            bar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            bar.Dock = DockStyle.None;
            bar.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            table.RowStyles[row] = new RowStyle(SizeType.AutoSize);
        }
    }

    private async Task CheckWireSockUpdate()
    {
        if (HasPendingOperation) return;
        if (activeSession is not null)
        {
            wireSockUpdateStatus.Text = T("Disconnect the VPN before updating WireSock.",
                "Desconecte a VPN antes de atualizar o WireSock.", "Перед обновлением WireSock отключите VPN.");
            return;
        }
        updateBusy = true;
        updateWireSock.Enabled = false;
        checkUpdates.Enabled = false;
        if (channelMode is not null) channelMode.Enabled = false;
        wireSockUpdateStatus.Text = T("Checking and installing WireSock updates via WinGet…",
            "Verificando e instalando atualizações do WireSock pelo WinGet…", "Проверка и установка обновлений WireSock через WinGet…");
        UpdateState();
        try
        {
            var result = await Task.Run(WireSockUpdater.Upgrade);
            if (IsDisposed || Disposing) return;
            var installed = result.Installed?.ToString() ?? "—";
            var latest = result.Latest?.ToString() ?? "—";
            var message = result.Updated
                ? T($"WireSock was updated to {installed}.", $"WireSock atualizado para {installed}.", $"WireSock обновлён до {installed}.")
                : result.ExitCode switch
                {
                    WireSockUpdater.NoUpdate => T($"WireSock {installed} is up to date (latest on WinGet: {latest}).",
                        $"O WireSock {installed} está atualizado (mais recente no WinGet: {latest}).",
                        $"WireSock {installed} актуален (последняя версия в WinGet: {latest})."),
                    WireSockUpdater.AlreadyInstalled => T($"WinGet ran, but WireSock is still {installed} (latest: {latest}).",
                        $"O WinGet executou, mas o WireSock continua em {installed} (mais recente: {latest}).",
                        $"WinGet завершил работу, но WireSock всё ещё {installed} (последняя: {latest})."),
                    WireSockUpdater.PackageNotFound => T("WinGet did not find the WireSock CLI package.",
                        "O WinGet não encontrou o pacote WireSock CLI.", "WinGet не нашёл пакет WireSock CLI."),
                    var code => T($"WinGet could not update WireSock (0x{code:X8}).",
                        $"O WinGet não conseguiu atualizar o WireSock (0x{code:X8}).", $"WinGet не смог обновить WireSock (0x{code:X8}).")
                };
            if (result.Updated)
            {
                wireSockChecked = default;
                UpdateEngineInfo();
            }
            AddLog(message);
            wireSockUpdateStatus.Text = message;
        }
        catch (Exception error) when (Expected(error))
        {
            if (!IsDisposed && !Disposing)
            {
                AddLog(error.Message);
                wireSockUpdateStatus.Text = error.Message;
            }
        }
        finally
        {
            updateBusy = false;
            if (!IsDisposed && !Disposing)
            {
                updateWireSock.Enabled = true;
                checkUpdates.Enabled = true;
                if (channelMode is not null) channelMode.Enabled = true;
                UpdateState();
            }
        }
    }

    public void RefreshStartup()
    {
        try { startup.Checked = WindowsStartup.IsEnabled(); }
        catch (Exception error) when (Expected(error)) { startup.Checked = false; }
    }

    public void SetStartup(bool enabled)
    {
        Attempt(() =>
        {
            WindowsStartup.SetEnabled(enabled);
            startup.Checked = enabled;
            footer.Text = enabled
                ? T("AppTunnel will start in the tray when you sign in.", "O AppTunnel abrirá na bandeja quando você entrar no Windows.", "AppTunnel будет открываться в трее при входе в Windows.")
                : T("AppTunnel will no longer start at sign-in.", "O AppTunnel não abrirá mais ao entrar no Windows.", "Автозапуск AppTunnel отключён.");
        });
    }

    public bool TrySaveTheme(ThemeMode theme)
    {
        if (!Enum.IsDefined(theme) || settings.Theme == theme) return false;
        var original = settings.Theme;
        settings.Theme = theme;
        try { Save(); return true; }
        catch (Exception error) when (Expected(error))
        {
            settings.Theme = original;
            Failure(error);
            return false;
        }
    }

    private ThemeCombo? channelMode;

    private UpdateChannel Channel => Enum.IsDefined(settings.UpdateChannel) ? settings.UpdateChannel : UpdateChannel.Stable;

    private static string ChannelName(UpdateChannel channel) => channel switch
    {
        UpdateChannel.Beta => T("Beta", "Beta", "Бета"),
        UpdateChannel.Nightly => T("Nightly", "Nightly", "Ночная сборка"),
        _ => T("Stable", "Estável", "Стабильный")
    };

    private Control BuildChannelSelector()
    {
        var caption = T("Update channel", "Canal de atualização", "Канал обновлений");
        var label = MakeLabel(caption, muted: true);
        label.Dock = DockStyle.None;
        label.AutoSize = true;
        label.Margin = new Padding(0, 11, 10, 0);
        var mode = new ThemeCombo { Width = 260, Margin = new Padding(0, 2, 0, 0), AccessibleName = caption };
        mode.Items.AddRange([
            T("Stable", "Estável", "Стабильный"),
            T("Beta · previews of the next version", "Beta · prévias da próxima versão", "Бета · предварительные версии"),
            T("Nightly · every change, experimental", "Nightly · cada mudança, experimental", "Ночная · каждое изменение, экспериментально")]);
        mode.Glyphs.AddRange([Glyph.CheckCircle, Glyph.Flask, Glyph.Moon]);
        mode.SelectedIndex = (int)Channel;
        mode.SelectedIndexChanged += async (_, _) =>
        {
            var chosen = (UpdateChannel)Math.Max(0, mode.SelectedIndex);
            if (chosen == Channel) return;
            var previous = settings.UpdateChannel;
            var lastCheck = settings.LastAutoUpdateCheck;
            settings.UpdateChannel = chosen;
            // The next automatic check runs on the new channel instead of waiting a day.
            settings.LastAutoUpdateCheck = null;
            try { Save(); }
            catch (Exception error) when (Expected(error))
            {
                settings.UpdateChannel = previous;
                settings.LastAutoUpdateCheck = lastCheck;
                mode.SelectedIndex = (int)previous;
                Failure(error);
                return;
            }
            // Changing the channel only looks; installing stays a separate, confirmed step.
            if (HasPendingOperation)
            {
                updateStatus.Text = T($"Channel set to {ChannelName(chosen)}. Check for updates when the current task finishes.",
                    $"Canal alterado para {ChannelName(chosen)}. Verifique atualizações quando a tarefa atual terminar.",
                    $"Выбран канал «{ChannelName(chosen)}». Проверьте обновления, когда текущая задача завершится.");
                return;
            }
            await CheckForUpdates(checkOnly: true);
        };
        channelMode = mode;
        return MakeBar(label, mode);
    }

    public async Task CheckForAutomaticUpdate()
    {
        var now = DateTimeOffset.UtcNow;
        if (!settings.AutoUpdate || !autoUpdate.Enabled || HasPendingOperation ||
            (settings.LastAutoUpdateCheck is { } last && now - last < TimeSpan.FromHours(24))) return;
        settings.LastAutoUpdateCheck = now;
        try { Save(); }
        catch (Exception error) when (Expected(error)) { return; }
        await CheckForUpdates(automatic: true);
    }

    private async Task CheckForUpdates(bool automatic = false, bool checkOnly = false)
    {
        if (HasPendingOperation) return;
        updateBusy = true;
        checkUpdates.Enabled = false;
        if (channelMode is not null) channelMode.Enabled = false;
        UpdateState();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            updateStatus.Text = T("Checking GitHub for an update…", "Buscando atualização no GitHub…", "Проверка обновлений на GitHub…");
            var channel = Channel;
            var installed = AppUpdater.CurrentRelease;
            var update = await AppUpdater.FindBestAsync(channel, timeout.Token);
            var name = ChannelName(channel);
            if (update is null)
            {
                updateStatus.Text = T($"No {name} release is available yet.", $"Ainda não há versão no canal {name}.", $"В канале «{name}» пока нет выпусков.");
                return;
            }
            // AppTunnel never installs a lower version: after leaving a faster channel it waits for this one to catch up.
            if (update.Version < installed)
            {
                updateStatus.Text = T($"Installed {installed} is ahead of {name} ({update.Version}); updates resume when {name} catches up.",
                    $"A versão instalada {installed} está à frente do canal {name} ({update.Version}); as atualizações voltam quando ele alcançar.",
                    $"Установленная {installed} новее канала «{name}» ({update.Version}); обновления возобновятся, когда канал догонит.");
                return;
            }
            if (update.Version <= installed)
            {
                updateStatus.Text = T($"You're using the latest {name} version.", $"Você está usando a versão mais recente do canal {name}.",
                    $"Установлена последняя версия канала «{name}».");
                return;
            }
            if (checkOnly)
            {
                updateStatus.Text = T($"AppTunnel {update.Version} is available on {name}. Use Check for updates to install it.",
                    $"O AppTunnel {update.Version} está disponível no canal {name}. Use Verificar atualizações para instalar.",
                    $"AppTunnel {update.Version} доступен в канале «{name}». Нажмите «Проверить обновления», чтобы установить.");
                return;
            }

            var version = update.Version.ToString();
            updateStatus.Text = T($"Downloading AppTunnel {version}…", $"Baixando AppTunnel {version}…", $"Скачивание AppTunnel {version}…");
            var progress = new Progress<int>(percent => updateStatus.Text =
                T($"Downloading AppTunnel {version} · {percent}%", $"Baixando AppTunnel {version} · {percent}%", $"Скачивание AppTunnel {version} · {percent}%"));
            var installer = await AppUpdater.DownloadAsync(update, progress, timeout.Token);
            updateStatus.Text = T($"AppTunnel {version} downloaded and verified.", $"AppTunnel {version} foi baixado e verificado.", $"AppTunnel {version} скачан и проверен.");
            // The channel cannot change during the update, but the choice is checked again before Setup starts.
            if (automatic && !settings.AutoUpdate || !ReleaseUpdate.Includes(Channel, update.Version)) return;
            if (!automatic)
            {
                var prompt = T($"Install AppTunnel {version} now? The setup will close the AppTunnel interface and Proton sign-in window.",
                    $"Instalar o AppTunnel {version} agora? O instalador fechará a interface do AppTunnel e a janela de login da Proton.",
                    $"Установить AppTunnel {version}? Установщик закроет интерфейс AppTunnel и окно входа в Proton.");
                // An update must never be blocked by the confirmation itself: if it cannot open, the installer opens
                // with its own wizard, which asks before changing anything.
                bool confirmed;
                try { confirmed = ThemedMessage.Ask(this, prompt); }
                catch (Exception error) when (Expected(error))
                {
                    AddLog(error.Message);
                    confirmed = true;
                }
                if (!confirmed) return;
            }
            var arguments = $"/LANG={InstallerLanguage}" + (automatic ? " /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /REOPENAPPTUNNEL=1" : "");
            using var setup = Process.Start(new ProcessStartInfo(installer, arguments) { WorkingDirectory = Path.GetDirectoryName(installer)! });
            if (setup is null)
                throw new InvalidOperationException(T("The AppTunnel installer did not open.", "O instalador do AppTunnel não abriu.", "Не удалось открыть установщик AppTunnel."));
        }
        catch (Exception error) when (Expected(error) || error is HttpRequestException or OperationCanceledException)
        {
            updateStatus.Text = T("Could not complete the update. Try again.", "Não foi possível concluir a atualização. Tente novamente.", "Не удалось завершить обновление. Попробуйте снова.");
            if (!automatic) Failure(error);
        }
        finally
        {
            updateBusy = false;
            checkUpdates.Enabled = true;
            if (channelMode is not null) channelMode.Enabled = true;
            UpdateState();
        }
    }
}
