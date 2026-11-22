using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace AppTunnel.Windows;

// Rounded surface for the card-based pages. Children inherit its BackColor, so changing Fill recolors the whole card.
internal sealed class RoundCard : Panel
{
    private Color? edge;
    private int radius = 12;

    public RoundCard(Color fill, Color? edge = null)
    {
        BackColor = fill;
        this.edge = edge ?? DesktopTheme.Border;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw, true);
        Margin = new Padding(0);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color? Edge
    {
        get => edge;
        set { edge = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius
    {
        get => radius;
        set { radius = value; Invalidate(); }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var outside = new SolidBrush(Parent?.BackColor ?? DesktopTheme.Background);
        e.Graphics.FillRectangle(outside, ClientRectangle);
        DesktopTheme.PaintRounded(e.Graphics, ClientRectangle, radius * DeviceDpi / 96, BackColor, edge);
    }
}

// The large on/off control of the Home page. The caption is only for screen readers; the icon carries the meaning.
internal sealed class PowerButton : Button
{
    private bool on;
    private bool hovering;

    public PowerButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        Margin = new Padding(0);
        Size = new Size(128, 128);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool On
    {
        get => on;
        set { on = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        using (var outside = new SolidBrush(Parent?.BackColor ?? DesktopTheme.Surface))
            graphics.FillRectangle(outside, ClientRectangle);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var side = Math.Min(Width, Height) - 2;
        var circle = new Rectangle((Width - side) / 2, (Height - side) / 2, side, side);
        var ring = Math.Max(4, side / 16);
        using (var halo = new SolidBrush(on && Enabled ? DesktopTheme.AmberWash : DesktopTheme.Background))
            graphics.FillEllipse(halo, circle);
        var core = Rectangle.Inflate(circle, -ring, -ring);
        var fill = !Enabled ? DesktopTheme.Raised : on ? hovering ? DesktopTheme.AccentHover : DesktopTheme.Accent :
            hovering ? DesktopTheme.Hover : DesktopTheme.Raised;
        using (var brush = new SolidBrush(fill)) graphics.FillEllipse(brush, core);
        using (var border = new Pen(on && Enabled ? DesktopTheme.Accent : DesktopTheme.InputBorder, Math.Max(1f, DeviceDpi / 96f)))
            graphics.DrawEllipse(border, core);

        var ink = !Enabled ? DesktopTheme.Disabled : on ? DesktopTheme.AccentInk : DesktopTheme.Text;
        var radius = core.Width * .2f;
        var cx = core.X + core.Width / 2f;
        var cy = core.Y + core.Height / 2f + radius * .1f;
        using var pen = new Pen(ink, Math.Max(2.5f, core.Width * .055f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawArc(pen, cx - radius, cy - radius, radius * 2, radius * 2, -60, 300);
        graphics.DrawLine(pen, cx, cy - radius * 1.25f, cx, cy - radius * .2f);

        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(DesktopTheme.Accent, Math.Max(2f, DeviceDpi * 2f / 96)) { DashStyle = DashStyle.Dot };
            graphics.DrawEllipse(focus, Rectangle.Inflate(circle, -1, -1));
        }
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovering = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovering = false; Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
}

// On/off switch used for each app. It stays a CheckBox, so keyboard and screen readers treat it as one.
internal sealed class ToggleSwitch : CheckBox
{
    public ToggleSwitch()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(52, 30);
        Cursor = Cursors.Hand;
        Margin = new Padding(0);
        AutoSize = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        using (var outside = new SolidBrush(Parent?.BackColor ?? DesktopTheme.Surface))
            graphics.FillRectangle(outside, ClientRectangle);
        var height = Math.Min(Height - 2, Width / 2 + 2);
        var track = new Rectangle(1, (Height - height) / 2, Width - 2, height);
        DesktopTheme.PaintRounded(graphics, track, height / 2, Checked && Enabled ? DesktopTheme.Accent : DesktopTheme.Border);
        var inset = Math.Max(3, height / 8);
        var knob = height - inset * 2;
        var left = Checked ? track.Right - inset - knob : track.Left + inset;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var brush = new SolidBrush(!Enabled ? DesktopTheme.Disabled : Checked ? DesktopTheme.AccentInk : DesktopTheme.Muted))
            graphics.FillEllipse(brush, left, track.Top + inset, knob, knob);
        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(DesktopTheme.Text) { DashStyle = DashStyle.Dot };
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.DrawRectangle(focus, 0, 0, Width - 1, Height - 1);
        }
    }

    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
}

// Pill that turns one app on or off from the Home page.
internal sealed class ChipToggle : CheckBox
{
    private bool hovering;

    public ChipToggle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = true;
        Cursor = Cursors.Hand;
        Font = DesktopTheme.Body(10, bold: true);
        Margin = new Padding(0, 0, 10, 10);
        MinimumSize = new Size(0, 40);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        var unit = DeviceDpi / 96f;
        return new Size(text.Width + (int)(46 * unit), Math.Max(MinimumSize.Height, text.Height + (int)(18 * unit)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        using (var outside = new SolidBrush(Parent?.BackColor ?? DesktopTheme.Surface))
            graphics.FillRectangle(outside, ClientRectangle);
        var fill = Checked ? DesktopTheme.AmberWash : hovering ? DesktopTheme.Hover : DesktopTheme.Raised;
        DesktopTheme.PaintRounded(graphics, ClientRectangle, Height / 2, fill, Checked ? DesktopTheme.Accent : DesktopTheme.Border);
        var unit = DeviceDpi / 96f;
        var dot = (int)(8 * unit);
        var left = (int)(16 * unit);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var brush = new SolidBrush(Checked ? DesktopTheme.Accent : DesktopTheme.InputBorder))
            graphics.FillEllipse(brush, left, (Height - dot) / 2, dot, dot);
        var caption = new Rectangle(left + dot + (int)(8 * unit), 0, Width - left - dot - (int)(20 * unit), Height);
        DesktopTheme.PaintText(graphics, Text, Font, caption, Checked ? DesktopTheme.Text : DesktopTheme.Muted);
        if (Focused && ShowFocusCues)
        {
            var inset = Math.Max(2, (int)(3 * unit));
            using var shape = DesktopTheme.Rounded(Rectangle.Inflate(ClientRectangle, -inset, -inset), Height / 2 - inset);
            using var focus = new Pen(DesktopTheme.Text) { DashStyle = DashStyle.Dot };
            graphics.DrawPath(focus, shape);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovering = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovering = false; Invalidate(); }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
}

// Radio dot for the server cards. AutoCheck is off: the form decides which card is chosen.
internal sealed class ThemeRadio : RadioButton
{
    public ThemeRadio()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer, true);
        AutoCheck = false;
        AutoSize = false;
        Size = new Size(28, 28);
        Cursor = Cursors.Hand;
        Margin = new Padding(0);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        using (var outside = new SolidBrush(Parent?.BackColor ?? DesktopTheme.Surface))
            graphics.FillRectangle(outside, ClientRectangle);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var side = Math.Min(Width, Height) * 3 / 4;
        var circle = new Rectangle((Width - side) / 2, (Height - side) / 2, side, side);
        using (var pen = new Pen(Checked ? DesktopTheme.Accent : DesktopTheme.InputBorder, Math.Max(2f, DeviceDpi * 2f / 96)))
            graphics.DrawEllipse(pen, circle);
        if (Checked)
        {
            var inner = Rectangle.Inflate(circle, -side / 4, -side / 4);
            using var dot = new SolidBrush(DesktopTheme.Accent);
            graphics.FillEllipse(dot, inner);
        }
        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(DesktopTheme.Text) { DashStyle = DashStyle.Dot };
            graphics.DrawEllipse(focus, Rectangle.Inflate(circle, 2, 2));
        }
    }

    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
}

// Short label on a rounded tile: app initials, a server's country code or a status mark.
internal sealed class Badge : Label
{
    private Color fill;

    public Badge(Color fill, Color ink)
    {
        this.fill = fill;
        ForeColor = ink;
        SetStyle(ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = false;
        Size = new Size(44, 44);
        Margin = new Padding(0);
        Font = DesktopTheme.Heading(13);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Fill
    {
        get => fill;
        set { fill = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using (var outside = new SolidBrush(Parent?.BackColor ?? DesktopTheme.Surface))
            e.Graphics.FillRectangle(outside, ClientRectangle);
        DesktopTheme.PaintRounded(e.Graphics, ClientRectangle, 10 * DeviceDpi / 96, fill);
        DesktopTheme.PaintText(e.Graphics, Text, Font, ClientRectangle, ForeColor, StringAlignment.Center);
    }
}

// Latency as a short bar: full and green when close, shorter and amber when far, empty when unknown.
internal sealed class LatencyBar : Control
{
    private long? milliseconds;

    public LatencyBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        TabStop = false;
        Margin = new Padding(0);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public long? Milliseconds
    {
        get => milliseconds;
        set { milliseconds = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using (var outside = new SolidBrush(Parent?.BackColor ?? DesktopTheme.Surface))
            e.Graphics.FillRectangle(outside, ClientRectangle);
        var height = Math.Max(4, DeviceDpi * 6 / 96);
        var track = new Rectangle(0, (Height - height) / 2, Width, height);
        DesktopTheme.PaintRounded(e.Graphics, track, height / 2, DesktopTheme.Border);
        if (milliseconds is not { } ms) return;
        var share = Math.Clamp(1 - ms / 300f, .08f, 1f);
        var color = ms < 60 ? DesktopTheme.Green : DesktopTheme.Amber;
        DesktopTheme.PaintRounded(e.Graphics, new Rectangle(track.X, track.Y, Math.Max(height, (int)(track.Width * share)), height),
            height / 2, color);
    }
}
