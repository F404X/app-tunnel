using static AppTunnel.Core.Localized;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;

namespace AppTunnel.Windows;

internal static class DesktopTheme
{
    // The Midnight tokens are from Cartigma's feature/ranking theme palette.
    public static readonly Color Background = Color.FromArgb(11, 16, 24);
    public static readonly Color Surface = Color.FromArgb(17, 24, 39);
    public static readonly Color Raised = Color.FromArgb(26, 36, 52);
    public static readonly Color Field = Background;
    public static readonly Color Border = Color.FromArgb(40, 53, 73);
    public static readonly Color Hover = Color.FromArgb(35, 48, 67);
    public static readonly Color Selection = Color.FromArgb(31, 44, 62);
    public static readonly Color Text = Color.FromArgb(235, 239, 247);
    public static readonly Color Muted = Color.FromArgb(165, 178, 197);
    public static readonly Color Disabled = Color.FromArgb(124, 142, 166);
    public static readonly Color Accent = Color.FromArgb(246, 202, 71);
    public static readonly Color AccentInk = Color.FromArgb(40, 30, 10);
    public static readonly Color Green = Color.FromArgb(152, 219, 182);
    public static readonly Color GreenWash = Color.FromArgb(26, 50, 45);
    public static readonly Color Amber = Color.FromArgb(246, 202, 71);
    public static readonly Color AmberWash = Color.FromArgb(52, 43, 25);

    private static readonly FontResources Fonts = new();
    private static readonly Dictionary<(float Size, FontStyle Style, bool Display), Font> UiFonts = new();

    public static Font Body(float size = 10, bool bold = false) => GetFont(size, bold ? FontStyle.Bold : FontStyle.Regular, false);
    public static Font Heading(float size) => GetFont(size, FontStyle.Bold, true);
    public static readonly Font Mono = new("Consolas", 10);

    private static Font GetFont(float size, FontStyle style, bool display)
    {
        var key = (size, style, display);
        if (UiFonts.TryGetValue(key, out var cached)) return cached;
        var family = display ? Fonts.Display : Fonts.Body;
        var font = family is not null && family.IsStyleAvailable(style)
            ? new Font(family, size, style)
            : new Font(display ? "Bahnschrift" : "Segoe UI", size, style);
        UiFonts.Add(key, font);
        return font;
    }

    public static GraphicsPath Rounded(RectangleF bounds, int radius)
    {
        var path = new GraphicsPath();
        var size = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
        if (size <= 0) return path;
        path.AddArc(bounds.Left, bounds.Top, size, size, 180, 90);
        path.AddArc(bounds.Right - size, bounds.Top, size, size, 270, 90);
        path.AddArc(bounds.Right - size, bounds.Bottom - size, size, size, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - size, size, size, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void PaintRounded(Graphics graphics, Rectangle bounds, int radius, Color fill, Color? edge = null)
    {
        if (bounds.Width < 2 || bounds.Height < 2) return;
        var previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = Rounded(new RectangleF(bounds.X + .5f, bounds.Y + .5f, bounds.Width - 1, bounds.Height - 1), radius);
        using var brush = new SolidBrush(fill);
        graphics.FillPath(brush, shape);
        if (edge is { } color)
        {
            using var pen = new Pen(color);
            graphics.DrawPath(pen, shape);
        }
        graphics.SmoothingMode = previous;
    }

    public static void PaintText(Graphics graphics, string text, Font font, Rectangle bounds, Color color,
        StringAlignment alignment = StringAlignment.Near)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var state = graphics.Save();
        try
        {
            graphics.SetClip(bounds, CombineMode.Intersect);
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using var format = new StringFormat
            {
                Alignment = alignment, LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap
            };
            using var brush = new SolidBrush(color);
            graphics.DrawString(text, font, brush, bounds, format);
        }
        finally { graphics.Restore(state); }
    }

    public static void PaintCheck(Graphics graphics, Rectangle box, bool picked, bool enabled)
    {
        PaintRounded(graphics, box, 4, picked ? enabled ? Accent : Border : Field, enabled ? Muted : Border);
        if (!picked) return;
        var previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(enabled ? AccentInk : Disabled, Math.Max(1.6f, box.Width * .115f))
            { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        graphics.DrawLines(pen, new PointF[] {
            new(box.X + box.Width * .26f, box.Y + box.Height * .52f),
            new(box.X + box.Width * .44f, box.Y + box.Height * .70f),
            new(box.X + box.Width * .75f, box.Y + box.Height * .34f)
        });
        graphics.SmoothingMode = previous;
    }

    private sealed class FontResources
    {
        private readonly PrivateFontCollection collection = new();
        // GDI+ holds the memory of private fonts for the lifetime of the process.
        private readonly List<nint> memory = [];
        private readonly List<nint> resources = [];
        public FontFamily? Body { get; }
        public FontFamily? Display { get; }

        public FontResources()
        {
            Register("AppTunnelSans-Regular.ttf");
            Register("AppTunnelSans-Bold.ttf");
            Register("AppTunnelDisplay-Bold.ttf");
            var families = collection.Families;
            Body = Array.Find(families, family => family.Name == "AppTunnel Sans");
            Display = Array.Find(families, family => family.Name == "AppTunnel Display");
        }

        private void Register(string name)
        {
            using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream($"AppTunnel.Fonts.{name}");
            if (source is null) return;
            var bytes = new byte[checked((int)source.Length)];
            source.ReadExactly(bytes);
            var pointer = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            try
            {
                collection.AddMemoryFont(pointer, bytes.Length);
                memory.Add(pointer);
                uint fonts = 0;
                var handle = AddFontMemResourceEx(pointer, (uint)bytes.Length, 0, ref fonts);
                if (handle != 0) resources.Add(handle);
            }
            catch (ArgumentException)
            {
                Marshal.FreeHGlobal(pointer);
            }
        }

        [DllImport("gdi32.dll", ExactSpelling = true)]
        private static extern nint AddFontMemResourceEx(nint font, uint size, nint reserved, ref uint count);
    }
}

internal enum ButtonKind { Secondary, Primary, Quiet, Navigation }

internal sealed class ThemeButton : Button
{
    private bool hovering;
    private bool pressed;
    private bool selected;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ButtonKind Kind { get; init; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => selected;
        set
        {
            if (selected == value) return;
            selected = value;
            AccessibleDescription = value ? T("Current section", "Seção atual") : "";
            Invalidate();
        }
    }

    public ThemeButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        BackColor = DesktopTheme.Background;
        Font = DesktopTheme.Body(bold: true);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = new Size(0, 36);
        Padding = new Padding(13, 0, 13, 0);
        Margin = new Padding(0, 2, 7, 2);
        Cursor = Cursors.Hand;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        return new Size(text.Width + Padding.Horizontal + 2, Math.Max(MinimumSize.Height, text.Height + 12));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var canvas = Parent?.BackColor ?? DesktopTheme.Background;
        using var background = new SolidBrush(canvas);
        e.Graphics.FillRectangle(background, ClientRectangle);
        var primary = Kind == ButtonKind.Primary;
        var quiet = Kind is ButtonKind.Quiet or ButtonKind.Navigation;
        var fill = !Enabled ? quiet ? Color.Transparent : DesktopTheme.Raised :
            primary ? hovering ? Color.FromArgb(251, 219, 117) : DesktopTheme.Accent :
            pressed || Selected ? DesktopTheme.Selection :
            hovering ? DesktopTheme.Hover : quiet ? Color.Transparent : DesktopTheme.Raised;
        var ink = !Enabled ? DesktopTheme.Disabled : primary ? DesktopTheme.AccentInk :
            Selected ? DesktopTheme.Accent : quiet && !hovering ? DesktopTheme.Muted : DesktopTheme.Text;
        var edge = Focused && ShowFocusCues ? DesktopTheme.Accent :
            !quiet && !primary ? DesktopTheme.Border : (Color?)null;
        if (fill.A > 0 || edge.HasValue)
            DesktopTheme.PaintRounded(e.Graphics, ClientRectangle, Math.Max(3, DeviceDpi * 4 / 96), fill, edge);
        var navigational = Kind == ButtonKind.Navigation;
        if (Selected && navigational)
        {
            using var active = new SolidBrush(DesktopTheme.Accent);
            e.Graphics.FillRectangle(active, 0, 0, Math.Max(2, DeviceDpi * 3 / 96), Height);
        }
        var text = navigational ? new Rectangle(Padding.Left, 0, Math.Max(0, Width - Padding.Horizontal), Height)
            : ClientRectangle;
        DesktopTheme.PaintText(e.Graphics, Text, Font, text, ink,
            navigational ? StringAlignment.Near : StringAlignment.Center);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovering = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovering = false; pressed = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); pressed = true; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
}

internal sealed class ThemeCard : Panel
{
    public ThemeCard(Color background)
    {
        BackColor = background;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width < 2 || Height < 2) return;
        using var border = new Pen(DesktopTheme.Border);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }
}

internal sealed class ThemeCheckBox : CheckBox
{
    public ThemeCheckBox() => SetStyle(ControlStyles.UserPaint | ControlStyles.Opaque |
        ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

    protected override void OnPaint(PaintEventArgs e)
    {
        using var background = new SolidBrush(Parent?.BackColor ?? BackColor);
        e.Graphics.FillRectangle(background, ClientRectangle);
        var side = DeviceDpi * 16 / 96;
        var box = new Rectangle(1, (Height - side) / 2, side, side);
        DesktopTheme.PaintCheck(e.Graphics, box, Checked, Enabled);
        var left = box.Right + DeviceDpi * 9 / 96;
        var caption = new Rectangle(left, 0, Math.Max(0, Width - left), Height);
        DesktopTheme.PaintText(e.Graphics, Text, Font, caption, Enabled ? DesktopTheme.Text : DesktopTheme.Disabled);
        if (Focused && ShowFocusCues)
        {
            var length = Math.Min(caption.Width, TextRenderer.MeasureText(Text, Font).Width);
            ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(left, (Height - Font.Height) / 2, length, Font.Height));
        }
    }

    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
}

internal sealed class StatusPill : Label
{
    private Color tint = DesktopTheme.Muted;
    private Color fill = DesktopTheme.Raised;

    public StatusPill()
    {
        Font = DesktopTheme.Body(9, bold: true);
        SetStyle(ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer, true);
        AutoSize = false;
        Margin = new Padding(0);
    }

    public void ShowStatus(string label, Color foreground, Color background)
    {
        Text = label;
        AccessibleName = label;
        tint = foreground;
        fill = background;
        var text = TextRenderer.MeasureText(label, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        Size = new Size(text.Width + DeviceDpi * 31 / 96, DeviceDpi * 24 / 96);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var background = new SolidBrush(Parent?.BackColor ?? BackColor);
        e.Graphics.FillRectangle(background, ClientRectangle);
        DesktopTheme.PaintRounded(e.Graphics, ClientRectangle, DeviceDpi * 4 / 96, fill);
        using var brush = new SolidBrush(tint);
        var dot = DeviceDpi * 6 / 96;
        var left = DeviceDpi * 10 / 96;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillEllipse(brush, left, (Height - dot) / 2, dot, dot);
        var inset = left + dot * 2;
        DesktopTheme.PaintText(e.Graphics, Text, Font, new Rectangle(inset, 0, Width - inset - 5, Height), tint);
    }
}
