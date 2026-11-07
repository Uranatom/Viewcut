using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace Viewcut;

internal sealed class FluentButton : Button
{
    private bool hover;
    private bool pressed;
    private readonly ToolTip tips = new() { InitialDelay = 400, ReshowDelay = 100, AutoPopDelay = 6500, ShowAlways = true, OwnerDraw = true };
    private string? hint;
    private Control? tipParent;
    private bool disabledTipVisible;
    [DefaultValue(null)]
    public string? Hint { get => hint; set { hint = value; UpdateTip(); } }
    public string Icon => Glyph ?? ButtonIcons.Resolve(Text);
    public string ToolTipText => Hint ?? ButtonIcons.Label(Text, AccessibleName);
    [DefaultValue(false)]
    public bool Emphasized { get; set; }
    [DefaultValue(null)]
    public string? Glyph { get; init; }

    public FluentButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = EditorTheme.Raised;
        ForeColor = EditorTheme.Text;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12, 6, 12, 6);
        Margin = new Padding(0, 3, 6, 3);
        MinimumSize = new Size(32, 30);
        Height = 30;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        tips.Popup += (_, e) =>
        {
            var scale = DeviceDpi / 96f;
            var measured = TextRenderer.MeasureText(ToolTipText, Font);
            e.ToolTipSize = new Size(measured.Width + (int)(20 * scale), measured.Height + (int)(12 * scale));
        };
        tips.Draw += (_, e) =>
        {
            using var fill = new SolidBrush(EditorTheme.Raised);
            using var border = new Pen(EditorTheme.Border);
            e.Graphics.FillRectangle(fill, e.Bounds);
            e.Graphics.DrawRectangle(border, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
            TextRenderer.DrawText(e.Graphics, ToolTipText, Font, e.Bounds, EditorTheme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };
        UpdateTip();
    }

    private void UpdateTip() { if (tips is not null && !IsDisposed) tips.SetToolTip(this, ToolTipText); }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); UpdateTip(); Invalidate(); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); UpdateTip(); }
    protected override void OnMouseEnter(EventArgs e) { UpdateTip(); hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? EditorTheme.Background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = new GraphicsPath();
        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        var scale = DeviceDpi / 96f;
        path.AddRoundedRectangle(rect, new SizeF(5 * scale, 5 * scale));
        var background = !Enabled ? EditorTheme.Raised : Emphasized ? EditorTheme.Accent
            : pressed ? EditorTheme.Surface : hover ? EditorTheme.Hover : BackColor;
        using var fill = new SolidBrush(background);
        using var border = new Pen(Focused ? EditorTheme.Text : pressed ? EditorTheme.Surface : EditorTheme.Border, scale);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
        var color = !Enabled ? EditorTheme.Muted : Emphasized ? EditorTheme.OnAccent : EditorTheme.Text;
        var iconBounds = new Rectangle(0, 0, Math.Min(Width, (int)Math.Ceiling(36 * scale)), Height);
        if (Icon is "mark-in" or "mark-out")
        {
            using var pen = new Pen(color, 1.5f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            var x = iconBounds.Width / 2f;
            var y = Height / 2f;
            var direction = Icon == "mark-in" ? -1 : 1;
            e.Graphics.DrawLines(pen, [new PointF(x - direction * 3 * scale, y - 7 * scale), new PointF(x + direction * 3 * scale, y - 7 * scale),
                new PointF(x + direction * 3 * scale, y + 7 * scale), new PointF(x - direction * 3 * scale, y + 7 * scale)]);
        }
        else
        {
            using var font = new Font(UiFonts.Icons, 16 * scale, GraphicsUnit.Pixel);
            TextRenderer.DrawText(e.Graphics, UiFonts.Glyph(Icon), font, iconBounds, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        if (ButtonIcons.Badge(Text) is { } badge)
        {
            using var font = new Font(UiFonts.Text, 9 * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            var size = TextRenderer.MeasureText(badge, font, Size.Empty, TextFormatFlags.NoPadding);
            var bounds = new Rectangle(Width - size.Width - (int)(3 * scale), (int)(2 * scale), size.Width + (int)(2 * scale), (int)(12 * scale));
            using var badgeFill = new SolidBrush(Emphasized ? EditorTheme.Surface : EditorTheme.Accent);
            e.Graphics.FillRectangle(badgeFill, bounds);
            TextRenderer.DrawText(e.Graphics, badge, font, bounds, Emphasized ? EditorTheme.Accent : EditorTheme.OnAccent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var scale = DeviceDpi / 96f;
        var badge = ButtonIcons.Badge(Text);
        var width = (int)Math.Ceiling((badge is null ? 36 : 44 + (badge.Length - 1) * 5) * scale);
        return new Size(Math.Max(MinimumSize.Width, width), Math.Max(MinimumSize.Height, (int)Math.Ceiling(36 * scale)));
    }

    protected override void OnParentChanged(EventArgs e)
    {
        if (tipParent is not null) { tipParent.MouseMove -= ParentMouseMove; tipParent.MouseLeave -= ParentMouseLeave; }
        base.OnParentChanged(e);
        tipParent = Parent;
        if (tipParent is not null) { tipParent.MouseMove += ParentMouseMove; tipParent.MouseLeave += ParentMouseLeave; }
    }

    private void ParentMouseMove(object? sender, MouseEventArgs e)
    {
        if (Enabled || tipParent is null) return;
        var inside = Bounds.Contains(e.Location);
        if (inside && !disabledTipVisible)
        { tips.Show(ToolTipText, tipParent, e.X + 12, e.Y + 20, 6500); disabledTipVisible = true; }
        else if (!inside && disabledTipVisible) { tips.Hide(tipParent); disabledTipVisible = false; }
    }

    private void ParentMouseLeave(object? sender, EventArgs e)
    {
        if (disabledTipVisible && tipParent is not null) tips.Hide(tipParent);
        disabledTipVisible = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (tipParent is not null) { tipParent.MouseMove -= ParentMouseMove; tipParent.MouseLeave -= ParentMouseLeave; }
            tips.Dispose();
        }
        base.Dispose(disposing);
    }
}
