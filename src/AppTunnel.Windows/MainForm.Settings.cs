using System.Diagnostics;
using System.Net.Http;
using static AppTunnel.Core.Localized;

namespace AppTunnel.Windows;

internal sealed partial class MainForm
{
    private readonly CheckBox startup = MakeCheck(T("Start with Windows", "Iniciar com o Windows"));
    private readonly ThemeButton checkUpdates = MakeButton(T("Check for updates", "Verificar atualizações"));
    private readonly Label updateStatus = MakeLabel("", muted: true);
    private bool updateBusy;

    public bool StartsWithWindows => startup.Checked;
    public event Action? StartupRequested;

    private Control BuildSettingsPage()
    {
        var page = MakePage();
        var content = Column(34, 30, 28, 20, 34, 40, 35, 0);
        content.Controls.Add(SectionTitle(T("Startup", "Inicialização")), 0, 0);
        startup.AutoCheck = false;
        startup.Click += (_, _) => StartupRequested?.Invoke();
        content.Controls.Add(startup, 0, 1);
        content.Controls.Add(MakeLabel(T("Opens AppTunnel in the tray when you sign in to Windows.",
            "Abre o AppTunnel na bandeja quando você entra no Windows."), muted: true), 0, 2);
        content.Controls.Add(SectionTitle(T("Updates", "Atualizações")), 0, 4);
        checkUpdates.Click += async (_, _) => await CheckForUpdates();
        var installed = MakeLabel($"AppTunnel {AppUpdater.CurrentVersion}", muted: true);
        installed.Dock = DockStyle.None;
        installed.AutoSize = true;
        installed.Margin = new Padding(10, 7, 0, 0);
        content.Controls.Add(MakeBar(checkUpdates, installed), 0, 5);
        content.Controls.Add(updateStatus, 0, 6);
        page.Controls.Add(content);
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
                ? T("AppTunnel will start in the tray when you sign in.", "O AppTunnel abrirá na bandeja quando você entrar no Windows.")
                : T("AppTunnel will no longer start at sign-in.", "O AppTunnel não abrirá mais ao entrar no Windows.");
        });
    }

    private async Task CheckForUpdates()
    {
        if (HasPendingOperation) return;
        updateBusy = true;
        checkUpdates.Enabled = false;
        UpdateState();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            updateStatus.Text = T("Checking GitHub for an update…", "Buscando atualização no GitHub…");
            var update = await AppUpdater.CheckAsync(timeout.Token);
            if (update is null)
            {
                updateStatus.Text = T("You're using the latest available version.", "Você está usando a versão mais recente disponível.");
                return;
            }

            var version = update.Version.ToString();
            updateStatus.Text = T($"Downloading AppTunnel {version}…", $"Baixando AppTunnel {version}…");
            var progress = new Progress<int>(percent => updateStatus.Text =
                T($"Downloading AppTunnel {version} · {percent}%", $"Baixando AppTunnel {version} · {percent}%"));
            var installer = await AppUpdater.DownloadAsync(update, progress, timeout.Token);
            updateStatus.Text = T($"AppTunnel {version} downloaded and verified.", $"AppTunnel {version} foi baixado e verificado.");
            var prompt = T($"Install AppTunnel {version} now? The setup will close the AppTunnel interface and Proton sign-in window.",
                $"Instalar o AppTunnel {version} agora? O instalador fechará a interface do AppTunnel e a janela de login da Proton.");
            if (MessageBox.Show(this, prompt, "AppTunnel", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            using var setup = Process.Start(new ProcessStartInfo(installer) { WorkingDirectory = Path.GetDirectoryName(installer)! });
            if (setup is null)
                throw new InvalidOperationException(T("The AppTunnel installer did not open.", "O instalador do AppTunnel não abriu."));
        }
        catch (Exception error) when (Expected(error) || error is HttpRequestException or OperationCanceledException)
        {
            updateStatus.Text = T("Could not complete the update. Try again.", "Não foi possível concluir a atualização. Tente novamente.");
            Failure(error);
        }
        finally
        {
            updateBusy = false;
            checkUpdates.Enabled = true;
            UpdateState();
        }
    }
}
