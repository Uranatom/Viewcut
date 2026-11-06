using System.Drawing.Drawing2D;
using System.Globalization;

namespace Viewcut;

internal sealed record ClipRange(Guid Id, int Number, double Start, double End, string? Name = null)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"片段 {Number}" : Name;
}

internal sealed class ClipTimeline : Control
{
    private static readonly Color[] ClipColors =
    [
        EditorTheme.Accent, Color.FromArgb(148, 188, 246), Color.FromArgb(221, 169, 240),
        Color.FromArgb(241, 190, 128)
    ];
    private const double MinClip = 0.05;

    private IReadOnlyList<ClipRange> clips = [];
    private double duration;
    private double position;
    private double? mark;
    private double? hover;
    private double viewStart;
    private double viewLength;
    private ClipRange? original;
    private ClipRange? edit;
    private bool startHandle;
    private bool scrubbing;
    private int dragOrigin;
    private readonly ContextMenuStrip clipMenu = EditorTheme.ContextMenu();
    private Guid renameTarget;

    public Guid? Selection { get; private set; }
    public event Action<Guid>? ClipSelected;
    public event Action<ClipRange>? ClipAdjusted;
    public event Action<Guid>? RenameRequested;
    public event Action<double>? SeekRequested;
    public event Action<double?, Point>? PreviewRequested;

    public ClipTimeline()
    {
        Dock = DockStyle.Fill;
        BackColor = EditorTheme.Surface;
        ForeColor = EditorTheme.Text;
        AccessibleName = "片段时间轴";
        AccessibleDescription = "点击定位，悬停预览。选中片段后拖动左右手柄。滚轮缩放，Shift 加滚轮平移。";
        TabStop = true;
        AutoSize = true;
        MinimumSize = new Size(120, 106);
        SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        clipMenu.Items.Add("重命名", null, (_, _) => RenameRequested?.Invoke(renameTarget));
    }

    public void SetMedia(double value)
    {
        CancelGesture();
        duration = viewLength = Math.Max(0, value);
        viewStart = position = 0;
        mark = null;
        Invalidate();
    }

    public void UpdateCursor(double value, double? marker)
    {
        var redraw = Math.Round(X(position)) != Math.Round(X(value)) || mark != marker;
        position = value;
        mark = marker;
        if (redraw) Invalidate();
    }

    public void SetClips(IReadOnlyList<ClipRange> ranges, Guid? selected)
    {
        clips = ranges;
        Selection = selected;
        Invalidate();
    }

    public void Fit()
    {
        viewStart = 0;
        viewLength = duration;
        Invalidate();
    }

    public void Reveal(ClipRange clip)
    {
        if (clip.Start >= viewStart && clip.End <= viewStart + viewLength) return;
        viewStart = Math.Clamp((clip.Start + clip.End - viewLength) / 2, 0, Math.Max(0, duration - viewLength));
        Invalidate();
    }

    public void Zoom(double factor) => ZoomAt(factor,
        position >= viewStart && position <= viewStart + viewLength ? X(position) : Track.Left + Track.Width / 2d);

    private void ZoomAt(double factor, double x)
    {
        if (duration <= 0 || original is not null || scrubbing) return;
        var fraction = Math.Clamp((x - Track.Left) / Math.Max(1, Track.Width), 0, 1);
        var anchor = viewStart + viewLength * fraction;
        viewLength = Math.Clamp(viewLength * factor, Math.Min(duration, 0.8), duration);
        viewStart = Math.Clamp(anchor - viewLength * fraction, 0, duration - viewLength);
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (duration <= 0 || original is not null || scrubbing) return;
        if ((ModifierKeys & Keys.Shift) != 0)
            viewStart = Math.Clamp(viewStart - viewLength * e.Delta / 480, 0, duration - viewLength);
        else
            ZoomAt(Math.Pow(0.75, e.Delta / 120d), e.X);
        MovePointer(e.Location);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Right && HitClip(e.Location, out _) is { } target)
        {
            Selection = target.Id;
            ClipSelected?.Invoke(target.Id);
            renameTarget = target.Id;
            clipMenu.Show(this, e.Location);
            return;
        }
        if (e.Button != MouseButtons.Left || duration <= 0) return;
        Focus();
        var clip = HitClip(e.Location, out var edge);
        if (clip is not null)
        {
            Selection = clip.Id;
            ClipSelected?.Invoke(clip.Id);
        }

        if (clip is not null && edge is { } leading)
        {
            original = edit = clip;
            startHandle = leading;
            dragOrigin = e.X;
        }
        else
        {
            scrubbing = true;
        }
        Capture = true;
        MovePointer(e.Location);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (duration > 0) MovePointer(e.Location);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || (original is null && !scrubbing)) return;
        MovePointer(e.Location);
        var seek = scrubbing;
        var adjusted = edit;
        var before = original;
        original = edit = null;
        scrubbing = false;
        Capture = false;
        if (seek)
            SeekRequested?.Invoke(At(e.X));
        else if (adjusted is not null && adjusted != before)
            ClipAdjusted?.Invoke(adjusted);

        if (!ClientRectangle.Contains(e.Location)) ClearHover();
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (original is null && !scrubbing) ClearHover();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture && (original is not null || scrubbing)) CancelGesture();
    }

    public bool CancelGesture()
    {
        var active = original is not null || scrubbing;
        original = edit = null;
        scrubbing = false;
        if (Capture) Capture = false;
        ClearHover();
        return active;
    }

    private void MovePointer(Point point)
    {
        var value = At(point.X);
        if (original is { } clip)
        {
            var delta = (point.X - dragOrigin) * viewLength / Math.Max(1, Track.Width);
            edit = startHandle
                ? clip with { Start = Math.Clamp(clip.Start + delta, 0, Math.Max(0, clip.End - MinClip)) }
                : clip with { End = Math.Clamp(clip.End + delta, Math.Min(duration, clip.Start + MinClip), duration) };
            value = startHandle ? edit.Start : edit.End;
            Cursor = Cursors.SizeWE;
        }
        else
        {
            HitClip(point, out var edge);
            Cursor = edge.HasValue ? Cursors.SizeWE : Cursors.Hand;
        }

        hover = value;
        PreviewRequested?.Invoke(value, PointToScreen(new Point((int)Math.Clamp(X(value), Track.Left, Track.Right), 0)));
        Invalidate();
    }

    private void ClearHover()
    {
        if (hover is null) return;
        hover = null;
        Cursor = Cursors.Default;
        PreviewRequested?.Invoke(null, Point.Empty);
        Invalidate();
    }

    private ClipRange? HitClip(Point point, out bool? edge)
    {
        edge = null;
        if (!Track.Contains(point)) return null;
        ClipRange? hit = null;
        for (var index = clips.Count - 1; index >= 0; index--)
        {
            var clip = clips[index];
            if (hit is not null && clip.Id != Selection) continue;
            var left = X(clip.Start);
            var right = X(clip.End);
            var toStart = Math.Abs(point.X - left);
            var toEnd = Math.Abs(point.X - right);
            var overEdge = Math.Min(toStart, toEnd) <= Scale(7);
            if (!overEdge && (point.X < left || point.X > right)) continue;
            hit = clip;
            edge = overEdge ? toStart <= toEnd : null;
            if (clip.Id == Selection) break;
        }
        return hit;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = Track;
        using var trackBrush = new SolidBrush(EditorTheme.Background);
        using var line = new Pen(EditorTheme.Border);
        g.FillRectangle(trackBrush, track);
        using var outline = new Pen(Focused ? EditorTheme.Accent : EditorTheme.Border);
        g.DrawRectangle(outline, track);

        if (duration <= 0)
        {
            DrawText(g, "打开素材后，用 I / O 标记要保留的片段", track, EditorTheme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        DrawRuler(g, line, track);
        var state = g.Save();
        g.SetClip(Rectangle.Inflate(track, 0, -1));
        foreach (var clip in clips.Where(range => range.Id != Selection))
            DrawClip(g, clip, false);
        if (clips.FirstOrDefault(range => range.Id == Selection) is { } selected)
            DrawClip(g, edit ?? selected, true);
        g.Restore(state);

        if (mark is { } value) DrawPointer(g, value, EditorTheme.Marker, track.Bottom, false);
        DrawPointer(g, position, EditorTheme.Text, track.Bottom, true);
        if (hover is { } preview) DrawPointer(g, preview, EditorTheme.Accent, track.Bottom, false);

        var overview = new RectangleF(track.Left, track.Bottom + Scale(9), track.Width, Scale(7));
        g.FillRectangle(trackBrush, overview);
        using var overviewClip = new SolidBrush(Color.FromArgb(92, EditorTheme.Accent));
        foreach (var clip in clips)
            g.FillRectangle(overviewClip, overview.X + (float)(clip.Start / duration * overview.Width), overview.Y,
                Math.Max(1, (float)((clip.End - clip.Start) / duration * overview.Width)), overview.Height);
        g.DrawRectangle(line, overview.X + (float)(viewStart / duration * overview.Width), overview.Y,
            (float)(viewLength / duration * overview.Width), overview.Height);
    }

    private void DrawRuler(Graphics g, Pen line, Rectangle track)
    {
        var minimum = viewLength * Scale(70) / Math.Max(1, track.Width);
        var decade = Math.Pow(10, Math.Floor(Math.Log10(minimum)));
        var step = (minimum <= decade ? 1 : minimum <= 2 * decade ? 2 : minimum <= 5 * decade ? 5 : 10) * decade;
        var lastLabelEnd = int.MinValue;
        for (var tick = Math.Ceiling(viewStart / step) * step; tick <= viewStart + viewLength; tick += step)
        {
            var x = (int)X(tick);
            g.DrawLine(line, x, track.Top - Scale(5), x, track.Top);
            var clock = TimeSpan.FromSeconds(tick);
            var format = step < 1 ? @"mm\:ss\.ff" : duration >= 3600 ? @"hh\:mm\:ss" : @"mm\:ss";
            var label = clock.ToString(format, CultureInfo.InvariantCulture);
            var width = TextRenderer.MeasureText(g, label, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
            var left = Math.Min(x + Scale(3), track.Right - width);
            if (left < lastLabelEnd + Scale(8)) continue;
            DrawText(g, label, new Rectangle(left, 0, width + Scale(2), track.Top - Scale(5)), EditorTheme.Muted,
                TextFormatFlags.VerticalCenter);
            lastLabelEnd = left + width;
        }
    }

    private void DrawClip(Graphics g, ClipRange clip, bool selected)
    {
        var track = Track;
        var x = X(clip.Start);
        var end = X(clip.End);
        if (end < track.Left || x > track.Right) return;
        var left = (float)Math.Max(track.Left, x);
        var right = (float)Math.Min(track.Right, end);
        var rect = new RectangleF(left, track.Top + Scale(4), Math.Max(1, right - left), track.Height - Scale(8));
        var color = ClipColors[(clip.Number - 1) % ClipColors.Length];
        using var fill = new SolidBrush(Color.FromArgb(selected ? 205 : 97, color));
        using var border = new Pen(color, selected ? Scale(2) : 1);
        g.FillRectangle(fill, rect);
        g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
        if (rect.Width > Scale(35))
            DrawText(g, clip.DisplayName, Rectangle.Round(RectangleF.Inflate(rect, -Scale(9), 0)),
                selected ? EditorTheme.Background : EditorTheme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (!selected) return;
        using var grip = new SolidBrush(color);
        if (x >= track.Left) g.FillRectangle(grip, rect.Left, rect.Top, Math.Min(Scale(5), rect.Width), rect.Height);
        if (end <= track.Right) g.FillRectangle(grip, Math.Max(rect.Left, rect.Right - Scale(5)), rect.Top,
            Math.Min(Scale(5), rect.Width), rect.Height);
    }

    private void DrawPointer(Graphics g, double seconds, Color color, int bottom, bool head)
    {
        var x = (float)X(seconds);
        if (x < Track.Left || x > Track.Right) return;
        using var pen = new Pen(color, Scale(head ? 2 : 1));
        var y = Track.Top - Scale(5);
        g.DrawLine(pen, x, y, x, bottom);
        if (head)
        {
            using var brush = new SolidBrush(color);
            g.FillPolygon(brush, [new PointF(x - Scale(4), y), new PointF(x + Scale(4), y), new PointF(x, y + Scale(5))]);
        }
    }

    private void DrawText(Graphics g, string text, Rectangle bounds, Color color, TextFormatFlags flags) =>
        TextRenderer.DrawText(g, text, Font, bounds, color, flags | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

    public override Size GetPreferredSize(Size proposedSize) => new(
        MinimumSize.Width, Math.Max(MinimumSize.Height, RulerHeight + Font.Height + Scale(44)));

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    private int RulerHeight => Math.Max(Scale(28), Font.Height + Scale(12));
    private Rectangle Track => new(Scale(12), RulerHeight, Math.Max(1, Width - Scale(24)),
        Math.Max(Scale(32), Height - RulerHeight - Scale(25)));
    private double X(double seconds) => Track.Left + (viewLength > 0 ? (seconds - viewStart) * Track.Width / viewLength : 0);
    private double At(double x) => Math.Clamp(viewStart + (x - Track.Left) * viewLength / Track.Width, 0, duration);
    private int Scale(int size) => (int)Math.Round(size * DeviceDpi / 96d);

    protected override void Dispose(bool disposing)
    {
        if (disposing) clipMenu.Dispose();
        base.Dispose(disposing);
    }
}
