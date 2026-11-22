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

    // Shared setup for secondary windows: 96-DPI baseline scaling (the app is PerMonitorV2) and a title bar
    // that follows the light/dark palette like the main window.
    public static void PrepareDialog(Form form)
    {
        form.AutoScaleDimensions = new SizeF(96, 96);
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.HandleCreated += (_, _) => ApplyTitleBar(form.Handle);
        ThemeScrollbars(form);
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object> ScrollThemed = new();

    // Gives every scrolling control in the tree Windows' dark scrollbars in dark mode (Windows 10 1809+ and 11).
    // "DarkMode_Explorer" is an undocumented uxtheme class; older Windows ignores it and keeps the default look.
    // Controls added later, such as rows rebuilt after a change, are themed when they join the tree.
    public static void ThemeScrollbars(Control root)
    {
        if (!ScrollThemed.TryAdd(root, ScrollThemed)) return;
        root.ControlAdded += (_, e) => { if (e.Control is { } child) ThemeScrollbars(child); };
        if (Scrolls(root))
        {
            if (root.IsHandleCreated) ApplyScrollbarTheme(root);
            root.HandleCreated += (_, _) => ApplyScrollbarTheme(root);
        }
        foreach (Control child in root.Controls) ThemeScrollbars(child);
    }

    // A DataGridView draws its scrollbars as ScrollBar child controls, so those are themed instead of the grid.
    private static bool Scrolls(Control control) => control is TextBoxBase { Multiline: true } or ScrollBar or
        ListBox or ListView or TreeView or ScrollableControl { AutoScroll: true };

    private static void ApplyScrollbarTheme(Control control) =>
        _ = SetWindowTheme(control.Handle, IsDark ? "DarkMode_Explorer" : "Explorer", null);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SetWindowTheme(nint window, string? subAppName, string? subIdList);

    public static void ApplyTitleBar(nint window)
    {
        var enabled = IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(window, 20, ref enabled, sizeof(int));
        // Windows 11 draws the caption in the window color, so the title bar blends into the app instead of
        // showing a separate strip. Older versions ignore the attribute.
        var caption = ColorTranslator.ToWin32(Background);
        _ = DwmSetWindowAttribute(window, 35, ref caption, sizeof(int));
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    // The dark palette uses Cartigma's Midnight colors.
    public static Color Background => Palette(0x0b1018, 0xf3f5f9);
    public static Color Surface => Palette(0x111827, 0xffffff);
    public static Color Raised => Palette(0x1a2434, 0xe9eef5);
    public static Color Field => Palette(0x0b1018, 0xfbfcff);
    public static Color Border => Palette(0x283549, 0xcbd4e1);
    public static Color StrongBorder => Palette(0x3d4c63, 0x9aa8bc);
    public static Color Divider => Palette(0x1c2636, 0xe1e7ef);
    // Input fields need at least 3:1 contrast against their surroundings; decorative borders stay softer.
    public static Color InputBorder => Palette(0x56698a, 0x8592a6);
    public static Color Hover => Palette(0x233043, 0xdde6f3);
    public static Color Selection => Palette(0x1f2c3e, 0xe1ebf8);
    public static Color Text => Palette(0xebeff7, 0x182436);
    public static Color Muted => Palette(0xa5b2c5, 0x526179);
    public static Color Disabled => Palette(0x7c8ea6, 0x657389);
    public static Color Accent => Palette(0xf6ca47, 0x255ba9);
    public static Color AccentInk => Palette(0x281e0a, 0xffffff);
    public static Color AccentHover => Palette(0xfbdb75, 0x17498b);
    // Tinted surface behind accent content: selected cards, enabled app icons and the connected power ring.
    public static Color AccentWash => Palette(0x342b19, 0xe3ecf9);
    public static Color Green => Palette(0x98dbb6, 0x1a6344);
    public static Color GreenWash => Palette(0x1a322d, 0xd8eee4);
    public static Color Amber => Palette(0xf6ca47, 0x805000);
    public static Color AmberWash => Palette(0x342b19, 0xffedc5);
    public static Color Danger => Palette(0xf59e8b, 0xa63a24);
    public static Color DangerWash => Palette(0x3a1f1c, 0xfbe3dd);

    private static Color Palette(int dark, int light) => Color.FromArgb(unchecked((int)0xff000000) | (IsDark ? dark : light));

    private static readonly FontResources Fonts = new();
    private static readonly Dictionary<(float Size, int Weight, bool Display, bool Cyrillic), Font> UiFonts = new();

    // Sizes are in points: the design's 14 px body text is 10.5 pt at 96 DPI.
    public static Font Body(float size = 10.5f, bool bold = false) => GetFont(size, bold ? 700 : 400, false);
    public static Font Medium(float size = 10.5f) => GetFont(size, 500, false);
    public static Font Heading(float size) => GetFont(size, 700, true);
    public static readonly Font Mono = new("Consolas", 10);

    private static Font GetFont(float size, int weight, bool display)
    {
        var cyrillic = Language == "ru";
        var key = (size, weight, display, cyrillic);
        if (UiFonts.TryGetValue(key, out var cached)) return cached;
        var style = weight >= 700 ? FontStyle.Bold : FontStyle.Regular;
        var family = cyrillic ? null : display ? Fonts.Display : weight == 500 ? Fonts.Medium ?? Fonts.Body : Fonts.Body;
        // The bundled fonts have no Cyrillic, so Russian uses Segoe UI with the closest weights.
        var font = family is not null && family.IsStyleAvailable(style)
            ? new Font(family, size, style)
            : new Font(weight == 500 ? "Segoe UI Semibold" : display && !cyrillic ? "Bahnschrift" : "Segoe UI", size,
                weight == 500 ? FontStyle.Regular : style);
        UiFonts.Add(key, font);
        return font;
    }

    public static float DpiScale(Control control) => control.DeviceDpi / 96f;

    public static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var size = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
        if (size <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }
        path.AddArc(bounds.Left, bounds.Top, size, size, 180, 90);
        path.AddArc(bounds.Right - size, bounds.Top, size, size, 270, 90);
        path.AddArc(bounds.Right - size, bounds.Bottom - size, size, size, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - size, size, size, 90, 90);
        path.CloseFigure();
        return path;
    }

    // Shapes are filled with anti-aliasing and half-pixel offsets so 1 px borders land on whole pixels.
    public static void PaintRounded(Graphics graphics, Rectangle bounds, float radius, Color fill, Color? edge = null,
        float edgeWidth = 1, DashStyle dash = DashStyle.Solid)
    {
        if (bounds.Width < 2 || bounds.Height < 2) return;
        var state = graphics.Save();
        try
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            var inset = edge.HasValue ? edgeWidth / 2 : 0;
            using var shape = Rounded(new RectangleF(bounds.X + inset, bounds.Y + inset,
                bounds.Width - inset * 2, bounds.Height - inset * 2), radius);
            if (fill.A > 0)
            {
                using var brush = new SolidBrush(fill);
                graphics.FillPath(brush, shape);
            }
            if (edge is { } color)
            {
                using var pen = new Pen(color, edgeWidth) { DashStyle = dash };
                graphics.DrawPath(pen, shape);
            }
        }
        finally { graphics.Restore(state); }
    }

    public static void PaintCircle(Graphics graphics, RectangleF bounds, Color fill, Color? edge = null, float edgeWidth = 1)
    {
        var state = graphics.Save();
        try
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            if (fill.A > 0)
            {
                using var brush = new SolidBrush(fill);
                graphics.FillEllipse(brush, bounds);
            }
            if (edge is { } color)
            {
                using var pen = new Pen(color, edgeWidth);
                var inset = edgeWidth / 2;
                graphics.DrawEllipse(pen, bounds.X + inset, bounds.Y + inset, bounds.Width - edgeWidth, bounds.Height - edgeWidth);
            }
        }
        finally { graphics.Restore(state); }
    }

    // All interface text goes through here. The bundled fonts are auto-hinted, so grid fitting keeps small text
    // crisp while grayscale anti-aliasing keeps the curves smooth; large headings skip grid fitting entirely so
    // their outlines render at their designed shapes. Grayscale also avoids ClearType's color fringes on the
    // dark palette and on surfaces that are not opaque pixels on screen.
    public static TextRenderingHint HintFor(Font font) =>
        font.SizeInPoints >= 15 ? TextRenderingHint.AntiAlias : TextRenderingHint.AntiAliasGridFit;

    public static void PaintText(Graphics graphics, string text, Font font, Rectangle bounds, Color color,
        StringAlignment alignment = StringAlignment.Near, StringAlignment line = StringAlignment.Center, bool wrap = false)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || text.Length == 0) return;
        var state = graphics.Save();
        try
        {
            graphics.SetClip(bounds, CombineMode.Intersect);
            graphics.TextRenderingHint = HintFor(font);
            graphics.TextContrast = 4;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            using var format = MakeFormat(alignment, line, wrap);
            using var brush = new SolidBrush(color);
            graphics.DrawString(text, font, brush, bounds, format);
        }
        finally { graphics.Restore(state); }
    }

    public static Size MeasureText(Graphics graphics, string text, Font font, int width = int.MaxValue, bool wrap = false)
    {
        if (text.Length == 0) return new Size(0, (int)Math.Ceiling(font.GetHeight(graphics)));
        graphics.TextRenderingHint = HintFor(font);
        using var format = MakeFormat(StringAlignment.Near, StringAlignment.Near, wrap);
        var size = graphics.MeasureString(text, font, new SizeF(width, 100_000), format);
        return new Size((int)Math.Ceiling(size.Width) + 1, (int)Math.Ceiling(size.Height));
    }

    // Line height at the control's monitor DPI; Font.Height always measures at the primary screen's DPI.
    public static int LineHeight(this Font font, Control control) => (int)Math.Ceiling(font.GetHeight(control.DeviceDpi));

    public static Size MeasureText(Control control, string text, Font font, int width = int.MaxValue, bool wrap = false)
    {
        using var graphics = control.IsHandleCreated ? control.CreateGraphics() : Graphics.FromHwnd(0);
        return MeasureText(graphics, text, font, width, wrap);
    }

    private static StringFormat MakeFormat(StringAlignment alignment, StringAlignment line, bool wrap)
    {
        // Typographic layout keeps the glyph advances the font designed. The default layout adds padding and,
        // with grid fitting, stretches the spacing of lines that get trimmed.
        var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = alignment, LineAlignment = line,
            Trimming = wrap ? StringTrimming.EllipsisWord : StringTrimming.EllipsisCharacter
        };
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | (wrap ? 0 : StringFormatFlags.NoWrap);
        format.HotkeyPrefix = HotkeyPrefix.None;
        return format;
    }

    public static void PaintCheck(Graphics graphics, Rectangle box, bool picked, bool enabled)
    {
        var radius = box.Width * .27f;
        if (picked) PaintRounded(graphics, box, radius, enabled ? Accent : Border);
        else PaintRounded(graphics, box, radius, Color.Transparent, enabled ? InputBorder : Border, Math.Max(1.3f, box.Width / 14f));
        if (!picked) return;
        PaintIcon(graphics, Glyph.Check, Rectangle.Inflate(box, -box.Width / 7, -box.Height / 7), enabled ? AccentInk : Disabled, 3f);
    }

    public static void PaintIcon(Graphics graphics, Glyph glyph, RectangleF bounds, Color color, float stroke = 2f)
    {
        var state = graphics.Save();
        try
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            var unit = Math.Min(bounds.Width, bounds.Height) / 24f;
            graphics.TranslateTransform(bounds.X + (bounds.Width - unit * 24) / 2, bounds.Y + (bounds.Height - unit * 24) / 2);
            graphics.ScaleTransform(unit, unit);
            using var pen = new Pen(color, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            switch (glyph)
            {
                case Glyph.Power:
                    graphics.DrawLine(pen, 12, 3, 12, 12);
                    graphics.DrawArc(pen, 4, 4, 16, 16, -47, 274);
                    break;
                case Glyph.Apps:
                    foreach (var (x, y) in new[] { (4f, 4f), (13f, 4f), (4f, 13f), (13f, 13f) })
                    {
                        using var tile = Rounded(new RectangleF(x, y, 7, 7), 2);
                        graphics.DrawPath(pen, tile);
                    }
                    break;
                case Glyph.Globe:
                    graphics.DrawEllipse(pen, 3.5f, 3.5f, 17, 17);
                    graphics.DrawLine(pen, 3.5f, 12, 20.5f, 12);
                    using (var meridian = new GraphicsPath())
                    {
                        meridian.AddBezier(12, 3.5f, 14.5f, 6.1f, 15.6f, 8.9f, 15.6f, 12);
                        meridian.AddBezier(15.6f, 12, 15.6f, 15.1f, 14.5f, 17.9f, 12, 20.5f);
                        meridian.AddBezier(12, 20.5f, 9.5f, 17.9f, 8.4f, 15.1f, 8.4f, 12);
                        meridian.AddBezier(8.4f, 12, 8.4f, 8.9f, 9.5f, 6.1f, 12, 3.5f);
                        graphics.DrawPath(pen, meridian);
                    }
                    break;
                case Glyph.Sliders:
                    graphics.DrawLine(pen, 4, 7, 14, 7);
                    graphics.DrawLine(pen, 18, 7, 20, 7);
                    graphics.DrawLine(pen, 4, 17, 8, 17);
                    graphics.DrawLine(pen, 12, 17, 20, 17);
                    graphics.DrawEllipse(pen, 14, 5, 4, 4);
                    graphics.DrawEllipse(pen, 8, 15, 4, 4);
                    break;
                case Glyph.Pulse:
                    graphics.DrawLines(pen, new PointF[] { new(3, 12), new(7, 12), new(9.5f, 6), new(14, 18), new(16.5f, 12), new(21, 12) });
                    break;
                case Glyph.Plus:
                    graphics.DrawLine(pen, 12, 5, 12, 19);
                    graphics.DrawLine(pen, 5, 12, 19, 12);
                    break;
                case Glyph.Check:
                    graphics.DrawLines(pen, new PointF[] { new(5, 12.5f), new(9.5f, 17), new(19, 7.5f) });
                    break;
                case Glyph.Close:
                    graphics.DrawLine(pen, 6.5f, 6.5f, 17.5f, 17.5f);
                    graphics.DrawLine(pen, 17.5f, 6.5f, 6.5f, 17.5f);
                    break;
                case Glyph.Alert:
                    graphics.DrawLine(pen, 12, 6.5f, 12, 13);
                    graphics.DrawLine(pen, 12, 17.3f, 12, 17.5f);
                    break;
                case Glyph.Shield:
                    graphics.DrawPolygon(pen, new PointF[] { new(12, 3), new(19, 6), new(19, 11), new(17.5f, 16.5f), new(12, 21), new(6.5f, 16.5f), new(5, 11), new(5, 6) });
                    break;
                case Glyph.More:
                    using (var dot = new SolidBrush(color))
                        foreach (var x in new[] { 5.5f, 12f, 18.5f }) graphics.FillEllipse(dot, x - 1.6f, 10.4f, 3.2f, 3.2f);
                    break;
                case Glyph.Chevron:
                    graphics.DrawLines(pen, new PointF[] { new(7, 10), new(12, 15), new(17, 10) });
                    break;
                case Glyph.Tunnel:
                    graphics.DrawBezier(pen, 4, 18, 4, 11, 8, 6, 12, 6);
                    graphics.DrawBezier(pen, 12, 6, 16, 6, 20, 11, 20, 18);
                    graphics.DrawBezier(pen, 9, 18, 9, 14.5f, 10.4f, 12, 12, 12);
                    graphics.DrawBezier(pen, 12, 12, 13.6f, 12, 15, 14.5f, 15, 18);
                    break;
            }
        }
        finally { graphics.Restore(state); }
    }

    private sealed class FontResources
    {
        private readonly PrivateFontCollection collection = new();
        // GDI+ holds the memory of private fonts for the lifetime of the process.
        private readonly List<nint> memory = [];
        private readonly List<nint> resources = [];
        public FontFamily? Body { get; }
        public FontFamily? Medium { get; }
        public FontFamily? Display { get; }

        public FontResources()
        {
            Register("AppTunnelSans-Regular.ttf");
            Register("AppTunnelSans-Bold.ttf");
            Register("AppTunnelSans-Medium.ttf");
            Register("AppTunnelDisplay-Bold.ttf");
            var families = collection.Families;
            Body = Array.Find(families, family => family.Name == "AppTunnel Sans");
            Medium = Array.Find(families, family => family.Name == "AppTunnel Sans Medium");
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

internal enum Glyph { None, Power, Apps, Globe, Sliders, Pulse, Plus, Check, Close, Alert, Shield, More, Chevron, Tunnel }

internal enum ButtonKind { Secondary, Primary, Quiet, Navigation, Link }

internal sealed class ThemeButton : Button
{
    private bool hovering;
    private bool pressed;
    private bool selected;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ButtonKind Kind { get; init; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Glyph Glyph { get; set; }

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
        Font = DesktopTheme.Medium();
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = new Size(0, 40);
        Padding = new Padding(16, 0, 16, 0);
        Margin = new Padding(0, 2, 8, 2);
        Cursor = Cursors.Hand;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = DesktopTheme.MeasureText(this, Text, Font);
        var extra = Image is not null ? IconSize * 4 / 3 + IconGap : Glyph != Glyph.None ? IconSize + IconGap : 0;
        return new Size(text.Width + Padding.Horizontal + extra, Math.Max(MinimumSize.Height, text.Height + DeviceDpi * 14 / 96));
    }

    private int IconSize => Kind == ButtonKind.Navigation ? DeviceDpi * 18 / 96 : DeviceDpi * 16 / 96;
    private int IconGap => DeviceDpi * 10 / 96;

    protected override void OnPaint(PaintEventArgs e)
    {
        var canvas = Parent?.BackColor ?? DesktopTheme.Background;
        using var background = new SolidBrush(canvas);
        e.Graphics.FillRectangle(background, ClientRectangle);
        var primary = Kind == ButtonKind.Primary;
        var link = Kind == ButtonKind.Link;
        var quiet = Kind is ButtonKind.Quiet or ButtonKind.Navigation or ButtonKind.Link;
        var fill = link ? Color.Transparent : !Enabled ? quiet ? Color.Transparent : DesktopTheme.Raised :
            primary ? hovering ? DesktopTheme.AccentHover : DesktopTheme.Accent :
            pressed || Selected ? DesktopTheme.Selection :
            hovering ? DesktopTheme.Hover : quiet ? Color.Transparent : DesktopTheme.Raised;
        var ink = !Enabled ? DesktopTheme.Disabled : primary ? DesktopTheme.AccentInk :
            link ? hovering ? DesktopTheme.AccentHover : DesktopTheme.Accent :
            Selected ? DesktopTheme.Text : quiet && !hovering ? DesktopTheme.Muted : DesktopTheme.Text;
        var radius = DeviceDpi * (Kind == ButtonKind.Navigation ? 8 : 10) / 96f;
        var edge = !quiet && !primary ? DesktopTheme.Border : (Color?)null;
        if (fill.A > 0 || edge.HasValue)
            DesktopTheme.PaintRounded(e.Graphics, ClientRectangle, radius, fill, edge);
        if (Focused && ShowFocusCues && Enabled)
        {
            // Inset ring in a color that contrasts with the fill: accent ink on primary, accent elsewhere.
            var inset = Math.Max(2, DeviceDpi * 3 / 96);
            DesktopTheme.PaintRounded(e.Graphics, Rectangle.Inflate(ClientRectangle, -inset, -inset), Math.Max(2, radius - inset),
                Color.Transparent, primary ? DesktopTheme.AccentInk : DesktopTheme.Accent, Math.Max(1.5f, DeviceDpi * 2f / 96));
        }
        var navigational = Kind == ButtonKind.Navigation;
        var font = navigational && Selected ? DesktopTheme.Body(Font.SizeInPoints, bold: true) : Font;
        var alignLeft = navigational || Kind == ButtonKind.Link && Glyph != Glyph.None;
        var text = new Rectangle(Padding.Left, 0, Math.Max(0, Width - Padding.Horizontal), Height);
        if (Image is { } icon)
        {
            var size = IconSize;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(icon, new Rectangle(Padding.Left, (Height - size) / 2, size * 4 / 3, size));
            text = new Rectangle(Padding.Left + size * 4 / 3 + IconGap, 0, Math.Max(0, Width - Padding.Horizontal - size * 4 / 3 - IconGap), Height);
            alignLeft = true;
        }
        else if (Glyph != Glyph.None)
        {
            var size = IconSize;
            var glyphInk = navigational && Selected && Enabled ? DesktopTheme.Accent : ink;
            if (alignLeft)
            {
                DesktopTheme.PaintIcon(e.Graphics, Glyph, new Rectangle(Padding.Left, (Height - size) / 2, size, size), glyphInk,
                    primary ? 2.6f : 2f);
                text = new Rectangle(Padding.Left + size + IconGap, 0, Math.Max(0, Width - Padding.Horizontal - size - IconGap), Height);
            }
            else
            {
                var width = DesktopTheme.MeasureText(e.Graphics, Text, font).Width;
                var start = Math.Max(Padding.Left, (Width - width - size - IconGap) / 2);
                DesktopTheme.PaintIcon(e.Graphics, Glyph, new Rectangle(start, (Height - size) / 2, size, size), glyphInk,
                    primary ? 2.6f : 2.2f);
                text = new Rectangle(start + size + IconGap, 0, Math.Max(0, Width - start - size - IconGap), Height);
                alignLeft = true;
            }
        }
        DesktopTheme.PaintText(e.Graphics, Text, font, text, ink, alignLeft ? StringAlignment.Near : StringAlignment.Center);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovering = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovering = false; pressed = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); pressed = true; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
}

// A rounded panel. Its corners show the parent's color, so cards sit on the page without square edges.
internal class ThemeCard : Panel
{
    private Color? edge;
    private float radius = 14;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color? Edge
    {
        get => edge;
        set { edge = value; Invalidate(); }
    }

    // Corner radius in 96-DPI pixels.
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float Radius
    {
        get => radius;
        set { radius = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Dashed { get; set; }

    public ThemeCard(Color background)
    {
        BackColor = background;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var canvas = new SolidBrush(Parent?.BackColor ?? DesktopTheme.Background);
        e.Graphics.FillRectangle(canvas, ClientRectangle);
        var scale = DesktopTheme.DpiScale(this);
        DesktopTheme.PaintRounded(e.Graphics, ClientRectangle, radius * scale, Dashed ? Parent?.BackColor ?? BackColor : BackColor,
            edge ?? DesktopTheme.Border, Math.Max(1, scale * (Dashed ? 1.5f : 1)), Dashed ? DashStyle.Dash : DashStyle.Solid);
    }
}

// A label that draws through DesktopTheme.PaintText, so every caption shares the same smoothing.
internal sealed class SmoothLabel : Label
{
    public SmoothLabel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width is > 1 and < int.MaxValue ? proposedSize.Width - Padding.Horizontal : int.MaxValue;
        var size = DesktopTheme.MeasureText(this, Text, Font, width, wrap: !AutoEllipsis && width != int.MaxValue);
        return new Size(size.Width + Padding.Horizontal, size.Height + Padding.Vertical);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var fill = BackColor.A == 255 ? BackColor : Parent?.BackColor ?? DesktopTheme.Background;
        using var background = new SolidBrush(fill);
        e.Graphics.FillRectangle(background, ClientRectangle);
        var bounds = new Rectangle(Padding.Left, Padding.Top, Width - Padding.Horizontal, Height - Padding.Vertical);
        var (alignment, line) = TextAlign switch
        {
            ContentAlignment.TopLeft => (StringAlignment.Near, StringAlignment.Near),
            ContentAlignment.TopCenter => (StringAlignment.Center, StringAlignment.Near),
            ContentAlignment.TopRight => (StringAlignment.Far, StringAlignment.Near),
            ContentAlignment.MiddleCenter => (StringAlignment.Center, StringAlignment.Center),
            ContentAlignment.MiddleRight => (StringAlignment.Far, StringAlignment.Center),
            ContentAlignment.BottomLeft => (StringAlignment.Near, StringAlignment.Far),
            ContentAlignment.BottomCenter => (StringAlignment.Center, StringAlignment.Far),
            ContentAlignment.BottomRight => (StringAlignment.Far, StringAlignment.Far),
            _ => (StringAlignment.Near, StringAlignment.Center)
        };
        DesktopTheme.PaintText(e.Graphics, Text, Font, bounds, Enabled ? ForeColor : DesktopTheme.Disabled, alignment, line,
            wrap: !AutoEllipsis);
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
        var side = DeviceDpi * 18 / 96;
        var box = new Rectangle(1, (Height - side) / 2, side, side);
        DesktopTheme.PaintCheck(e.Graphics, box, Checked, Enabled);
        var left = box.Right + DeviceDpi * 10 / 96;
        var caption = new Rectangle(left, 0, Math.Max(0, Width - left), Height);
        DesktopTheme.PaintText(e.Graphics, Text, Font, caption, Enabled ? DesktopTheme.Text : DesktopTheme.Disabled);
        if (Focused && ShowFocusCues)
        {
            var length = Math.Min(caption.Width, DesktopTheme.MeasureText(e.Graphics, Text, Font).Width);
            ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(left, (Height - Font.Height) / 2, length, Font.Height));
        }
    }

    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
}

// Menu colors read the live palette, so open menus follow the current light/dark theme.
internal sealed class ThemeMenuColors : ProfessionalColorTable
{
    public override Color MenuBorder => DesktopTheme.Border;
    public override Color MenuItemBorder => DesktopTheme.Hover;
    public override Color MenuItemSelected => DesktopTheme.Hover;
    public override Color MenuItemSelectedGradientBegin => DesktopTheme.Hover;
    public override Color MenuItemSelectedGradientEnd => DesktopTheme.Hover;
    public override Color ToolStripDropDownBackground => DesktopTheme.Raised;
    public override Color ImageMarginGradientBegin => DesktopTheme.Raised;
    public override Color ImageMarginGradientMiddle => DesktopTheme.Raised;
    public override Color ImageMarginGradientEnd => DesktopTheme.Raised;
    public override Color SeparatorDark => DesktopTheme.Border;
    public override Color SeparatorLight => DesktopTheme.Raised;
    public override Color CheckBackground => DesktopTheme.Accent;
    public override Color CheckSelectedBackground => DesktopTheme.Accent;
}

// Menu text uses the same smoothing as the rest of the interface.
internal sealed class ThemeMenuRenderer() : ToolStripProfessionalRenderer(new ThemeMenuColors())
{
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var color = e.Item.Enabled ? DesktopTheme.Text : DesktopTheme.Disabled;
        DesktopTheme.PaintText(e.Graphics, e.Text ?? "", e.TextFont ?? DesktopTheme.Body(), e.TextRectangle, color);
    }
}
