using static AppTunnel.Core.Localized;
using static AppTunnel.Windows.DesktopTheme;

namespace AppTunnel.Windows;

// Replaces MessageBox, whose buttons follow the Windows display language: captions here follow the AppTunnel
// language and the window follows the light/dark theme.
internal static class ThemedMessage
{
    public static void Warn(IWin32Window? owner, string text) => Show(owner, text, question: false);

    public static bool Ask(IWin32Window? owner, string text) => Show(owner, text, question: true) == DialogResult.Yes;

    private static DialogResult Show(IWin32Window? owner, string text, bool question)
    {
        using var dialog = new Form
        {
            Text = "AppTunnel", FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false,
            ShowInTaskbar = owner is null, StartPosition = owner is null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent,
            BackColor = Surface, ForeColor = DesktopTheme.Text, Font = Body(), Padding = new Padding(20, 18, 20, 14),
            ClientSize = new Size(480, 160)
        };
        PrepareDialog(dialog);
        var message = new SmoothLabel
        {
            Text = text, Dock = DockStyle.Fill, AutoEllipsis = false, TextAlign = ContentAlignment.TopLeft, Font = Body(),
            ForeColor = DesktopTheme.Text, Padding = new Padding(36, 2, 0, 0), BackColor = Surface
        };
        message.Paint += (_, e) =>
        {
            var s = DpiScale(message);
            var badge = new RectangleF(0, 0, 24 * s, 24 * s);
            PaintCircle(e.Graphics, badge, question ? AccentWash : DangerWash);
            PaintIcon(e.Graphics, Glyph.Alert, RectangleF.Inflate(badge, -4 * s, -4 * s), question ? Accent : Danger, 2.4f);
        };
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 10, 0, 0), BackColor = Surface
        };
        if (question)
        {
            var yes = new ThemeButton { Text = T("Yes", "Sim", "Да"), Kind = ButtonKind.Primary, DialogResult = DialogResult.Yes };
            var no = new ThemeButton { Text = T("No", "Não", "Нет"), DialogResult = DialogResult.No };
            actions.Controls.AddRange([yes, no]);
            // Declining stays the default, as with the message boxes this replaces.
            dialog.AcceptButton = no;
            dialog.CancelButton = no;
            dialog.ActiveControl = no;
        }
        else
        {
            var ok = new ThemeButton { Text = "OK", Kind = ButtonKind.Primary, DialogResult = DialogResult.OK };
            actions.Controls.Add(ok);
            dialog.AcceptButton = ok;
            dialog.CancelButton = ok;
        }
        dialog.Controls.Add(message);
        dialog.Controls.Add(actions);
        // Tall enough for the whole message, so it never needs a scrollbar.
        var height = MeasureText(dialog, text, Body(), (int)((480 - 40 - 36) * dialog.DeviceDpi / 96f), wrap: true).Height * 96 / dialog.DeviceDpi;
        dialog.ClientSize = new Size(480, Math.Max(150, height + 18 + 14 + 64 + 8));
        return owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
    }
}
