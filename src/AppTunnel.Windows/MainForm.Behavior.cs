using static AppTunnel.Core.Localized;
using static AppTunnel.Windows.DesktopTheme;

namespace AppTunnel.Windows;

internal sealed partial class MainForm
{
    private readonly CheckBox autoApplyChanges = MakeCheck(T("Apply changes automatically while connected",
        "Aplicar mudanças automaticamente durante a conexão", "Автоматически применять изменения при подключении"));
    private ThemeCombo? discordMode;
    private bool quietPingBusy;
    private DateTime lastQuietPing = DateTime.MinValue;

    // Settings rows for how the connection reacts to changes; returns the next free row.
    private int AddConnectionBehavior(TableLayoutPanel content, int row)
    {
        content.Controls.Add(SectionTitle(T("Connection", "Conexão", "Подключение")), 0, row++);
        autoApplyChanges.Checked = settings.AutoApply;
        autoApplyChanges.CheckedChanged += (_, _) =>
        {
            var previous = settings.AutoApply;
            settings.AutoApply = autoApplyChanges.Checked;
            try { Save(); }
            catch (Exception error) when (Expected(error))
            {
                settings.AutoApply = previous;
                Failure(error);
                return;
            }
            if (settings.AutoApply) ScheduleAutoApply();
            else autoApply.Stop();
            UpdateState();
        };
        content.Controls.Add(autoApplyChanges, 0, row++);

        var caption = T("When Discord needs a restart", "Quando o Discord precisar reiniciar", "Когда нужно перезапустить Discord");
        var label = MakeLabel(caption, muted: true);
        label.Dock = DockStyle.None;
        label.AutoSize = true;
        label.Margin = new Padding(0, 9, 10, 0);
        var mode = new ThemeCombo { Width = 240, Margin = new Padding(0, 2, 0, 0), AccessibleName = caption };
        mode.Items.AddRange([
            T("Ask every time", "Perguntar sempre", "Всегда спрашивать"),
            T("Restart automatically", "Reiniciar automaticamente", "Перезапускать автоматически"),
            T("Don't restart", "Não reiniciar", "Не перезапускать")]);
        mode.Glyphs.AddRange([Glyph.Question, Glyph.Refresh, Glyph.Ban]);
        mode.SelectedIndex = Enum.IsDefined(settings.DiscordRestart) ? (int)settings.DiscordRestart : 0;
        mode.SelectedIndexChanged += (_, _) => SaveDiscordChoice((DiscordRestartChoice)Math.Max(0, mode.SelectedIndex));
        discordMode = mode;
        content.Controls.Add(MakeBar(label, mode), 0, row++);
        return row;
    }

    private void SaveDiscordChoice(DiscordRestartChoice choice)
    {
        if (settings.DiscordRestart == choice) return;
        var previous = settings.DiscordRestart;
        settings.DiscordRestart = choice;
        try { Save(); }
        catch (Exception error) when (Expected(error))
        {
            settings.DiscordRestart = previous;
            Failure(error);
        }
        if (discordMode is not null && discordMode.SelectedIndex != (int)settings.DiscordRestart)
            discordMode.SelectedIndex = (int)settings.DiscordRestart;
    }

    // Replaces the Yes/No message box so the answer can be remembered instead of asked after every connection.
    private bool ConfirmDiscordRestart(string names)
    {
        if (settings.DiscordRestart == DiscordRestartChoice.Always) return true;
        if (settings.DiscordRestart == DiscordRestartChoice.Never) return false;

        using var window = new Form
        {
            Text = "AppTunnel", ClientSize = new Size(500, 178), FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, BackColor = Surface,
            ForeColor = DesktopTheme.Text, Font = Body(), Padding = new Padding(16, 12, 16, 10)
        };
        PrepareDialog(window);
        using var layout = Column(0, 32, 46);
        using var message = MakeLabel(T($"Restart {names} to use the VPN? This will end any active calls or streams.",
            $"Reiniciar {names} para usar a VPN? Isso interrompe chamadas e transmissões em andamento.",
            $"Перезапустить {names} для подключения к VPN? Текущие звонки и трансляции будут прерваны."));
        message.AutoEllipsis = false;
        message.TextAlign = ContentAlignment.TopLeft;
        layout.Controls.Add(message, 0, 0);
        using var remember = MakeCheck(T("Remember my choice", "Lembrar minha escolha", "Запомнить выбор"));
        remember.AccessibleDescription = T("You can change this later in Settings.", "Você pode mudar isso depois em Configurações.",
            "Это можно изменить позже в настройках.");
        layout.Controls.Add(remember, 0, 1);
        using var restart = MakeButton(T("Restart now", "Reiniciar agora", "Перезапустить"), ButtonKind.Primary);
        restart.DialogResult = DialogResult.Yes;
        using var later = MakeButton(T("Not now", "Agora não", "Не сейчас"));
        later.DialogResult = DialogResult.No;
        using var bar = MakeBar(restart, later);
        bar.Padding = new Padding(0, 6, 0, 0);
        layout.Controls.Add(bar, 0, 2);
        window.Controls.Add(layout);
        // Declining stays the default, as with the previous message box, so Enter never ends a call by accident.
        window.AcceptButton = later;
        window.CancelButton = later;
        window.ActiveControl = later;
        var accepted = window.ShowDialog(this) == DialogResult.Yes;
        if (remember.Checked) SaveDiscordChoice(accepted ? DiscordRestartChoice.Always : DiscordRestartChoice.Never);
        return accepted;
    }

    // Keeps the Latency column filled without a separate button; at most once a minute and never selecting a route.
    private async Task RefreshLatencyQuietly()
    {
        if (initializing || quietPingBusy || pingBusy || settings.Profiles.Count == 0 ||
            DateTime.UtcNow - lastQuietPing < TimeSpan.FromMinutes(1)) return;
        quietPingBusy = true;
        lastQuietPing = DateTime.UtcNow;
        try
        {
            var results = await PingRoutesAsync(settings.Profiles.ToArray(), minimum: 1, latencyLifetime.Token);
            if (IsDisposed || Disposing || pingBusy) return;
            foreach (var result in results) latencies[result.profile.Id] = result.latency;
            // While disconnected, automatic mode follows the fastest server so the next connection starts there.
            if (settings.AutoServer && activeSession is null && !busy && Fastest() is { } best && best.Profile.Id != settings.SelectedProfile)
            {
                settings.SelectedProfile = best.Profile.Id;
                Attempt(Save);
            }
            if (!busy)
            {
                DrawRoutes();
                UpdateState();
            }
        }
        catch (OperationCanceledException) when (latencyLifetime.IsCancellationRequested) { }
        finally
        {
            quietPingBusy = false;
        }
    }
}
