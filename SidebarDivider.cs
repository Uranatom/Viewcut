namespace Viewcut;

internal sealed class SidebarDivider : Control
{
    private readonly TableLayoutPanel layout;
    private bool dragging;
    private bool changed;
    private int grabOffset;
    private int? pendingPosition;
    private int lastPosition;
    private readonly System.Windows.Forms.Timer frame = new() { Interval = 15 };
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
        AccessibleName = horizontal ? L.T("调整时间轴高度") : L.T("调整侧边栏宽度");
        Horizontal = horizontal;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        frame.Tick += (_, _) => ApplyPendingPosition();
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
        if (dragging) return;
        grabOffset = Horizontal ? e.Y : e.X;
        lastPosition = Horizontal ? Top : Left;
        pendingPosition = null;
        changed = false;
        dragging = Capture = true;
        BackColor = EditorTheme.Border;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging) return;
        QueuePosition(e.Location);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!dragging || e.Button != MouseButtons.Left) return;
        QueuePosition(e.Location);
        EndDrag();
        Capture = false;
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture && dragging) EndDrag();
    }

    private void QueuePosition(Point location)
    {
        var point = layout.PointToClient(PointToScreen(location));
        pendingPosition = (Horizontal ? point.Y : point.X) - grabOffset;
        if (!frame.Enabled) frame.Start();
    }

    private void ApplyPendingPosition()
    {
        frame.Stop();
        if (pendingPosition is not { } position) return;
        pendingPosition = null;
        if (position == lastPosition) return;
        var before = Horizontal ? Top : Left;
        move(position);
        lastPosition = position;
        changed |= before != (Horizontal ? Top : Left);
    }

    private void EndDrag()
    {
        dragging = false;
        ApplyPendingPosition();
        BackColor = EditorTheme.Background;
        if (changed) finished();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            dragging = false;
            frame.Dispose();
        }
        base.Dispose(disposing);
    }
}
