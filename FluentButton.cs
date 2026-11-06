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
        Padding = new Padding(9, 0, 9, 0);
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
        path.AddRoundedRectangle(rect, new SizeF(4, 4));
        var background = !Enabled ? EditorTheme.Raised : Emphasized ? EditorTheme.Accent
            : pressed ? EditorTheme.Surface : hover ? Color.FromArgb(57, 57, 57) : BackColor;
        using var fill = new SolidBrush(background);
        using var border = new Pen(Focused ? EditorTheme.Accent : EditorTheme.Border);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
        var color = !Enabled ? EditorTheme.Muted : Emphasized ? EditorTheme.Surface : EditorTheme.Text;
        if (Glyph is not null)
        {
            using var font = new Font("Segoe Fluent Icons", 11);
            TextRenderer.DrawText(e.Graphics, Glyph, font, ClientRectangle, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        else
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}
