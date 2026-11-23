using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using static AppTunnel.Windows.DesktopTheme;

namespace AppTunnel.Windows;

// A menu option with a line icon drawn in the interface's own style. Destructive options are drawn in red.
internal sealed class ThemeMenuItem : ToolStripMenuItem
{
    public ThemeMenuItem(string text, Glyph glyph = Glyph.None, EventHandler? click = null) : base(text, null, click) => Glyph = glyph;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Glyph Glyph { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Destructive { get; set; }

    protected override AccessibleObject CreateAccessibilityInstance() => new ItemAccessibility(this);

    // The shortcut column only reserves width (see ThemeMenu.FitWidth), so screen readers are not told about it.
    private sealed class ItemAccessibility(ThemeMenuItem owner) : ToolStripDropDownItemAccessibleObject(owner)
    {
        public override string KeyboardShortcut => "";

        public override AccessibleStates State =>
            owner.Checked ? base.State | AccessibleStates.Checked : base.State & ~AccessibleStates.Checked;
    }
}

// Every popup menu in AppTunnel: roomy rows with an icon each, a rounded highlight, the chosen option marked with a
// check, and rounded corners with a themed border on Windows 11. Colors are read when the menu opens, so a menu
// built before a theme change still opens in the current theme.
internal sealed class ThemeMenu : ContextMenuStrip
{
    private Size iconSize = new(18, 18);
    private int minimumWidth;

    public ThemeMenu()
    {
        Renderer = new ThemeMenuRenderer();
        Font = Medium(10.5f);
        ShowImageMargin = true;
        ShowCheckMargin = false;
        ApplyMetrics();
    }

    // Size of the icons, or of item images such as flags, at 96 DPI.
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Size IconSize
    {
        get => iconSize;
        set { iconSize = value; ApplyMetrics(); }
    }

    // Width the caller needs at least, in pixels, such as the width of the field a list opens under.
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int MinimumWidth
    {
        get => minimumWidth;
        set { minimumWidth = value; FitWidth(); }
    }

    // True once Windows 11 draws the rounded corners and border, so the renderer leaves the border to it.
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool RoundedByWindows { get; private set; }

    public ThemeMenuItem Add(string text, Glyph glyph, EventHandler? click = null)
    {
        var item = new ThemeMenuItem(text, glyph, click);
        Items.Add(item);
        return item;
    }

    public void AddSeparator() => Items.Add(new ToolStripSeparator());

    private int Px(float logical) => (int)Math.Round(logical * DeviceDpi / 96f);

    protected override void OnItemAdded(ToolStripItemEventArgs e)
    {
        base.OnItemAdded(e);
        if (e.Item is { } item) ApplyMetrics(item);
        FitEnds();
        FitWidth();
    }

    protected override void OnOpening(CancelEventArgs e)
    {
        ApplyMetrics();
        base.OnOpening(e);
    }

    // The popup's window is created, or recreated, once Windows has settled which monitor and DPI it opens on;
    // sizes are applied again then (ApplyMetrics also applies the rounded frame).
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyMetrics();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyMetrics();
    }

    private void ApplyMetrics()
    {
        BackColor = MenuBackground;
        ForeColor = DesktopTheme.Text;
        ImageScalingSize = new Size(Px(iconSize.Width), Px(iconSize.Height));
        foreach (ToolStripItem item in Items) ApplyMetrics(item);
        FitEnds();
        FitWidth();
        if (IsHandleCreated) ApplyFrame();
    }

    // The dropdown gives every row one width, computed from GDI text measurements plus its own fixed columns; it
    // ignores item padding and MinimumSize. The renderer lays rows out itself, with GDI+ text, a wider icon column
    // and a check column at the right, so the extra width is reserved through the shortcut column, which the
    // dropdown measures but the renderer never draws.
    private void FitWidth()
    {
        int gdi = 0, gdiPlus = 0, digit;
        using (var graphics = Graphics.FromHwnd(0))
        {
            foreach (ToolStripItem item in Items)
            {
                if (item is ToolStripSeparator || item.Text is not { Length: > 0 } label) continue;
                gdi = Math.Max(gdi, TextRenderer.MeasureText(label, item.Font, Size.Empty, TextFormatFlags.NoPrefix).Width);
                gdiPlus = Math.Max(gdiPlus, MeasureText(graphics, label, item.Font).Width);
            }
            digit = Math.Max(1, TextRenderer.MeasureText("00000000", Font, Size.Empty, TextFormatFlags.NoPadding).Width / 8);
        }
        var needed = Math.Max(minimumWidth - Padding.Right - 1,
            ThemeMenuRenderer.TextLeft(this) + gdiPlus + Px(ThemeMenuRenderer.CheckColumn) + ThemeMenuRenderer.Inset(this));
        // The dropdown's own row: image margin and text padding, the text, then text padding and the submenu arrow.
        var stock = ImageScalingSize.Width + Px(5) + Px(8) + gdi + Px(9) + Px(18);
        var reserve = needed - stock;
        var shortcut = reserve > 0 ? new string('0', reserve / digit + 1) : null;
        foreach (ToolStripItem item in Items)
            if (item is ToolStripMenuItem menuItem)
            {
                menuItem.ShowShortcutKeys = shortcut is not null;
                menuItem.ShortcutKeyDisplayString = shortcut;
            }
    }

    // The dropdown keeps its own padding (it folds the image margin into it), so the space above the first row and
    // below the last one comes from their margins.
    private void FitEnds()
    {
        for (var i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            var gap = item is ToolStripSeparator ? Px(3) : Px(1);
            item.Margin = new Padding(0, i == 0 ? Px(5) : gap, 0, i == Items.Count - 1 ? Px(5) : gap);
        }
    }

    private void ApplyMetrics(ToolStripItem item)
    {
        if (item is ToolStripSeparator) return;
        item.Padding = new Padding(0, Px(8), 0, Px(8));
        item.ForeColor = DesktopTheme.Text;
    }

    internal static Color MenuBackground => IsDark ? Raised : Surface;

    private void ApplyFrame()
    {
        // DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND and DWMWA_BORDER_COLOR; Windows 10 rejects both, and the
        // renderer then draws a square border itself.
        var corner = 2;
        RoundedByWindows = DwmSetWindowAttribute(Handle, 33, ref corner, sizeof(int)) == 0;
        if (!RoundedByWindows) return;
        var border = ColorTranslator.ToWin32(Border);
        _ = DwmSetWindowAttribute(Handle, 34, ref border, sizeof(int));
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}

internal sealed class ThemeMenuRenderer : ToolStripRenderer
{
    // Space at the right of each row for the check that marks the chosen option, at 96 DPI.
    public const int CheckColumn = 40;

    private static int Px(ToolStrip? strip, float logical) => (int)Math.Round(logical * (strip?.DeviceDpi ?? 96) / 96f);

    // Rows are laid out here rather than by the dropdown: icon or image at the left, then the text.
    // Rows start at the menu's left edge, so the highlight and content are inset from both edges here.
    public static int Inset(ToolStrip? strip) => Px(strip, 6);
    private static int IconLeft(ToolStrip? strip) => Inset(strip) + Px(strip, 10);
    public static int TextLeft(ToolStrip strip) => IconLeft(strip) + strip.ImageScalingSize.Width + Px(strip, 12);

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(ThemeMenu.MenuBackground);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is ThemeMenu { RoundedByWindows: true }) return;
        using var pen = new Pen(Border);
        var bounds = e.ToolStrip.ClientRectangle;
        e.Graphics.DrawRectangle(pen, 0, 0, bounds.Width - 1, bounds.Height - 1);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

    // The check is drawn at the right edge with the item background instead of in the image column.
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var inset = Inset(e.ToolStrip) + Px(e.ToolStrip, 6);
        var y = e.Item.Height / 2;
        using var pen = new Pen(Border, Math.Max(1, Px(e.ToolStrip, 1)));
        e.Graphics.DrawLine(pen, inset, y, e.Item.Width - inset, y);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var item = e.Item;
        var hot = item.Selected && item.Enabled;
        var picked = item is ToolStripMenuItem { Checked: true };
        if (!hot && !picked) return;
        var bounds = new Rectangle(Inset(e.ToolStrip), 0, Math.Max(0, item.Width - Inset(e.ToolStrip) * 2), item.Height);
        PaintRounded(e.Graphics, bounds, Px(e.ToolStrip, 6), hot ? Hover : AccentWash);
        if (!picked) return;
        var size = Px(e.ToolStrip, 16);
        PaintIcon(e.Graphics, Glyph.Check, new Rectangle(item.Width - Inset(e.ToolStrip) - Px(e.ToolStrip, 10) - size, (item.Height - size) / 2, size, size),
            item.Enabled ? Accent : Disabled, 2.6f);
    }

    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
        if (e.Image is not { } image || e.ToolStrip is not { } strip) return;
        var g = e.Graphics;
        var state = g.Save();
        try
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using var attributes = new ImageAttributes();
            if (!e.Item.Enabled) attributes.SetColorMatrix(new ColorMatrix { Matrix33 = .45f });
            var size = strip.ImageScalingSize;
            var area = new Rectangle(IconLeft(strip), (e.Item.Height - size.Height) / 2, size.Width, size.Height);
            g.DrawImage(image, FitImage(image, area), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
        }
        finally { g.Restore(state); }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var item = e.Item;
        // The shortcut column only reserves width (see ThemeMenu.FitWidth).
        if (e.ToolStrip is not { } strip || e.Text != item.Text) return;
        var destructive = item is ThemeMenuItem { Destructive: true };
        var color = !item.Enabled ? Disabled : destructive ? Danger : DesktopTheme.Text;
        if (item is ThemeMenuItem { Glyph: not Glyph.None } themed && item.Image is null)
        {
            var size = Math.Min(strip.ImageScalingSize.Height, item.Height);
            var left = IconLeft(strip) + (strip.ImageScalingSize.Width - size) / 2;
            var icon = new Rectangle(left, (item.Height - size) / 2, size, size);
            var hot = item.Selected || item is ToolStripMenuItem { Checked: true };
            var tint = !item.Enabled ? Disabled : destructive ? Danger : hot ? DesktopTheme.Text : Muted;
            PaintIcon(e.Graphics, themed.Glyph, icon, tint, 1.9f);
        }
        var textLeft = TextLeft(strip);
        var text = new Rectangle(textLeft, 0, Math.Max(0, item.Width - textLeft - Inset(strip) - Px(strip, CheckColumn)), item.Height);
        PaintText(e.Graphics, e.Text ?? "", e.TextFont ?? Medium(10.5f), text, color);
    }
}
