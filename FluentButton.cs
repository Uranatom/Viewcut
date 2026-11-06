using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace Viewcut;

internal sealed class FluentButton : Button
{
    private bool hover;
    private bool pressed;
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
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
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
        if (Glyph is not null)
        {
            using var font = new Font("Segoe Fluent Icons", 16 * scale, GraphicsUnit.Pixel);
            TextRenderer.DrawText(e.Graphics, Glyph, font, ClientRectangle, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        else
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var scale = DeviceDpi / 96f;
        var text = Glyph is null
            ? TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix)
            : new Size((int)Math.Ceiling(18 * scale), (int)Math.Ceiling(18 * scale));
        return new Size(Math.Max(MinimumSize.Width, text.Width + Padding.Horizontal),
            Math.Max((int)Math.Ceiling(32 * scale), text.Height + Padding.Vertical));
    }
}
