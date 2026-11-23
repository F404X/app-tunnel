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

    // Read-only text that wraps (never a horizontal bar) and shows a vertical bar only when the text does not fit.
    public static RichTextBox ReadOnlyText(string text, Font font) => WithEditMenu(new RichTextBox
    {
        Text = text, ReadOnly = true, WordWrap = true, ScrollBars = RichTextBoxScrollBars.Vertical, DetectUrls = false,
        BorderStyle = BorderStyle.None, BackColor = Field, ForeColor = Text, Font = font, Dock = DockStyle.Fill,
        Margin = new Padding(0), ShortcutsEnabled = true, TabStop = true
    });

    // The native edit menu of a text field follows the Windows language; this one follows the AppTunnel language.
    public static TBox WithEditMenu<TBox>(TBox box) where TBox : TextBoxBase
    {
        var menu = new ThemeMenu();
        var cut = menu.Add(AppTunnel.Core.Localized.T("Cut", "Recortar", "Вырезать"), Glyph.Scissors, (_, _) => box.Cut());
        var copy = menu.Add(AppTunnel.Core.Localized.T("Copy", "Copiar", "Копировать"), Glyph.Copy, (_, _) => box.Copy());
        var paste = menu.Add(AppTunnel.Core.Localized.T("Paste", "Colar", "Вставить"), Glyph.Clipboard, (_, _) => box.Paste());
        menu.AddSeparator();
        var all = menu.Add(AppTunnel.Core.Localized.T("Select all", "Selecionar tudo", "Выделить всё"), Glyph.SelectAll, (_, _) => box.SelectAll());
        menu.Opening += (_, _) =>
        {
            cut.Enabled = !box.ReadOnly && box.SelectionLength > 0;
            copy.Enabled = box.SelectionLength > 0;
            paste.Enabled = !box.ReadOnly && Clipboard.ContainsText();
            all.Enabled = box.TextLength > 0;
        };
        box.ContextMenuStrip = menu;
        box.Disposed += (_, _) => menu.Dispose();
        return box;
    }

    // Grows a dialog so its text fits without scrolling, up to most of the screen it opens on.
    public static void FitToText(Form dialog, string text, Font font, int textWidth, int chrome)
    {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        using var graphics = Graphics.FromHwnd(0);
        var scale = graphics.DpiY / 96f;
        var needed = MeasureText(graphics, text, font, (int)(textWidth * scale), wrap: true).Height / scale;
        var height = (int)Math.Min(needed + chrome + 24, area.Height / scale * .85f);
        dialog.ClientSize = new Size(dialog.ClientSize.Width, Math.Max(dialog.ClientSize.Height, height));
    }

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

    // Fit the whole image inside its slot without changing its proportions or cropping it.
    public static Rectangle FitImage(Image image, Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return Rectangle.Empty;
        var scale = Math.Min((double)bounds.Width / image.Width, (double)bounds.Height / image.Height);
        var width = Math.Clamp((int)Math.Round(image.Width * scale), 1, bounds.Width);
        var height = Math.Clamp((int)Math.Round(image.Height * scale), 1, bounds.Height);
        return new Rectangle(bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - height) / 2, width, height);
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
                case Glyph.Intersect:
                    // Keep these coordinates in sync with tools/Generate-BrandAssets.ps1.
                    using (var brandPen = new Pen(color, 3.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        graphics.DrawLine(brandPen, 3, 12, 21, 12);
                        graphics.DrawLine(brandPen, 8, 20, 16, 4);
                    }
                    break;
                case Glyph.Window:
                    DrawRounded(graphics, pen, 3, 4.5f, 18, 15, 2.5f);
                    graphics.DrawLine(pen, 3, 9, 21, 9);
                    break;
                case Glyph.File:
                    graphics.DrawPolygon(pen, new PointF[] { new(6, 3), new(14, 3), new(19, 8), new(19, 21), new(6, 21) });
                    graphics.DrawLines(pen, new PointF[] { new(14, 3), new(14, 8), new(19, 8) });
                    break;
                case Glyph.Folder:
                    graphics.DrawPolygon(pen, new PointF[] { new(3, 5.5f), new(9.5f, 5.5f), new(11.5f, 8), new(21, 8), new(21, 19), new(3, 19) });
                    break;
                case Glyph.Compass:
                    graphics.DrawEllipse(pen, 3, 3, 18, 18);
                    graphics.DrawPolygon(pen, new PointF[] { new(15.5f, 8.5f), new(13.5f, 13.5f), new(8.5f, 15.5f), new(10.5f, 10.5f) });
                    break;
                case Glyph.External:
                    graphics.DrawLines(pen, new PointF[] { new(18, 13.5f), new(18, 19.5f), new(4.5f, 19.5f), new(4.5f, 6), new(10.5f, 6) });
                    graphics.DrawLines(pen, new PointF[] { new(14.5f, 3.5f), new(20.5f, 3.5f), new(20.5f, 9.5f) });
                    graphics.DrawLine(pen, 20.5f, 3.5f, 11, 13);
                    break;
                case Glyph.User:
                    graphics.DrawEllipse(pen, 8, 3.5f, 8, 8);
                    graphics.DrawBezier(pen, 4.5f, 20.5f, 4.5f, 13.5f, 19.5f, 13.5f, 19.5f, 20.5f);
                    break;
                case Glyph.Import:
                    graphics.DrawLine(pen, 12, 3.5f, 12, 14.5f);
                    graphics.DrawLines(pen, new PointF[] { new(7.5f, 10), new(12, 14.5f), new(16.5f, 10) });
                    graphics.DrawLines(pen, new PointF[] { new(4, 15), new(4, 20), new(20, 20), new(20, 15) });
                    break;
                case Glyph.Pencil:
                    graphics.DrawPolygon(pen, new PointF[] { new(16, 4), new(20, 8), new(8.5f, 19.5f), new(4, 20), new(4.5f, 15.5f) });
                    graphics.DrawLine(pen, 13.5f, 6.5f, 17.5f, 10.5f);
                    break;
                case Glyph.Trash:
                    graphics.DrawLine(pen, 4, 6.5f, 20, 6.5f);
                    graphics.DrawLines(pen, new PointF[] { new(9, 6.5f), new(9, 3.5f), new(15, 3.5f), new(15, 6.5f) });
                    graphics.DrawLines(pen, new PointF[] { new(6, 6.5f), new(7, 20.5f), new(17, 20.5f), new(18, 6.5f) });
                    graphics.DrawLine(pen, 10, 10.5f, 10, 16.5f);
                    graphics.DrawLine(pen, 14, 10.5f, 14, 16.5f);
                    break;
                case Glyph.Scissors:
                    graphics.DrawEllipse(pen, 3, 3, 6, 6);
                    graphics.DrawEllipse(pen, 3, 15, 6, 6);
                    graphics.DrawLine(pen, 20, 4, 8.1f, 15.9f);
                    graphics.DrawLine(pen, 14.5f, 14.5f, 20, 20);
                    graphics.DrawLine(pen, 8.1f, 8.1f, 12, 12);
                    break;
                case Glyph.Copy:
                    DrawRounded(graphics, pen, 8.5f, 8.5f, 12.5f, 12.5f, 2);
                    graphics.DrawLines(pen, new PointF[] { new(5, 15.5f), new(3.5f, 15.5f), new(3.5f, 3.5f), new(15.5f, 3.5f), new(15.5f, 5) });
                    break;
                case Glyph.Clipboard:
                    DrawRounded(graphics, pen, 5, 4.5f, 14, 16.5f, 2);
                    DrawRounded(graphics, pen, 9, 2.5f, 6, 4, 1);
                    break;
                case Glyph.SelectAll:
                    using (var dashed = new Pen(color, stroke) { DashPattern = [1.4f, 1.6f], DashCap = DashCap.Round })
                        DrawRounded(graphics, dashed, 3.5f, 3.5f, 17, 17, 2.5f);
                    graphics.DrawLines(pen, new PointF[] { new(8, 12.5f), new(10.8f, 15.3f), new(16.3f, 9.3f) });
                    break;
                case Glyph.Monitor:
                    DrawRounded(graphics, pen, 3, 4, 18, 12.5f, 2);
                    graphics.DrawLine(pen, 12, 16.5f, 12, 20);
                    graphics.DrawLine(pen, 8, 20, 16, 20);
                    break;
                case Glyph.Sun:
                    graphics.DrawEllipse(pen, 8, 8, 8, 8);
                    for (var ray = 0; ray < 8; ray++)
                    {
                        var angle = ray * Math.PI / 4;
                        var (dx, dy) = ((float)Math.Cos(angle), (float)Math.Sin(angle));
                        graphics.DrawLine(pen, 12 + dx * 6.8f, 12 + dy * 6.8f, 12 + dx * 9, 12 + dy * 9);
                    }
                    break;
                case Glyph.Moon:
                    // A circle with a smaller one bitten out of its upper right edge.
                    using (var moon = new GraphicsPath())
                    {
                        moon.AddArc(3, 3, 18, 18, -5, 280);
                        moon.AddArc(12, 0, 12, 12, 209.6f, -149.2f);
                        moon.CloseFigure();
                        graphics.DrawPath(pen, moon);
                    }
                    break;
                case Glyph.Flask:
                    graphics.DrawLine(pen, 8.5f, 3, 15.5f, 3);
                    graphics.DrawLines(pen, new PointF[] { new(10, 3), new(10, 9), new(4.6f, 18.2f), new(5.8f, 20.5f), new(18.2f, 20.5f), new(19.4f, 18.2f), new(14, 9), new(14, 3) });
                    graphics.DrawLine(pen, 7.2f, 14.5f, 16.8f, 14.5f);
                    break;
                case Glyph.Question:
                    graphics.DrawEllipse(pen, 3, 3, 18, 18);
                    graphics.DrawArc(pen, 9.2f, 6.8f, 5.6f, 5.6f, 190, 225);
                    graphics.DrawLine(pen, 13.8f, 11.4f, 12, 13.2f);
                    graphics.DrawLine(pen, 12, 17, 12, 17.1f);
                    break;
                case Glyph.Refresh:
                    graphics.DrawArc(pen, 4, 4, 16, 16, 135, 270);
                    graphics.DrawLines(pen, new PointF[] { new(17.7f, 12.6f), new(17.7f, 17.7f), new(22.2f, 17.7f) });
                    break;
                case Glyph.Ban:
                    graphics.DrawEllipse(pen, 3, 3, 18, 18);
                    graphics.DrawLine(pen, 5.7f, 5.7f, 18.3f, 18.3f);
                    break;
                case Glyph.EyeOff:
                    using (var eye = new GraphicsPath())
                    {
                        eye.AddBezier(2.5f, 12, 6, 5.5f, 18, 5.5f, 21.5f, 12);
                        eye.AddBezier(21.5f, 12, 18, 18.5f, 6, 18.5f, 2.5f, 12);
                        graphics.DrawPath(pen, eye);
                    }
                    graphics.DrawEllipse(pen, 9.5f, 9.5f, 5, 5);
                    graphics.DrawLine(pen, 4, 3.5f, 20, 20.5f);
                    break;
                case Glyph.Exit:
                    graphics.DrawLines(pen, new PointF[] { new(9.5f, 4), new(5, 4), new(5, 20), new(9.5f, 20) });
                    graphics.DrawLine(pen, 10, 12, 20.5f, 12);
                    graphics.DrawLines(pen, new PointF[] { new(16.5f, 8), new(20.5f, 12), new(16.5f, 16) });
                    break;
                case Glyph.Bolt:
                    graphics.DrawPolygon(pen, new PointF[] { new(13.5f, 2.5f), new(4.5f, 13.5f), new(11.5f, 13.5f), new(10.5f, 21.5f), new(19.5f, 10.5f), new(12.5f, 10.5f) });
                    break;
                case Glyph.Sparkle:
                    graphics.DrawPolygon(pen, new PointF[] { new(10, 3), new(11.9f, 9.1f), new(18, 11), new(11.9f, 12.9f), new(10, 19), new(8.1f, 12.9f), new(2, 11), new(8.1f, 9.1f) });
                    graphics.DrawLine(pen, 18.5f, 15, 18.5f, 21);
                    graphics.DrawLine(pen, 15.5f, 18, 21.5f, 18);
                    break;
                case Glyph.Layers:
                    graphics.DrawPolygon(pen, new PointF[] { new(12, 3.5f), new(21, 8.5f), new(12, 13.5f), new(3, 8.5f) });
                    graphics.DrawLines(pen, new PointF[] { new(3, 12.5f), new(12, 17.5f), new(21, 12.5f) });
                    graphics.DrawLines(pen, new PointF[] { new(3, 16.5f), new(12, 21.5f), new(21, 16.5f) });
                    break;
                case Glyph.CheckCircle:
                    graphics.DrawEllipse(pen, 3, 3, 18, 18);
                    graphics.DrawLines(pen, new PointF[] { new(8, 12.3f), new(10.9f, 15.2f), new(16.2f, 9.4f) });
                    break;
            }
        }
        finally { graphics.Restore(state); }
    }

    private static void DrawRounded(Graphics graphics, Pen pen, float x, float y, float width, float height, float radius)
    {
        using var shape = Rounded(new RectangleF(x, y, width, height), radius);
        graphics.DrawPath(pen, shape);
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

internal enum Glyph
{
    None, Power, Apps, Globe, Sliders, Pulse, Plus, Check, Close, Alert, Shield, More, Chevron, Tunnel, Intersect,
    Window, File, Folder, Compass, External, User, Import, Pencil, Trash, Scissors, Copy, Clipboard, SelectAll,
    Monitor, Sun, Moon, Flask, Question, Refresh, Ban, EyeOff, Exit, Bolt, Sparkle, Layers, CheckCircle
}

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
            e.Graphics.DrawImage(icon, DesktopTheme.FitImage(icon, new Rectangle(Padding.Left, (Height - size) / 2, size * 4 / 3, size)));
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
