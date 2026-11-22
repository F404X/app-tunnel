using static AppTunnel.Core.Localized;
using static AppTunnel.Windows.DesktopTheme;

namespace AppTunnel.Windows;

// What changed in AppTunnel: before an update, a short list with Install now / Not now; after it, the complete
// notes of the new version. In both, Read more swaps in the entire changelog and grows the window to fit.
internal static class ChangelogWindow
{
    public static bool AskInstall(IWin32Window owner, string title, string intro, string summary, string full) =>
        Show(owner, title, intro, summary, full, question: true) == DialogResult.Yes;

    public static void ShowWhatsNew(IWin32Window owner, string title, string intro, string notes, string full) =>
        Show(owner, title, intro, notes, full, question: false);

    private static DialogResult Show(IWin32Window owner, string title, string intro, string text, string full, bool question)
    {
        using var dialog = Build(standalone: false, title, intro, text, full, question);
        return dialog.ShowDialog(owner);
    }

    // Built without showing it, so the checks can build every variant; the update confirmation must never fail.
    internal static Form Build(bool standalone, string title, string intro, string text, string full, bool question)
    {
        const int width = 580;
        var dialog = new Form
        {
            Text = "AppTunnel", FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false,
            ShowInTaskbar = standalone, StartPosition = standalone ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent,
            BackColor = Surface, ForeColor = DesktopTheme.Text, Font = Body(), Padding = new Padding(24, 20, 24, 16),
            ClientSize = new Size(width, 360)
        };
        PrepareDialog(dialog);

        var heading = new SmoothLabel
        {
            Text = title, Dock = DockStyle.Top, Height = 34, Font = Heading(15), ForeColor = DesktopTheme.Text, BackColor = Surface,
            AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft
        };
        var introHeight = intro.Length == 0 ? 0 : MeasureLogical(dialog, intro, Body(), width - 48) + 12;
        var introLabel = new SmoothLabel
        {
            Text = intro, Dock = DockStyle.Top, Height = introHeight, Font = Body(), ForeColor = Muted, BackColor = Surface,
            AutoEllipsis = false, TextAlign = ContentAlignment.TopLeft, Padding = new Padding(0, 4, 0, 0), Visible = intro.Length > 0
        };

        var notes = ReadOnlyText(text, Body());
        notes.AccessibleName = T("Changes", "Mudanças", "Изменения");
        var frame = new ThemeCard(Field) { Dock = DockStyle.Fill, Padding = new Padding(14, 12, 6, 10), Margin = new Padding(0), Edge = Border, Radius = 10 };
        frame.Controls.Add(notes);
        var spacer = new Panel { Dock = DockStyle.Top, Height = 12, BackColor = Surface };

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 14, 0, 0), BackColor = Surface, WrapContents = false
        };
        if (question)
        {
            var install = new ThemeButton { Text = T("Install now", "Instalar agora", "Установить"), Kind = ButtonKind.Primary, DialogResult = DialogResult.Yes };
            var later = new ThemeButton { Text = T("Not now", "Agora não", "Не сейчас"), DialogResult = DialogResult.No };
            actions.Controls.AddRange([install, later]);
            // Declining stays the default, so Enter never installs by accident.
            dialog.AcceptButton = later;
            dialog.CancelButton = later;
        }
        else
        {
            var ok = new ThemeButton { Text = "OK", Kind = ButtonKind.Primary, DialogResult = DialogResult.OK };
            actions.Controls.Add(ok);
            dialog.AcceptButton = ok;
            dialog.CancelButton = ok;
        }
        var readMore = new ThemeButton { Text = T("Read more", "Ler mais", "Подробнее"), Kind = ButtonKind.Link, Visible = full.Length > 0 };
        readMore.Margin = new Padding(0, 4, 0, 0);
        var linkBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Left, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0, 14, 0, 0),
            BackColor = Surface, WrapContents = false
        };
        linkBar.Controls.Add(readMore);
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 58, BackColor = Surface };
        bottom.Controls.Add(actions);
        actions.Dock = DockStyle.Right;
        bottom.Controls.Add(linkBar);

        // Docking runs from the back of the z-order: the notes fill what the heading, intro and buttons leave.
        dialog.Controls.Add(frame);
        dialog.Controls.Add(spacer);
        dialog.Controls.Add(introLabel);
        dialog.Controls.Add(heading);
        dialog.Controls.Add(bottom);
        if (question) dialog.ActiveControl = dialog.AcceptButton as Control;

        var chrome = 20 + 34 + introHeight + 12 + 58 + 16 + 22;
        var screen = Screen.FromPoint(Cursor.Position).WorkingArea.Height * 96 / Math.Max(96, dialog.DeviceDpi);
        int Fit(string content, float share) =>
            Math.Clamp(MeasureLogical(dialog, content, Body(), width - 48 - 20 - SystemInformation.VerticalScrollBarWidth) + 8,
                90, Math.Max(90, (int)(screen * share) - chrome));
        dialog.ClientSize = new Size(width, chrome + Fit(text, .6f));
        readMore.Click += (_, _) =>
        {
            notes.Text = full;
            notes.SelectionStart = 0;
            readMore.Visible = false;
            var height = chrome + Fit(full, .85f);
            if (height > dialog.ClientSize.Height)
            {
                dialog.ClientSize = new Size(width, height);
                if (dialog.Visible && Screen.FromControl(dialog).WorkingArea is var area && dialog.Bottom > area.Bottom)
                    dialog.Top = Math.Max(area.Top, area.Bottom - dialog.Height);
            }
            notes.Focus();
        };
        return dialog;
    }

    // Height of wrapped text at 96 DPI, the unit the dialog is laid out in.
    private static int MeasureLogical(Control control, string text, Font font, int logicalWidth) =>
        MeasureText(control, text, font, (int)(logicalWidth * control.DeviceDpi / 96f), wrap: true).Height * 96 / Math.Max(96, control.DeviceDpi);
}
