using System.Globalization;

namespace Viewcut;

internal sealed class FramePreview : Form
{
    private readonly Panel video = new() { Dock = DockStyle.Fill, BackColor = Color.Black };
    private readonly Label clock = new()
    {
        Dock = DockStyle.Bottom,
        Height = 28,
        Padding = new Padding(8, 0, 8, 0),
        TextAlign = ContentAlignment.MiddleLeft
    };
    private Player? player;
    private string? source;
    private string? loaded;
    private double desired;
    private double issued;
    private long nextSeek;

    public double? FramePosition => player is { HasFrame: true, Seeking: false, Error: null } && source == loaded
        ? player.Position : null;

    public FramePreview()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(228, 157);
        Padding = new Padding(1);
        BackColor = EditorTheme.Border;
        ForeColor = EditorTheme.Text;
        clock.BackColor = EditorTheme.Surface;
        AccessibleName = "时间轴画面预览";
        Controls.Add(video);
        Controls.Add(clock);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x08000000 | 0x00000080; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
            return parameters;
        }
    }

    public void Request(Form owner, string path, double position, Point anchor)
    {
        source = path;
        desired = position;
        var area = Screen.FromPoint(anchor).WorkingArea;
        Location = new Point(
            Math.Clamp(anchor.X - Width / 2, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Clamp(anchor.Y - Height - 6, area.Top, Math.Max(area.Top, area.Bottom - Height)));
        if (Visible) return;
        nextSeek = Environment.TickCount64 + 55;
        clock.Text = "正在定位画面…";
        Opacity = owner.Opacity;
        Show(owner);
    }

    public void UpdateFrame()
    {
        if (!Visible || source is null) return;
        var now = Environment.TickCount64;
        try
        {
            if (source != loaded && now >= nextSeek)
            {
                player ??= new Player(video.Handle, preview: true);
                player.Open(source, desired);
                loaded = source;
                issued = desired;
            }
            if (player is not { } engine || source != loaded) return;
            engine.Poll();
            if (engine.Error is not null)
            {
                clock.Text = "无法预览此位置";
                return;
            }
            if (engine.HasFrame && !engine.Seeking && now >= nextSeek && Math.Abs(issued - desired) >= 0.02)
            {
                issued = desired;
                engine.Seek(desired, exact: true);
                nextSeek = now + 65;
            }
            if (FramePosition is { } position)
                clock.Text = "预览  " + TimeSpan.FromSeconds(position).ToString(@"hh\:mm\:ss\.ff", CultureInfo.InvariantCulture);
        }
        catch (InvalidOperationException)
        {
            clock.Text = "无法预览此位置";
            nextSeek = now + 500;
        }
    }

    public void ResetMedia()
    {
        Hide();
        player?.Dispose();
        player = null;
        source = loaded = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) player?.Dispose();
        base.Dispose(disposing);
    }
}
