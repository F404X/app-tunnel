using AppTunnel.Core;
using static AppTunnel.Core.Localized;
using static AppTunnel.Windows.DesktopTheme;

namespace AppTunnel.Windows;

internal sealed partial class MainForm
{
    private bool exploringApps;

    private async Task ExploreCommonApps()
    {
        if (exploringApps || HasPendingOperation) return;
        exploringApps = true;
        try
        {
            var found = await Task.Run(ApplicationDiscovery.FindCommonApps);
            if (IsDisposed || HasPendingOperation) return;
            ShowAppCatalog(found);
        }
        catch (Exception error) when (Expected(error)) { Failure(error); }
        finally { exploringApps = false; }
    }

    private void ShowAppCatalog(IReadOnlyList<ApplicationTarget> found)
    {
        var heading = T("Explore Common Apps", "Explorar aplicativos comuns", "Часто используемые приложения");
        using var dialog = new Form
        {
            Text = heading, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(764, 520),
            MinimumSize = new Size(570, 414), BackColor = Background, ForeColor = DesktopTheme.Text,
            Font = Body(), Padding = new Padding(16), MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false
        };
        PrepareDialog(dialog);
        using var layout = Column(35, 36, 0, 44);
        layout.Controls.Add(SectionTitle(heading), 0, 0);
        layout.Controls.Add(MakeLabel(T("Select installed apps to add them to your VPN rules.",
            "Selecione aplicativos instalados para incluí-los nas regras da VPN.",
            "Выберите установленные приложения, чтобы добавить их в правила VPN."), muted: true), 0, 1);
        using var cards = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Background, Margin = new Padding(0) };
        var groups = found.ToLookup(target => target.Label, StringComparer.OrdinalIgnoreCase);
        var choices = new List<(CheckBox Check, bool InitiallySelected, ApplicationTarget[] Routes)>();
        foreach (var app in CommonApps.Catalog)
        {
            var matches = groups[app.Name].ToArray();
            var installed = matches.Length > 0;
            var selected = matches.Any(match => settings.Applications.Any(existing => existing.Enabled &&
                (CoversTarget(existing, match) || CoversTarget(match, existing))));
            var card = new ThemeCard(installed ? Surface : Field)
            {
                Size = new Size(228, 60), Margin = new Padding(0, 0, 9, 9), Padding = new Padding(11, 5, 10, 5)
            };
            var label = installed ? T("Installed", "Instalado", "Установлено") : T("Not detected", "Não detectado", "Не найдено");
            var cardLayout = Column(28, 18);
            var check = MakeCheck(app.Name);
            check.Enabled = installed;
            check.Checked = selected;
            cardLayout.Controls.Add(check, 0, 0);
            var status = MakeLabel(label, muted: true);
            status.Font = Body(9);
            if (!installed) status.ForeColor = Disabled;
            cardLayout.Controls.Add(status, 0, 1);
            card.Controls.Add(cardLayout);
            if (installed)
            {
                status.Cursor = Cursors.Hand;
                status.Click += (_, _) => check.Checked = !check.Checked;
                choices.Add((check, selected, matches));
            }
            cards.Controls.Add(card);
        }
        layout.Controls.Add(cards, 0, 2);
        // Four columns, tall enough for every card, so the list only scrolls on a very small screen.
        var rows = (cards.Controls.Count + 3) / 4;
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        var scale = dialog.DeviceDpi / 96f;
        var needed = 32 + 35 + 36 + rows * 69 + 44 + 24;
        var limit = (int)(area.Height / scale * .9f);
        // Sized to fit, so no bar shows; scrolling stays on for a dialog made narrower or shorter by hand.
        dialog.ClientSize = new Size(4 * (228 + 9) + 32 + (needed > limit ? 24 : 4), Math.Min(needed, limit));
        var save = MakeButton(T("Apply selection", "Aplicar seleção", "Применить выбор"), ButtonKind.Primary);
        save.DialogResult = DialogResult.OK;
        var cancel = MakeButton(T("Cancel", "Cancelar", "Отмена"), ButtonKind.Quiet);
        cancel.DialogResult = DialogResult.Cancel;
        var buttons = MakeBar(save, cancel);
        buttons.FlowDirection = FlowDirection.RightToLeft;
        buttons.Padding = new Padding(0, 4, 0, 0);
        layout.Controls.Add(buttons, 0, 3);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = save;
        dialog.CancelButton = cancel;

        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        foreach (var (check, initial, matches) in choices)
        {
            if (check.Checked == initial) continue;
            for (var i = 0; i < settings.Applications.Count; i++)
            {
                var item = settings.Applications[i];
                // Same two-way coverage test used to pre-select the card, so unchecking also disables broader saved rules.
                if (matches.Any(match => CoversTarget(match, item) || CoversTarget(item, match)))
                    settings.Applications[i] = item with { Enabled = check.Checked };
            }
            if (check.Checked)
            {
                foreach (var match in matches) AddTarget(match);
            }
        }
        DrawApps();
        Changed();
        footer.Text = T("Common app selection saved.", "Seleção de aplicativos comuns salva.", "Выбор приложений сохранён.");
    }

    private static bool CoversTarget(ApplicationTarget rule, ApplicationTarget item) =>
        rule.Match.Equals(item.Match, StringComparison.OrdinalIgnoreCase) ||
        (rule.Folder && DiscordClients.IsRouted(item.Match, [rule with { Enabled = true }]));
}
