using System.Diagnostics;
using System.Net.Http;
using static AppTunnel.Windows.DesktopTheme;
using static AppTunnel.Core.Localized;

namespace AppTunnel.Windows;

internal sealed partial class MainForm
{
    private readonly CheckBox startup = MakeCheck(T("Start with Windows", "Iniciar com o Windows", "Запускать вместе с Windows"));
    private readonly CheckBox autoUpdate = MakeCheck(T("Update automatically", "Atualizar automaticamente", "Автоматически обновлять"));
    private readonly ThemeButton checkUpdates = MakeButton(T("Check for updates", "Verificar atualizações", "Проверить обновления"));
    private readonly Label updateStatus = MakeLabel("", muted: true);
    private bool updateBusy;

    public bool StartsWithWindows => startup.Checked;
    public event Action? StartupRequested;
    public event Action<ThemeMode>? ThemeRequested;
    public ThemeMode PreferredTheme => settings.Theme;

    private Control BuildSettingsPage()
    {
        var page = MakePage();
        var content = Column(34, 29, 29, 28, 10, 34, 36, 10, 34, 40, 34, 12, 34, 29, 29, 29, 42, 34, 10, 42, 0);
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

        content.Controls.Add(SectionTitle(T("Appearance", "Aparência", "Оформление")), 0, 5);
        var themes = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Font = Body(),
            BackColor = Raised, ForeColor = DesktopTheme.Text, Width = 230, Margin = new Padding(0, 2, 0, 0),
            AccessibleName = T("Color theme", "Tema de cores", "Цветовая тема")
        };
        themes.Items.AddRange([
            T("Follow Windows", "Seguir o Windows", "Как в Windows"),
            T("Light", "Claro", "Светлая"),
            T("Dark", "Escuro", "Тёмная")]);
        themes.SelectedIndex = Enum.IsDefined(settings.Theme) ? (int)settings.Theme : 0;
        themes.SelectedIndexChanged += (_, _) => BeginInvoke(() => ThemeRequested?.Invoke((ThemeMode)themes.SelectedIndex));
        content.Controls.Add(MakeBar(themes), 0, 6);

        content.Controls.Add(SectionTitle(T("Updates", "Atualizações", "Обновления")), 0, 8);
        checkUpdates.Click += async (_, _) => await CheckForUpdates();
        var installed = MakeLabel($"AppTunnel {AppUpdater.CurrentVersion}", muted: true);
        installed.Dock = DockStyle.None;
        installed.AutoSize = true;
        installed.Margin = new Padding(10, 7, 0, 0);
        content.Controls.Add(MakeBar(checkUpdates, installed), 0, 9);
        content.Controls.Add(updateStatus, 0, 10);
        AddPrivacyControls(content, 12);
        content.Controls.Add(MakeBar(ActionButton(TermsWindow.Caption, () => TermsWindow.Show(this))), 0, 19);
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = new Padding(0) };
        content.Dock = DockStyle.Top;
        content.Height = 603;
        scroll.Controls.Add(content);
        page.Controls.Add(scroll);
        return page;
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

    private async Task CheckForUpdates(bool automatic = false)
    {
        if (HasPendingOperation) return;
        updateBusy = true;
        checkUpdates.Enabled = false;
        UpdateState();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            updateStatus.Text = T("Checking GitHub for an update…", "Buscando atualização no GitHub…", "Проверка обновлений на GitHub…");
            var update = await AppUpdater.CheckAsync(timeout.Token);
            if (update is null)
            {
                updateStatus.Text = T("You're using the latest available version.", "Você está usando a versão mais recente disponível.", "Установлена последняя доступная версия.");
                return;
            }

            var version = update.Version.ToString();
            updateStatus.Text = T($"Downloading AppTunnel {version}…", $"Baixando AppTunnel {version}…", $"Скачивание AppTunnel {version}…");
            var progress = new Progress<int>(percent => updateStatus.Text =
                T($"Downloading AppTunnel {version} · {percent}%", $"Baixando AppTunnel {version} · {percent}%", $"Скачивание AppTunnel {version} · {percent}%"));
            var installer = await AppUpdater.DownloadAsync(update, progress, timeout.Token);
            updateStatus.Text = T($"AppTunnel {version} downloaded and verified.", $"AppTunnel {version} foi baixado e verificado.", $"AppTunnel {version} скачан и проверен.");
            if (automatic && !settings.AutoUpdate) return;
            if (!automatic)
            {
                var prompt = T($"Install AppTunnel {version} now? The setup will close the AppTunnel interface and Proton sign-in window.",
                    $"Instalar o AppTunnel {version} agora? O instalador fechará a interface do AppTunnel e a janela de login da Proton.",
                    $"Установить AppTunnel {version}? Установщик закроет интерфейс AppTunnel и окно входа в Proton.");
                if (MessageBox.Show(this, prompt, "AppTunnel", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
            }
            var arguments = automatic ? "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /REOPENAPPTUNNEL=1" : "";
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
            UpdateState();
        }
    }
}
