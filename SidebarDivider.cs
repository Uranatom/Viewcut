namespace Viewcut;

internal sealed class SidebarDivider : Control
{
    private readonly TableLayoutPanel layout;
    private bool dragging;
    private readonly Action<double> resized;

    public SidebarDivider(TableLayoutPanel layout, Action<double> resized)
    {
        this.layout = layout;
        this.resized = resized;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        Cursor = Cursors.SizeWE;
        BackColor = EditorTheme.Background;
        AccessibleName = "调整侧边栏宽度";
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        dragging = Capture = true;
        BackColor = EditorTheme.Border;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging) return;
        var point = layout.PointToClient(PointToScreen(e.Location));
        var minimum = Math.Max(330 * DeviceDpi / 96d, layout.ClientSize.Width * 0.28);
        var width = Math.Clamp(layout.ClientSize.Width - point.X, minimum,
            Math.Max(minimum, Math.Min(layout.ClientSize.Width * 0.62, layout.ClientSize.Width - 360 * DeviceDpi / 96d)));
        var fraction = width / Math.Max(1, layout.ClientSize.Width - Width);
        layout.ColumnStyles[0].Width = (float)(100 * (1 - fraction));
        layout.ColumnStyles[2].Width = (float)(100 * fraction);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!dragging) return;
        dragging = Capture = false;
        BackColor = EditorTheme.Background;
        resized(layout.ColumnStyles[2].Width / 100);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (Capture) return;
        dragging = false;
        BackColor = EditorTheme.Background;
    }
}
