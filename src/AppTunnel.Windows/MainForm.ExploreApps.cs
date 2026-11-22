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
        var choices = new List<(CheckBox Check, bool InitiallySelected, ApplicationTarget[] Routes)>();
        using var dialog = BuildAppCatalog(found, matches => matches.Any(match => settings.Applications.Any(existing => existing.Enabled &&
            (CoversTarget(existing, match) || CoversTarget(match, existing)))), choices);
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

    // Built separately from showing it, so the checks can lay it out off screen.
    internal static Form BuildAppCatalog(IReadOnlyList<ApplicationTarget> found, Func<ApplicationTarget[], bool> isSelected,
        List<(CheckBox Check, bool InitiallySelected, ApplicationTarget[] Routes)> choices)
    {
        var heading = T("Explore Common Apps", "Explorar aplicativos comuns", "Часто используемые приложения");
        var dialog = new Form
        {
            Text = heading, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(764, 520),
            MinimumSize = new Size(570, 414), BackColor = Background, ForeColor = DesktopTheme.Text,
            Font = Body(), Padding = new Padding(16), MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false
        };
        PrepareDialog(dialog);
        var layout = Column(35, 36, 0, 44);
        layout.Controls.Add(SectionTitle(heading), 0, 0);
        layout.Controls.Add(MakeLabel(T("Select installed apps to add them to your VPN rules.",
            "Selecione aplicativos instalados para incluí-los nas regras da VPN.",
            "Выберите установленные приложения, чтобы добавить их в правила VPN."), muted: true), 0, 1);
        var cards = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Background, Margin = new Padding(0) };
        var groups = found.ToLookup(target => target.Label, StringComparer.OrdinalIgnoreCase);
        foreach (var app in CommonApps.Catalog)
        {
            var matches = groups[app.Name].ToArray();
            var installed = matches.Length > 0;
            var selected = isSelected(matches);
            var card = new ThemeCard(installed ? Surface : Field)
            {
                Size = new Size(CardWidth, CardHeight), Margin = new Padding(0), Padding = new Padding(11, 5, 10, 5)
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
        var arranging = false;
        cards.Layout += (_, _) =>
        {
            if (arranging) return;
            arranging = true;
            try { ArrangeCards(cards); }
            finally { arranging = false; }
        };
        // Four columns, tall enough for every card, so the list only scrolls on a very small screen.
        var rows = (cards.Controls.Count + CardColumns - 1) / CardColumns;
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        var scale = dialog.DeviceDpi / 96f;
        var needed = 32 + 35 + 36 + rows * (CardHeight + CardGap) + 44 + 24;
        var limit = (int)(area.Height / scale * .9f);
        var gridWidth = CardColumns * CardWidth + (CardColumns - 1) * CardGap;
        // A scrollbar only appears on a short screen; the cards then share the narrower width.
        dialog.ClientSize = new Size(gridWidth + 32 + (needed > limit ? SystemInformation.VerticalScrollBarWidth : 0), Math.Min(needed, limit));
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
        return dialog;
    }

    private const int CardColumns = 4, CardWidth = 228, CardHeight = 60, CardGap = 10;

    // Lays the cards out as an even grid: as many columns as fit (at most four), every card the same width, the same
    // gap between cards, and the grid flush with both edges, at any DPI and with or without a scrollbar.
    private static void ArrangeCards(FlowLayoutPanel cards)
    {
        var scale = cards.DeviceDpi / 96f;
        var gap = (int)Math.Round(CardGap * scale);
        var width = cards.ClientSize.Width;
        if (width <= 0 || cards.Controls.Count == 0) return;
        var minimum = (int)Math.Round(200 * scale);
        var columns = Math.Clamp((width + gap) / (minimum + gap), 1, CardColumns);
        var cardWidth = (width - gap * (columns - 1)) / columns;
        // The pixels left over from the division are split between both sides.
        var spare = width - cardWidth * columns - gap * (columns - 1);
        var padding = new Padding(spare / 2, 0, spare - spare / 2, 0);
        if (cards.Padding != padding) cards.Padding = padding;
        for (var i = 0; i < cards.Controls.Count; i++)
        {
            var card = cards.Controls[i];
            var margin = new Padding(0, 0, i % columns == columns - 1 ? 0 : gap, gap);
            if (card.Margin != margin) card.Margin = margin;
            if (card.Width != cardWidth) card.Width = cardWidth;
        }
    }

    private static bool CoversTarget(ApplicationTarget rule, ApplicationTarget item) =>
        rule.Match.Equals(item.Match, StringComparison.OrdinalIgnoreCase) ||
        (rule.Folder && DiscordClients.IsRouted(item.Match, [rule with { Enabled = true }]));
}
