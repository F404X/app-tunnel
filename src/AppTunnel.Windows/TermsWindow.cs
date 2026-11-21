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
        var body = new TextBox
        {
            Text = reader.ReadToEnd().ReplaceLineEndings("\r\n"), Dock = DockStyle.Fill,
            ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical,
            AccessibleName = Caption, BackColor = Field, ForeColor = DesktopTheme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };
        var close = new ThemeButton { Text = T("Close", "Fechar", "Закрыть"), DialogResult = DialogResult.Cancel };
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0)
        };
        actions.Controls.Add(close);
        dialog.Controls.Add(body);
        dialog.Controls.Add(actions);
        dialog.CancelButton = close;
        // Read-only text should open unselected, with the caret at the top.
        dialog.Shown += (_, _) => body.Select(0, 0);
        dialog.ShowDialog(owner);
    }
}
