using static AppTunnel.Core.Localized;
using static AppTunnel.Windows.DesktopTheme;

namespace AppTunnel.Windows;

internal static class TermsWindow
{
    public static string Caption => T("Terms of use", "Termos de uso", "Условия использования");

    public static void Show(IWin32Window owner)
    {
        using var resource = typeof(TermsWindow).Assembly.GetManifestResourceStream($"AppTunnel.Terms.{Language}")
            ?? throw new InvalidOperationException(T("The AppTunnel terms of use are unavailable.",
                "Os termos de uso do AppTunnel estão indisponíveis.", "Условия использования AppTunnel недоступны."));
        using var reader = new StreamReader(resource);
        using var dialog = new Form
        {
            Text = T("AppTunnel Terms of Use", "Termos de uso do AppTunnel", "Условия использования AppTunnel"),
            StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(690, 490),
            MinimumSize = new Size(470, 320), Font = Body(), BackColor = Surface, ForeColor = DesktopTheme.Text,
            Padding = new Padding(14)
        };
        PrepareDialog(dialog);
        var text = reader.ReadToEnd().ReplaceLineEndings("\n");
        var body = ReadOnlyText(text, Body());
        body.AccessibleName = Caption;
        var frame = new ThemeCard(Field) { Dock = DockStyle.Fill, Padding = new Padding(14, 12, 6, 12), Radius = 10 };
        frame.Controls.Add(body);
        FitToText(dialog, text, Body(), 690 - 28 - 20 - 20, 14 * 2 + 24 + 56);
        var close = new ThemeButton { Text = T("Close", "Fechar", "Закрыть"), DialogResult = DialogResult.Cancel };
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0)
        };
        actions.Controls.Add(close);
        dialog.Controls.Add(frame);
        dialog.Controls.Add(actions);
        dialog.CancelButton = close;
        // Read-only text should open unselected, with the caret at the top.
        dialog.Shown += (_, _) => body.Select(0, 0);
        dialog.ShowDialog(owner);
    }
}
