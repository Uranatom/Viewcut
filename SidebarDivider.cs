namespace Viewcut;

internal sealed class SidebarDivider : Control
{
    private readonly TableLayoutPanel layout;
    private bool dragging;
    private readonly Action<int> move;
    private readonly Action finished;

    public SidebarDivider(TableLayoutPanel layout, bool horizontal, Action<int> move, Action finished)
    {
        this.layout = layout;
        this.move = move;
        this.finished = finished;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        Cursor = horizontal ? Cursors.SizeNS : Cursors.SizeWE;
        BackColor = EditorTheme.Background;
        AccessibleName = horizontal ? "调整时间轴高度" : "调整侧边栏宽度";
        Horizontal = horizontal;
    }

    private bool Horizontal { get; }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var pen = new Pen(EditorTheme.Border, Math.Max(1, LogicalToDeviceUnits(1)));
        if (Horizontal)
            e.Graphics.DrawLine(pen, 0, Height / 2, Width, Height / 2);
        else
            e.Graphics.DrawLine(pen, Width / 2, 0, Width / 2, Height);
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
        move(Horizontal ? point.Y : point.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!dragging) return;
        dragging = Capture = false;
        BackColor = EditorTheme.Background;
        finished();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (Capture) return;
        dragging = false;
        BackColor = EditorTheme.Background;
    }
}
