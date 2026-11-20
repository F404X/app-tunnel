using static AppTunnel.Core.Localized;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AppTunnel.Windows;

internal enum ThemeMode { System, Light, Dark }

internal static class DesktopTheme
{
    public static bool IsDark { get; private set; }

    public static bool ResolveDark(ThemeMode mode)
    {
        if (mode is ThemeMode.Light or ThemeMode.Dark) return mode == ThemeMode.Dark;
        try
        {
            return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1) is 0;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    public static void Apply(ThemeMode mode) => IsDark = ResolveDark(mode);

    // The dark palette uses Cartigma's Midnight colors.
    public static Color Background => Palette(0x0b1018, 0xf3f5f9);
    public static Color Surface => Palette(0x111827, 0xffffff);
    public static Color Raised => Palette(0x1a2434, 0xe9eef5);
    public static Color Field => Palette(0x0b1018, 0xfbfcff);
    public static Color Border => Palette(0x283549, 0xcbd4e1);
    public static Color Hover => Palette(0x233043, 0xdde6f3);
    public static Color Selection => Palette(0x1f2c3e, 0xe1ebf8);
    public static Color Text => Palette(0xebeff7, 0x182436);
    public static Color Muted => Palette(0xa5b2c5, 0x526179);
    public static Color Disabled => Palette(0x7c8ea6, 0x657389);
    public static Color Accent => Palette(0xf6ca47, 0x255ba9);
    public static Color AccentInk => Palette(0x281e0a, 0xffffff);
    public static Color AccentHover => Palette(0xfbdb75, 0x17498b);
    public static Color Green => Palette(0x98dbb6, 0x1a6344);
    public static Color GreenWash => Palette(0x1a322d, 0xd8eee4);
    public static Color Amber => Palette(0xf6ca47, 0x805000);
    public static Color AmberWash => Palette(0x342b19, 0xffedc5);

    private static Color Palette(int dark, int light) => Color.FromArgb(unchecked((int)0xff000000) | (IsDark ? dark : light));

    private static readonly FontResources Fonts = new();
    private static readonly Dictionary<(float Size, FontStyle Style, bool Display, bool Cyrillic), Font> UiFonts = new();

    public static Font Body(float size = 10, bool bold = false) => GetFont(size, bold ? FontStyle.Bold : FontStyle.Regular, false);
    public static Font Heading(float size) => GetFont(size, FontStyle.Bold, true);
    public static readonly Font Mono = new("Consolas", 10);

    private static Font GetFont(float size, FontStyle style, bool display)
    {
        var cyrillic = Language == "ru";
        var key = (size, style, display, cyrillic);
        if (UiFonts.TryGetValue(key, out var cached)) return cached;
        var family = cyrillic ? null : display ? Fonts.Display : Fonts.Body;
        var font = family is not null && family.IsStyleAvailable(style)
            ? new Font(family, size, style)
            : new Font(display && !cyrillic ? "Bahnschrift" : "Segoe UI", size, style);
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
            AccessibleDescription = value ? T("Current section", "Seção atual", "Текущий раздел") : "";
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
        return new Size(text.Width + Padding.Horizontal + (Image is null ? 2 : 25), Math.Max(MinimumSize.Height, text.Height + 12));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var canvas = Parent?.BackColor ?? DesktopTheme.Background;
        using var background = new SolidBrush(canvas);
        e.Graphics.FillRectangle(background, ClientRectangle);
        var primary = Kind == ButtonKind.Primary;
        var quiet = Kind is ButtonKind.Quiet or ButtonKind.Navigation;
        var fill = !Enabled ? quiet ? Color.Transparent : DesktopTheme.Raised :
            primary ? hovering ? DesktopTheme.AccentHover : DesktopTheme.Accent :
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
        if (Image is { } icon)
        {
            var size = Math.Max(15, DeviceDpi * 18 / 96);
            var left = DeviceDpi * 10 / 96;
            e.Graphics.DrawImage(icon, new Rectangle(left, (Height - size) / 2, size * 4 / 3, size));
            text = new Rectangle(left + size * 4 / 3 + left, 0, Math.Max(0, Width - left * 3 - size * 4 / 3), Height);
        }
        DesktopTheme.PaintText(e.Graphics, Text, Font, text, ink,
            navigational || Image is not null ? StringAlignment.Near : StringAlignment.Center);
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
        Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom;
        Margin = new Padding(0);
    }

    public void ShowStatus(string label, Color foreground, Color background)
    {
        Text = label;
        AccessibleName = label;
        tint = foreground;
        fill = background;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var background = new SolidBrush(Parent?.BackColor ?? BackColor);
        e.Graphics.FillRectangle(background, ClientRectangle);
        var padding = DeviceDpi * 10 / 96;
        var measured = TextRenderer.MeasureText(e.Graphics, Text, Font, Size.Empty,
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        var badge = new Rectangle(0, 0, Math.Min(Width, measured.Width + padding * 4), Height);
        DesktopTheme.PaintRounded(e.Graphics, badge, DeviceDpi * 4 / 96, fill);
        using var brush = new SolidBrush(tint);
        var dot = DeviceDpi * 6 / 96;
        var left = DeviceDpi * 10 / 96;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillEllipse(brush, left, (Height - dot) / 2, dot, dot);
        var inset = left + dot * 2;
        var textArea = new Rectangle(inset, 0, Math.Max(1, badge.Width - inset - padding), Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, textArea, tint,
            TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }
}
