using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;
using static AppTunnel.Core.Localized;
using static AppTunnel.Windows.DesktopTheme;

namespace AppTunnel.Windows;

// Base for painted controls that take focus and act like buttons, so Space, Enter and screen readers work.
internal abstract class PaintedButton : Button
{
    protected bool Hovering { get; private set; }

    protected PaintedButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.Opaque | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        Margin = new Padding(0);
    }

    protected float S => DpiScale(this);
    protected int Px(float logical) => (int)Math.Round(logical * S);

    protected void PaintCanvas(Graphics graphics)
    {
        using var canvas = new SolidBrush(Parent?.BackColor ?? Background);
        graphics.FillRectangle(canvas, ClientRectangle);
    }

    protected void PaintFocus(Graphics graphics, Rectangle bounds, float radius)
    {
        if (!Focused || !ShowFocusCues) return;
        PaintRounded(graphics, bounds, radius, Color.Transparent, Accent, Math.Max(1.5f, 2 * S));
    }

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Hovering = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Hovering = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
}

internal enum PowerState { Off, On, Busy }

// The round connect/disconnect control on the home page.
internal sealed class PowerButton : PaintedButton
{
    private readonly System.Windows.Forms.Timer spin = new() { Interval = 33 };
    private PowerState state;
    private float angle;

    public PowerButton()
    {
        spin.Tick += (_, _) => { angle = (angle + 9) % 360; Invalidate(); };
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PowerState State
    {
        get => state;
        set
        {
            if (state == value) return;
            state = value;
            spin.Enabled = value == PowerState.Busy;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        PaintCanvas(g);
        var side = Math.Min(Width, Height) - 1;
        var outer = new RectangleF((Width - side) / 2f, (Height - side) / 2f, side, side);
        var on = state == PowerState.On;
        var ring = Px(8);
        PaintCircle(g, outer, on ? Accent : InputBorder);
        var middle = RectangleF.Inflate(outer, -Math.Max(1, S), -Math.Max(1, S));
        PaintCircle(g, middle, on ? AccentWash : Surface);
        var core = RectangleF.Inflate(outer, -ring - S, -ring - S);
        var fill = on ? Hovering ? AccentHover : Accent : Hovering && Enabled ? Hover : Raised;
        PaintCircle(g, core, fill);
        if (state == PowerState.Busy)
        {
            var saved = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Accent, ring * .55f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            var arc = RectangleF.Inflate(outer, -ring / 2f - S, -ring / 2f - S);
            g.DrawArc(pen, arc, angle, 80);
            g.Restore(saved);
        }
        if (Focused && ShowFocusCues)
            PaintCircle(g, RectangleF.Inflate(core, -Px(5), -Px(5)), Color.Transparent, on ? AccentInk : Accent, Math.Max(1.5f, 2 * S));
        var icon = Px(56);
        PaintIcon(g, Glyph.Power, new RectangleF(outer.X + (side - icon) / 2f, outer.Y + (side - icon) / 2f, icon, icon),
            !Enabled ? Disabled : on ? AccentInk : DesktopTheme.Text, 2.2f);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) spin.Dispose();
        base.Dispose(disposing);
    }
}

// Rounded status label such as "Connected" or "Via VPN".
internal static class Pills
{
    public static Size Measure(Graphics graphics, Control owner, string text, Font font)
    {
        var s = DpiScale(owner);
        var size = MeasureText(graphics, text, font);
        return new Size(size.Width + (int)(22 * s), Math.Max(size.Height + (int)(6 * s), (int)(26 * s)));
    }

    public static Rectangle Paint(Graphics graphics, Control owner, string text, Font font, Point location, Color ink, Color fill)
    {
        var size = Measure(graphics, owner, text, font);
        var bounds = new Rectangle(location, size);
        PaintRounded(graphics, bounds, size.Height / 2f, fill);
        PaintText(graphics, text, font, bounds, ink, StringAlignment.Center);
        return bounds;
    }
}

internal static class Switches
{
    public static void Paint(Graphics graphics, Rectangle track, bool on, bool enabled)
    {
        PaintRounded(graphics, track, track.Height / 2f, on ? enabled ? Accent : Border : Border);
        var inset = track.Height * 3 / 32f;
        var knob = track.Height - inset * 2;
        var x = on ? track.Right - inset - knob : track.X + inset;
        PaintCircle(graphics, new RectangleF(x, track.Y + inset, knob, knob), on ? AccentInk : enabled ? Muted : Disabled);
    }
}

// The large card at the top of the home page: power button, status pill, headline and detail.
internal sealed class HeroCard : ThemeCard
{
    private string pill = "";
    private string headline = "";
    private string detail = "";
    private Color pillInk = Muted;
    private Color pillFill = Raised;
    public PowerButton Power { get; } = new();
    public ThemeButton Action { get; }

    public HeroCard(ThemeButton action) : base(Surface)
    {
        Radius = 16;
        Action = action;
        Controls.Add(Power);
        Controls.Add(Action);
        Action.VisibleChanged += (_, _) => PerformLayout();
    }

    public void Show(string pillText, Color ink, Color fill, string headlineText, string detailText)
    {
        if (pill == pillText && headline == headlineText && detail == detailText && pillInk == ink && pillFill == fill) return;
        pill = pillText;
        pillInk = ink;
        pillFill = fill;
        headline = headlineText;
        detail = detailText;
        AccessibleName = $"{pill}. {headline}. {detail}";
        PerformLayout();
        Invalidate();
    }

    private float S => DpiScale(this);
    private Rectangle TextArea => new((int)(196 * S), (int)(28 * S), Math.Max(0, Width - (int)(228 * S)), Math.Max(0, Height - (int)(56 * S)));

    private (int Pill, int Headline, int Detail, int Total) Measure(Graphics graphics)
    {
        var area = TextArea;
        var pillHeight = Pills.Measure(graphics, this, pill, Body(9.75f, bold: true)).Height;
        var headlineHeight = MeasureText(graphics, headline, Heading(30), area.Width).Height;
        var detailHeight = MeasureText(graphics, detail, Body(12), area.Width, wrap: true).Height;
        var gap = (int)(8 * S);
        var actionHeight = Action.Visible ? Action.Height + (int)(14 * S) : 0;
        return (pillHeight, headlineHeight, detailHeight, pillHeight + gap + headlineHeight + gap + detailHeight + actionHeight);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var side = (int)(132 * S);
        Power.Bounds = new Rectangle((int)(32 * S), (Height - side) / 2, side, side);
        if (!Action.Visible) return;
        using var graphics = CreateGraphics();
        var measured = Measure(graphics);
        var area = TextArea;
        var top = area.Y + (area.Height - measured.Total) / 2;
        Action.Location = new Point(area.X, top + measured.Total - Action.Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        var area = TextArea;
        var m = Measure(g);
        var gap = (int)(8 * S);
        var top = area.Y + (area.Height - m.Total) / 2;
        Pills.Paint(g, this, pill, Body(9.75f, bold: true), new Point(area.X, top), pillInk, pillFill);
        top += m.Pill + gap;
        PaintText(g, headline, Heading(30), new Rectangle(area.X, top, area.Width, m.Headline), DesktopTheme.Text);
        top += m.Headline + gap;
        PaintText(g, detail, Body(12), new Rectangle(area.X, top, area.Width, m.Detail), Muted, line: StringAlignment.Near, wrap: true);
    }
}

// Small home cards: an uppercase caption, a badge, a title with detail, and one action button.
internal sealed class InfoCard : ThemeCard
{
    private string caption = "";
    private string title = "";
    private string detail = "";
    private string badgeText = "";
    private Glyph badgeGlyph;
    private Color badgeInk = DesktopTheme.Text;
    private Color badgeFill = Raised;
    public ThemeButton Action { get; }

    public InfoCard(string captionText, ThemeButton action) : base(Surface)
    {
        Radius = 16;
        caption = captionText.ToUpper(System.Globalization.CultureInfo.CurrentCulture);
        Action = action;
        Controls.Add(action);
    }

    public void Show(string titleText, string detailText, string badge, Glyph glyph, Color ink, Color fill)
    {
        if (title == titleText && detail == detailText && badgeText == badge && badgeGlyph == glyph && badgeInk == ink && badgeFill == fill) return;
        title = titleText;
        detail = detailText;
        badgeText = badge;
        badgeGlyph = glyph;
        badgeInk = ink;
        badgeFill = fill;
        AccessibleName = $"{caption}: {title}. {detail}";
        Invalidate();
    }

    private float S => DpiScale(this);
    private int ContentTop => (int)(22 * S) + Body(9.75f, bold: true).LineHeight(this) + (int)(14 * S);

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var row = (int)(44 * S);
        Action.Location = new Point(Width - (int)(24 * S) - Action.Width, ContentTop + (row - Action.Height) / 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        var left = (int)(24 * S);
        PaintText(g, caption, Body(9.75f, bold: true), new Rectangle(left, (int)(22 * S), Width - left * 2, Body(9.75f, bold: true).LineHeight(this)), Muted);
        var top = ContentTop;
        var badge = new Rectangle(left, top + (int)(2 * S), (int)(40 * S), (int)(40 * S));
        PaintRounded(g, badge, 10 * S, badgeFill, badgeGlyph == Glyph.None ? Border : null);
        if (badgeGlyph != Glyph.None) PaintIcon(g, badgeGlyph, Rectangle.Inflate(badge, -(int)(10 * S), -(int)(10 * S)), badgeInk, 2.4f);
        else PaintText(g, badgeText, Body(9.75f, bold: true), badge, badgeInk, StringAlignment.Center);
        var textLeft = badge.Right + (int)(14 * S);
        var width = Math.Max(0, Action.Left - (int)(14 * S) - textLeft);
        var titleHeight = Body(12.75f, bold: true).LineHeight(this);
        PaintText(g, title, Body(12.75f, bold: true), new Rectangle(textLeft, top, width, titleHeight), DesktopTheme.Text);
        PaintText(g, detail, Body(10.5f), new Rectangle(textLeft, top + titleHeight, width, Body(10.5f).LineHeight(this) + (int)(2 * S)), Muted);
    }
}

// A card with a caption on top and an optional link at the top right; children fill the area below.
internal sealed class CaptionCard : ThemeCard
{
    private readonly string caption;
    private readonly Control? link;

    public CaptionCard(string captionText, Control? headerLink = null) : base(Surface)
    {
        Radius = 16;
        caption = captionText.ToUpper(System.Globalization.CultureInfo.CurrentCulture);
        link = headerLink;
        if (link is not null) Controls.Add(link);
        Padding = new Padding(24, 56, 24, 20);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (link is null) return;
        var s = DpiScale(this);
        link.Location = new Point(Width - (int)(16 * s) - link.Width, (int)(12 * s));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var s = DpiScale(this);
        var font = Body(9.75f, bold: true);
        PaintText(e.Graphics, caption, font, new Rectangle((int)(24 * s), (int)(20 * s), Width - (int)(48 * s) - (link?.Width ?? 0),
            font.LineHeight(this) + (int)(4 * s)), Muted);
    }
}

// App toggle on the home page; the dashed variant opens the Apps page.
internal sealed class ChipButton : PaintedButton
{
    private bool on;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Dashed { get; init; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool On
    {
        get => on;
        set
        {
            on = value;
            AccessibleDescription = Dashed ? "" : value ? T("On the VPN", "Na VPN", "Через VPN") : T("Direct", "Direto", "Напрямую");
            Invalidate();
        }
    }

    public ChipButton()
    {
        Font = Medium(11.25f);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = new Padding(0, 0, 10, 10);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = MeasureText(this, Text, Font);
        return new Size(text.Width + Px(18) * 2 + Px(18), Px(44));
    }

    protected override void ScaleControl(SizeF factor, BoundsSpecified specified) { }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Margin = new Padding(0, 0, Px(10), Px(10));
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Margin = new Padding(0, 0, Px(10), Px(10));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        PaintCanvas(g);
        var bounds = ClientRectangle;
        var radius = Height / 2f;
        if (Dashed)
            PaintRounded(g, bounds, radius, Hovering ? Hover : Color.Transparent, InputBorder, Math.Max(1, S), DashStyle.Dash);
        else
            PaintRounded(g, bounds, radius, on ? AccentWash : Hovering ? Hover : Raised, on ? Accent : Border);
        PaintFocus(g, Rectangle.Inflate(bounds, -Px(3), -Px(3)), radius - Px(3));
        var left = Px(18);
        if (Dashed)
            PaintIcon(g, Glyph.Plus, new Rectangle(left - Px(2), (Height - Px(16)) / 2, Px(16), Px(16)), Muted, 2.2f);
        else
            PaintCircle(g, new RectangleF(left, (Height - Px(8)) / 2f, Px(8), Px(8)), on ? Accent : InputBorder);
        PaintText(g, Text, Font, new Rectangle(left + Px(18), 0, Width - left - Px(18) - Px(12), Height),
            Dashed ? Muted : on ? DesktopTheme.Text : Muted);
    }
}

// One app on the Apps page: initial, name, path, state, switch and a remove button.
internal sealed class AppRow : PaintedButton
{
    private readonly ToolTip tip = new();
    private bool overRemove;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title { get; set; } = "";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Note { get; set; } = "";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Missing { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool On { get; set; }
    public event Action? RemoveRequested;
    public event Action? ToggleRequested;

    public AppRow(string path)
    {
        TabStop = true;
        AccessibleRole = AccessibleRole.CheckButton;
        tip.SetToolTip(this, path);
    }

    public void Refresh(string path)
    {
        tip.SetToolTip(this, path);
        AccessibleName = Title;
        AccessibleDescription = On ? T("On the VPN", "Na VPN", "Через VPN") : T("Direct", "Direto", "Напрямую");
        Invalidate();
    }

    // Rows are created after the window has scaled, so they size themselves from the monitor DPI instead.
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified) { }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Refresh(tip.GetToolTip(this) ?? "");
        Height = Px(72);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Height = Px(72);
    }

    private Rectangle RemoveBounds => new(Width - Px(14) - Px(32), (Height - Px(32)) / 2, Px(32), Px(32));
    private Rectangle SwitchBounds => new(RemoveBounds.X - Px(10) - Px(56), (Height - Px(32)) / 2, Px(56), Px(32));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var over = RemoveBounds.Contains(e.Location);
        if (over == overRemove) return;
        overRemove = over;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        overRemove = false;
        base.OnMouseLeave(e);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (e is MouseEventArgs mouse && RemoveBounds.Contains(mouse.Location)) RemoveRequested?.Invoke();
        else ToggleRequested?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs kevent)
    {
        base.OnKeyDown(kevent);
        if (kevent.KeyCode != Keys.Delete) return;
        kevent.Handled = true;
        RemoveRequested?.Invoke();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        PaintCanvas(g);
        var bounds = ClientRectangle;
        PaintRounded(g, bounds, 14 * S, Hovering ? Hover : Surface, On ? StrongBorder : Border);
        PaintFocus(g, Rectangle.Inflate(bounds, -Px(3), -Px(3)), 11 * S);
        var icon = new Rectangle(Px(18), (Height - Px(44)) / 2, Px(44), Px(44));
        PaintRounded(g, icon, 12 * S, On ? AccentWash : Raised);
        var initial = Title.Length > 0 ? char.ToUpper(Title[0], System.Globalization.CultureInfo.CurrentCulture).ToString() : "?";
        PaintText(g, initial, Heading(15), icon, On ? Accent : Muted, StringAlignment.Center);

        var remove = RemoveBounds;
        if (overRemove) PaintRounded(g, remove, 8 * S, Raised);
        PaintIcon(g, Glyph.Close, Rectangle.Inflate(remove, -Px(8), -Px(8)), overRemove ? Danger : Muted, 2f);
        var track = SwitchBounds;
        Switches.Paint(g, track, On, Enabled);
        var state = On ? T("Via VPN", "Pela VPN", "Через VPN") : T("Direct", "Direto", "Напрямую");
        var pillFont = Body(9.75f, bold: true);
        var pillSize = Pills.Measure(g, this, state, pillFont);
        var pillLeft = track.X - Px(16) - pillSize.Width;
        Pills.Paint(g, this, state, pillFont, new Point(pillLeft, (Height - pillSize.Height) / 2), On ? Green : Muted, On ? GreenWash : Raised);

        var textLeft = icon.Right + Px(16);
        var width = Math.Max(0, pillLeft - Px(16) - textLeft);
        var name = Body(12.75f, bold: true);
        var note = Body(9.75f);
        var block = name.LineHeight(this) + note.LineHeight(this);
        var top = (Height - block) / 2;
        PaintText(g, Title, name, new Rectangle(textLeft, top, width, name.LineHeight(this)), DesktopTheme.Text);
        PaintText(g, Note, note, new Rectangle(textLeft, top + name.LineHeight(this), width, note.LineHeight(this) + Px(2)), Missing ? Danger : Muted);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) tip.Dispose();
        base.Dispose(disposing);
    }
}

// A server choice on the Servers page, drawn as a radio row. The automatic variant is taller and has a badge.
internal sealed class ServerRow : PaintedButton
{
    private readonly int logicalHeight;
    private bool overMenu;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Automatic { get; init; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Picked { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Code { get; set; } = "";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title { get; set; } = "";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Detail { get; set; } = "";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Status { get; set; } = "";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public long? Latency { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Measured { get; set; }
    public event Action<Point>? MenuRequested;

    // Country flag shown instead of the country code; the chooser and the Servers page set it.
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Image? Flag { get; set; }

    // Server load in percent, shown as "Load 45%" beside the ping bar when set.
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int? Load { get; set; }

    // The route chooser lists servers to pick once, so it hides the radio and the menu.
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Choice { get; init; }

    public ServerRow(bool automatic)
    {
        Automatic = automatic;
        logicalHeight = automatic ? 84 : 60;
        AccessibleRole = AccessibleRole.RadioButton;
    }

    public new void Refresh()
    {
        AccessibleName = Title;
        AccessibleDescription = (Picked ? T("Selected. ", "Selecionado. ", "Выбрано. ") : "") + (Automatic ? Detail : LatencyText);
        Invalidate();
    }

    protected override void ScaleControl(SizeF factor, BoundsSpecified specified) { }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Refresh();
        Height = Px(logicalHeight);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Height = Px(logicalHeight);
    }

    private string LatencyText => !Measured ? "—" : Latency is { } ms ? T($"{ms} ms", $"{ms} ms", $"{ms} мс") : T("No reply", "Sem resposta", "Нет ответа");
    private Rectangle MenuBounds => new(Width - Px(10) - Px(32), (Height - Px(32)) / 2, Px(32), Px(32));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var over = !Automatic && !Choice && MenuBounds.Contains(e.Location);
        if (over == overMenu) return;
        overMenu = over;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        overMenu = false;
        base.OnMouseLeave(e);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        base.OnMouseUp(mevent);
        if (!Automatic && !Choice && mevent.Button == MouseButtons.Right) MenuRequested?.Invoke(mevent.Location);
    }

    // The menu button sits inside the row, so its clicks open the menu instead of choosing the server.
    protected override void OnClick(EventArgs e)
    {
        if (e is MouseEventArgs mouse && !Automatic && !Choice && MenuBounds.Contains(mouse.Location))
        {
            MenuRequested?.Invoke(new Point(MenuBounds.Left, MenuBounds.Bottom));
            return;
        }
        base.OnClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs kevent)
    {
        base.OnKeyDown(kevent);
        if (Automatic || Choice || kevent.KeyCode != Keys.Apps && !(kevent.KeyCode == Keys.F10 && kevent.Shift)) return;
        kevent.Handled = true;
        MenuRequested?.Invoke(new Point(MenuBounds.Left, MenuBounds.Bottom));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        PaintCanvas(g);
        var bounds = ClientRectangle;
        var radius = (Automatic ? 14 : 12) * S;
        PaintRounded(g, bounds, radius, Picked ? AccentWash : Hovering ? Hover : Surface, Picked ? Accent : Border);
        PaintFocus(g, Rectangle.Inflate(bounds, -Px(3), -Px(3)), radius - Px(3));
        var left = Px(Automatic ? 20 : 18);
        var radio = new RectangleF(left, (Height - Px(22)) / 2f, Px(22), Px(22));
        if (!Choice)
        {
            PaintCircle(g, radio, Color.Transparent, Picked ? Accent : InputBorder, Math.Max(1.5f, 2 * S));
            if (Picked) PaintCircle(g, RectangleF.Inflate(radio, -Px(6), -Px(6)), Accent);
        }
        var x = Choice ? left : (int)radio.Right + Px(Automatic ? 16 : 14);

        if (Automatic)
        {
            var titleFont = Body(13.5f, bold: true);
            var autoDetail = Body(10.5f);
            var block = titleFont.LineHeight(this) + Px(3) + autoDetail.LineHeight(this);
            var autoTop = (Height - block) / 2;
            var titleWidth = MeasureText(g, Title, titleFont).Width;
            PaintText(g, Title, titleFont, new Rectangle(x, autoTop, Math.Min(titleWidth, Width - x), titleFont.LineHeight(this)), DesktopTheme.Text);
            var badge = T("Recommended", "Recomendado", "Рекомендуется");
            var badgeFont = Body(9.75f, bold: true);
            var badgeSize = MeasureText(g, badge, badgeFont);
            var pill = new Rectangle(x + titleWidth + Px(6), autoTop + (titleFont.LineHeight(this) - badgeSize.Height - Px(4)) / 2,
                badgeSize.Width + Px(14), badgeSize.Height + Px(4));
            PaintRounded(g, pill, pill.Height / 2f, Accent);
            PaintText(g, badge, badgeFont, pill, AccentInk, StringAlignment.Center);
            PaintText(g, Detail, autoDetail, new Rectangle(x, autoTop + titleFont.LineHeight(this) + Px(3), Width - x - Px(20), autoDetail.LineHeight(this) + Px(2)), Muted);
            return;
        }

        int loadLeft;
        var code = new Rectangle(x, (Height - Px(30)) / 2, Px(40), Px(30));
        if (Flag is { } flag)
        {
            // Country flags have different proportions, including square and tall flags.
            var flagBounds = FitImage(flag, code);
            var saved = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using (var clip = Rounded(flagBounds, 6 * S)) g.SetClip(clip, CombineMode.Intersect);
            g.DrawImage(flag, flagBounds);
            g.Restore(saved);
            PaintRounded(g, flagBounds, 6 * S, Color.Transparent, Border);
        }
        else
        {
            PaintRounded(g, code, 8 * S, Raised, Border);
            PaintText(g, Code, Body(9.75f, bold: true), code, DesktopTheme.Text, StringAlignment.Center);
        }
        var menu = MenuBounds;
        if (!Choice)
        {
            if (overMenu) PaintRounded(g, menu, 8 * S, Raised);
            PaintIcon(g, Glyph.More, Rectangle.Inflate(menu, -Px(6), -Px(6)), overMenu ? DesktopTheme.Text : Muted);
        }
        else menu = new Rectangle(Width - Px(6), 0, 0, Height);
        var meter = new Rectangle(menu.X - Px(12) - Px(200), 0, Px(200), Height);
        if (Load is { } load)
        {
            // Server load, spelled out and colored, just left of the ping bar.
            var label = T("Load", "Carga", "Нагрузка") + " ";
            var loadValue = $"{load}%";
            var labelFont = Body(10.5f);
            var valueFont = Body(10.5f, bold: true);
            var valueWidth = MeasureText(g, loadValue, valueFont).Width;
            var labelWidth = MeasureText(g, label, labelFont).Width;
            var right = meter.X - Px(14);
            PaintText(g, loadValue, valueFont, new Rectangle(right - valueWidth, 0, valueWidth, Height),
                load < 60 ? Green : load < 85 ? Amber : Danger);
            PaintText(g, label, labelFont, new Rectangle(right - valueWidth - labelWidth, 0, labelWidth, Height), Muted);
            loadLeft = right - valueWidth - labelWidth;
        }
        else loadLeft = meter.X;
        var ms = new Rectangle(meter.Right - Px(96), 0, Px(96), Height);
        PaintText(g, LatencyText, Body(10.5f), ms, Measured && Latency is null ? Muted : DesktopTheme.Text, StringAlignment.Far);
        var bar = new Rectangle(meter.X, (Height - Px(6)) / 2, meter.Width - ms.Width - Px(10), Px(6));
        PaintRounded(g, bar, bar.Height / 2f, Border);
        if (Latency is { } value)
        {
            var share = Math.Clamp(100 - value / 3.0, 8, 100) / 100;
            var color = value < 60 ? Green : value < 180 ? Amber : Danger;
            PaintRounded(g, bar with { Width = Math.Max(bar.Height, (int)(bar.Width * share)) }, bar.Height / 2f, color);
        }
        var nameLeft = code.Right + Px(14);
        var nameFont = Body(12, bold: true);
        var statusWidth = Status.Length == 0 ? 0 : MeasureText(g, Status, Body(9.75f, bold: true)).Width + Px(12);
        var nameWidth = Math.Max(0, loadLeft - Px(16) - nameLeft - statusWidth);
        var titleSize = Math.Min(nameWidth, MeasureText(g, Title, nameFont).Width);
        var detailFont = Body(9.75f);
        var hasDetail = Detail.Length > 0;
        var top = hasDetail ? (Height - nameFont.LineHeight(this) - detailFont.LineHeight(this)) / 2 : (Height - nameFont.LineHeight(this)) / 2;
        PaintText(g, Title, nameFont, new Rectangle(nameLeft, top, nameWidth, nameFont.LineHeight(this)), DesktopTheme.Text);
        if (Status.Length > 0)
            PaintText(g, Status, Body(9.75f, bold: true), new Rectangle(nameLeft + titleSize + Px(10), top, statusWidth, nameFont.LineHeight(this)), Green);
        if (hasDetail)
            PaintText(g, Detail, detailFont, new Rectangle(nameLeft, top + nameFont.LineHeight(this), loadLeft - Px(16) - nameLeft, detailFont.LineHeight(this) + Px(2)), Muted);
    }
}

// Page title with a one-line description underneath.
internal sealed class PageHeader : Control
{
    private readonly string subtitle;

    public PageHeader(string title, string subtitleText)
    {
        Text = title;
        subtitle = subtitleText;
        AccessibleRole = AccessibleRole.StaticText;
        AccessibleName = title;
        AccessibleDescription = subtitle;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw, true);
        Margin = new Padding(0);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var canvas = new SolidBrush(Parent?.BackColor ?? Background);
        e.Graphics.FillRectangle(canvas, ClientRectangle);
        var s = DpiScale(this);
        var title = Heading(25.5f);
        var body = Body(12);
        var titleHeight = MeasureText(e.Graphics, Text, title).Height;
        var top = Math.Max(0, Height - titleHeight - (int)(6 * s) - body.LineHeight(this) - (int)(4 * s));
        PaintText(e.Graphics, Text, title, new Rectangle(0, top, Width, titleHeight), DesktopTheme.Text);
        PaintText(e.Graphics, subtitle, body, new Rectangle(0, top + titleHeight + (int)(6 * s), Width, body.LineHeight(this) + (int)(4 * s)), Muted);
    }
}

// Status messages appear as a pill at the bottom of the window and fade out after a while.
internal sealed class StatusToast : Control
{
    private readonly System.Windows.Forms.Timer hide = new() { Interval = 9000 };
    private string? actionLabel;
    private Action? action;
    private Rectangle actionBounds;

    public StatusToast()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw, true);
        AccessibleRole = AccessibleRole.StatusBar;
        hide.Tick += (_, _) => Clear();
    }

    [AllowNull]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public override string Text
    {
        get => base.Text;
        set
        {
            base.Text = value ?? "";
            actionLabel = null;
            action = null;
            AccessibleName = base.Text;
            hide.Stop();
            if (base.Text.Length > 0) hide.Start();
            Invalidate();
        }
    }

    public void ShowAction(string message, string label, Action callback)
    {
        Text = message;
        actionLabel = label;
        action = callback;
        AccessibleDescription = $"{label}: Ctrl+Z";
        Invalidate();
    }

    public bool TryRunAction()
    {
        if (action is null) return false;
        var callback = action;
        Clear();
        callback();
        return true;
    }

    public void Clear()
    {
        hide.Stop();
        base.Text = "";
        actionLabel = null;
        action = null;
        Cursor = Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = action is not null && actionBounds.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (action is null || !actionBounds.Contains(e.Location)) return;
        TryRunAction();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using var canvas = new SolidBrush(Parent?.BackColor ?? Background);
        g.FillRectangle(canvas, ClientRectangle);
        if (Text.Length == 0) return;
        var s = DpiScale(this);
        var font = Body(10.5f);
        var bold = Body(10.5f, bold: true);
        var gap = (int)(14 * s);
        var actionWidth = actionLabel is null ? 0 : MeasureText(g, actionLabel, bold).Width + gap;
        var max = Width - (int)(40 * s);
        var textWidth = Math.Min(MeasureText(g, Text, font).Width, Math.Max(0, max - actionWidth));
        var height = (int)(40 * s);
        var pill = new Rectangle((Width - textWidth - actionWidth - (int)(36 * s)) / 2, (Height - height) / 2,
            textWidth + actionWidth + (int)(36 * s), height);
        PaintRounded(g, pill, height / 2f, Raised, Border);
        PaintText(g, Text, font, new Rectangle(pill.X + (int)(18 * s), pill.Y, textWidth, height), DesktopTheme.Text);
        if (actionLabel is null) return;
        actionBounds = new Rectangle(pill.X + (int)(18 * s) + textWidth + gap, pill.Y, actionWidth - gap, height);
        PaintText(g, actionLabel, bold, actionBounds, Accent);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) hide.Dispose();
        base.Dispose(disposing);
    }
}

// Vertical list that stretches each child to its width and scrolls when the children do not fit.
internal sealed class StackPanel : Panel
{
    private bool arranging;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Gap { get; set; } = 10;

    // A fitted stack never scrolls: it grows or shrinks to the height of its children plus its padding.
    private readonly bool fit;

    public StackPanel(bool fitContent = false)
    {
        fit = fitContent;
        AutoScroll = !fitContent;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }

    // Children are sized to the width left after the vertical bar (when one is needed), so a horizontal bar
    // never appears; only a list that is taller than the panel scrolls, and only vertically.
    protected override void OnLayout(LayoutEventArgs levent)
    {
        if (arranging) return;
        arranging = true;
        try
        {
            var gap = Gap * DeviceDpi / 96;
            var visible = Controls.Cast<Control>().Where(child => child.Visible).ToArray();
            var total = visible.Sum(child => child.Height) + Math.Max(0, visible.Length - 1) * gap;
            if (fit)
            {
                var top = visible.Length == 0 ? 0 : Padding.Top;
                foreach (var child in visible)
                {
                    child.SetBounds(0, top, Width, child.Height);
                    top += child.Height + gap;
                }
                var fitted = visible.Length == 0 ? 0 : total + Padding.Vertical;
                if (Height != fitted) Height = fitted;
                base.OnLayout(levent);
                return;
            }
            var bar = total > Height ? SystemInformation.GetVerticalScrollBarWidthForDpi(DeviceDpi) : 0;
            var width = Math.Max(0, Width - bar);
            var y = AutoScrollPosition.Y;
            foreach (var child in visible)
            {
                child.SetBounds(0, y, width, child.Height);
                y += child.Height + gap;
            }
            AutoScrollMinSize = new Size(0, total);
            base.OnLayout(levent);
        }
        finally { arranging = false; }
    }
}

// Dashed target that accepts dragged executables, shortcuts and folders.
internal sealed class DropZone : ThemeCard
{
    private readonly string title;
    private readonly string detail;
    private bool active;

    public DropZone(string titleText, string detailText) : base(Background)
    {
        title = titleText;
        detail = detailText;
        Dashed = true;
        Edge = InputBorder;
        AllowDrop = true;
        AccessibleRole = AccessibleRole.Grouping;
        AccessibleName = title;
        AccessibleDescription = detail;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Active
    {
        get => active;
        set
        {
            if (active == value) return;
            active = value;
            Edge = value ? Accent : InputBorder;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var s = DpiScale(this);
        var bold = Body(10.5f, bold: true);
        var body = Body(10.5f);
        var block = bold.LineHeight(this) + (int)(4 * s) + body.LineHeight(this);
        var top = (Height - block) / 2;
        PaintText(e.Graphics, title, bold, new Rectangle(0, top, Width, bold.LineHeight(this)), active ? Accent : DesktopTheme.Text, StringAlignment.Center);
        PaintText(e.Graphics, detail, body, new Rectangle(0, top + bold.LineHeight(this) + (int)(4 * s), Width, body.LineHeight(this) + (int)(2 * s)), Muted, StringAlignment.Center);
    }
}

// Collapsible header for the advanced server options.
internal sealed class ExpanderHeader : PaintedButton
{
    private bool expanded;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Detail { get; set; } = "";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Expanded
    {
        get => expanded;
        set
        {
            expanded = value;
            AccessibleDescription = value ? T("Expanded", "Expandido", "Развёрнуто") : T("Collapsed", "Recolhido", "Свёрнуто");
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        PaintCanvas(g);
        PaintFocus(g, Rectangle.Inflate(ClientRectangle, -Px(3), -Px(3)), 10 * S);
        var left = Px(20);
        var bold = Body(12, bold: true);
        var titleWidth = MeasureText(g, Text, bold).Width;
        PaintText(g, Text, bold, new Rectangle(left, 0, titleWidth, Height), DesktopTheme.Text);
        var state = expanded ? T("Hide", "Ocultar", "Скрыть") : T("Show", "Mostrar", "Показать");
        var stateWidth = MeasureText(g, state, Body(10.5f)).Width;
        PaintText(g, state, Body(10.5f), new Rectangle(Width - left - stateWidth, 0, stateWidth, Height), Hovering ? DesktopTheme.Text : Muted);
        PaintText(g, "· " + Detail, Body(10.5f), new Rectangle(left + titleWidth + Px(6), 0,
            Math.Max(0, Width - left * 2 - titleWidth - stateWidth - Px(16)), Height), Muted);
    }
}

// Selection field drawn entirely by AppTunnel: a rounded field with a chevron that opens a themed menu. It keeps
// the ComboBox members the app uses (Items, SelectedIndex, SelectedItem, SelectedIndexChanged), so it replaces the
// native combo box, whose system border and arrow could not be fully themed.
internal sealed class ThemeCombo : PaintedButton
{
    private int selected = -1;
    private ThemeMenu? open;
    public event EventHandler? SelectedIndexChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public List<object> Items { get; } = [];

    // Optional icon per item, by index, shown in the list and beside the chosen item.
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public List<Glyph> Glyphs { get; } = [];

    private Glyph GlyphAt(int index) => index >= 0 && index < Glyphs.Count ? Glyphs[index] : Glyph.None;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => selected;
        set
        {
            var next = value >= 0 && value < Items.Count ? value : -1;
            if (next == selected) return;
            selected = next;
            AccessibleDescription = SelectedText;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object? SelectedItem => selected >= 0 && selected < Items.Count ? Items[selected] : null;

    private string SelectedText => SelectedItem?.ToString() ?? "";

    public ThemeCombo()
    {
        Font = Body();
        Size = new Size(240, 40);
        AccessibleRole = AccessibleRole.ComboBox;
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        ShowList();
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs kevent)
    {
        base.OnKeyDown(kevent);
        if (Items.Count == 0) return;
        if (kevent.KeyCode == Keys.Down && kevent.Alt) ShowList();
        else if (kevent.KeyCode == Keys.Down) SelectedIndex = Math.Min(Items.Count - 1, selected + 1);
        else if (kevent.KeyCode == Keys.Up) SelectedIndex = Math.Max(0, selected - 1);
        else return;
        kevent.Handled = true;
    }

    private void ShowList()
    {
        if (!Enabled || Items.Count == 0 || open is not null) return;
        var menu = new ThemeMenu { MaximumSize = new Size(0, Screen.FromControl(this).WorkingArea.Height / 2) };
        for (var i = 0; i < Items.Count; i++)
        {
            var index = i;
            var item = menu.Add(Items[i].ToString() ?? "", GlyphAt(i), (_, _) => SelectedIndex = index);
            item.Checked = i == selected;
        }
        menu.MinimumWidth = Width;
        menu.Closed += (_, _) =>
        {
            open = null;
            Invalidate();
            BeginInvoke(menu.Dispose);
        };
        open = menu;
        Invalidate();
        menu.Show(this, new Point(0, Height + Px(4)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        PaintCanvas(g);
        var bounds = ClientRectangle;
        var active = Focused || open is not null;
        PaintRounded(g, bounds, 8 * S, !Enabled ? Raised : Hovering || open is not null ? Hover : Raised,
            active && Enabled ? Accent : InputBorder, Math.Max(1, S * (active ? 1.5f : 1)));
        var chevron = Px(16);
        var right = Width - Px(12) - chevron;
        PaintIcon(g, Glyph.Chevron, new Rectangle(right, (Height - chevron) / 2, chevron, chevron), Enabled ? Muted : Disabled, 2f);
        var left = Px(12);
        if (GlyphAt(selected) is var glyph and not Glyph.None)
        {
            var size = Px(18);
            PaintIcon(g, glyph, new Rectangle(left, (Height - size) / 2, size, size), Enabled ? Muted : Disabled, 1.9f);
            left += size + Px(10);
        }
        PaintText(g, SelectedText, Font, new Rectangle(left, 0, Math.Max(0, right - left - Px(8)), Height),
            Enabled ? DesktopTheme.Text : Disabled);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) open?.Dispose();
        base.Dispose(disposing);
    }
}
