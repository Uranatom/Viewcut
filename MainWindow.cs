using System.Globalization;
using System.Runtime.InteropServices;

namespace Viewcut;

internal sealed class MainWindow : Form
{
    private readonly SessionStore store;
    private readonly Exporter exporter = new(AppContext.BaseDirectory);
    private readonly CancellationTokenSource shutdown = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 32 };
    private readonly Panel video = new() { Dock = DockStyle.Fill, BackColor = Color.Black };
    private readonly ClipTimeline timeline = new();
    private readonly FileBrowser browser = new();
    private readonly ListView clips = CreateList(("片段", 62), ("时间范围", 240));
    private readonly ListView jobs = CreateList(("状态", 72), ("导出范围", 230));
    private readonly Label time = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight };
    private readonly Label mediaName = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Text = "播放器" };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = EditorTheme.Muted };
    private readonly Button pause = Button("暂停");
    private readonly Button exportAll = Button("导出当前素材全部片段");
    private readonly Button retry = Button("重试选中任务");
    private readonly Button[] sectionButtons;
    private readonly Control[] sections;

    private Player? player;
    private FramePreview? preview;
    private Task? worker;
    private string? current;
    private SourceStamp? sourceStamp;
    private string[] sources = [];
    private ClipDraft[] clipDrafts = [];
    private ClipRange[] ranges = [];
    private Guid? selectedClip;
    private double? start;
    private double mediaDuration;
    private bool selectingClip;
    private bool opening;
    private bool closing;

    public MainWindow(SessionStore store, string? input)
    {
        this.store = store;
        Text = "Viewcut";
        ClientSize = new Size(1360, 800);
        MinimumSize = new Size(1024, 600);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI Variable Text", 9);
        BackColor = EditorTheme.Background;
        ForeColor = EditorTheme.Text;
        time.Font = new Font("Segoe UI Variable Text", 10);
        time.ForeColor = EditorTheme.Accent;
        time.BackColor = EditorTheme.Surface;
        time.Padding = new Padding(8, 0, 8, 0);
        time.Text = "00:00:00.00 / 00:00:00.00";
        mediaName.Padding = new Padding(11, 0, 11, 0);

        var fraction = double.IsFinite(store.Data.SidebarFraction) ? Math.Clamp(store.Data.SidebarFraction, 0.28, 0.62) : 0.4;
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 3, Margin = Padding.Empty,
            AccessibleName = "播放器与文件浏览器布局" };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, (float)(100 * (1 - fraction))));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 7));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, (float)(100 * fraction)));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var playback = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
            BackColor = EditorTheme.Surface, Margin = Padding.Empty };
        playback.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        playback.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        video.Margin = Padding.Empty;
        video.Paint += DrawEmptyVideo;
        playback.Controls.Add(mediaName, 0, 0);
        playback.Controls.Add(video, 0, 1);
        split.Controls.Add(playback, 0, 0);

        var exportPage = new Panel { Dock = DockStyle.Fill };
        exportPage.Controls.Add(jobs);
        retry.Dock = DockStyle.Bottom;
        exportPage.Controls.Add(retry);
        sections = [browser, clips, exportPage];
        sectionButtons = [Button("文件", () => ShowSection(0)), Button("片段", () => ShowSection(1)),
            Button("导出", () => ShowSection(2))];
        var sectionNav = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false,
            Margin = Padding.Empty, Padding = new Padding(6, 2, 0, 0) };
        sectionNav.Controls.AddRange(sectionButtons);
        var sectionBody = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        sectionBody.Controls.AddRange(sections);
        var sidebar = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
            BackColor = EditorTheme.Surface, Margin = Padding.Empty };
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidebar.Controls.Add(sectionNav, 0, 0);
        sidebar.Controls.Add(sectionBody, 0, 1);
        split.Controls.Add(sidebar, 2, 0);
        split.Controls.Add(new SidebarDivider(split, value =>
        {
            store.Data.SidebarFraction = value;
            Save();
        }), 1, 0);
        ShowSection(0);

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        var wordmark = new Label { Text = "Viewcut", AutoSize = true, Font = new Font("Segoe UI Variable Display Semib", 16),
            Margin = new Padding(0, 2, 18, 0), ForeColor = EditorTheme.Text };
        toolbar.Controls.AddRange([wordmark, Button("选择保存目录", ChooseDestination), Button("打开保存目录", OpenDestination)]);

        var transport = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        exportAll.Click += (_, _) => ExportCurrent();
        Emphasize(exportAll, true);
        transport.Controls.AddRange([pause, Button("P  上一条", () => MoveFile(-1)), Button("N  下一条", () => MoveFile(1)),
            Button("I  起点", MarkStart), Button("O  终点", MarkEnd)]);
        var transportBar = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 2, Margin = Padding.Empty };
        transportBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        transportBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 236));
        transportBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        transportBar.Controls.Add(transport, 0, 0);
        transportBar.Controls.Add(time, 1, 0);

        var timelineHeader = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        timelineHeader.Controls.AddRange([
            new Label { Text = "片段时间轴", AutoSize = true, Margin = new Padding(12, 9, 15, 0), ForeColor = EditorTheme.Text },
            Button("−", () => timeline.Zoom(1.5)), Button("+", () => timeline.Zoom(0.67)), Button("全览", timeline.Fit), exportAll,
            new Label { Text = "悬停预览　·　滚轮缩放　·　Shift + 滚轮平移", AutoSize = true,
                ForeColor = EditorTheme.Muted, Margin = new Padding(12, 9, 0, 0) }
        ]);
        var timelinePanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
            BackColor = EditorTheme.Surface, Margin = Padding.Empty };
        timelinePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        timelinePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        timeline.Margin = Padding.Empty;
        timelinePanel.Controls.Add(timelineHeader, 0, 0);
        timelinePanel.Controls.Add(timeline, 0, 1);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 41));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 119));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(split, 0, 1);
        layout.Controls.Add(transportBar, 0, 2);
        layout.Controls.Add(timelinePanel, 0, 3);
        layout.Controls.Add(status, 0, 4);
        Controls.Add(layout);

        browser.SetView(store.Data.BrowserLargeIcons, notify: false);
        browser.VideoActivated += path =>
        {
            sources = browser.VideoFiles;
            OpenFile(path);
            timeline.Focus();
        };
        browser.PreferencesChanged += () =>
        {
            store.Data.BrowserFolder = browser.CurrentFolder;
            store.Data.BrowserLargeIcons = browser.LargeIcons;
            if (string.Equals(Path.GetDirectoryName(current), browser.CurrentFolder, StringComparison.OrdinalIgnoreCase))
                sources = browser.VideoFiles;
            Save();
        };
        jobs.RetrieveVirtualItem += (_, e) =>
        {
            var job = store.Data.Jobs[e.ItemIndex];
            e.Item = new ListViewItem([JobLabel(job.State), $"{Path.GetFileName(job.Source)}  {Clock(job.Start)}–{Clock(job.End)}"])
            {
                ToolTipText = job.Error ?? job.Output ?? job.Folder
            };
        };
        jobs.SelectedIndexChanged += (_, _) => UpdateRetry();
        retry.Click += (_, _) => RetrySelected();
        pause.Click += (_, _) => player?.TogglePause();
        clips.RetrieveVirtualItem += (_, e) =>
        {
            var clip = ranges[e.ItemIndex];
            e.Item = new ListViewItem([$"{clip.Number:D2}", $"{Clock(clip.Start)}  →  {Clock(clip.End)}"])
                { ToolTipText = ClipState(clipDrafts[e.ItemIndex]) + "  ·  " + Clock(clip.End - clip.Start) };
        };
        clips.SelectedIndexChanged += (_, _) =>
        {
            if (!selectingClip && clips.SelectedIndices.Count == 1)
                SelectClip(ranges[clips.SelectedIndices[0]].Id, reveal: true);
        };
        clips.DoubleClick += (_, _) =>
        {
            if (clips.SelectedIndices.Count == 1) player?.Seek(ranges[clips.SelectedIndices[0]].Start, true);
        };
        timeline.ClipSelected += id =>
        {
            SelectClip(id);
            ShowSection(1);
        };
        timeline.ClipAdjusted += AdjustClip;
        timeline.SeekRequested += seconds => player?.Seek(seconds, true);
        timeline.PreviewRequested += PreviewAt;
        Deactivate += (_, _) => timeline.CancelGesture();
        timer.Tick += (_, _) => UpdatePlayback();
        FormClosing += HandleClosing;
        Shown += (_, _) =>
        {
            timer.Start();
            RefreshJobs();
            SetStatus("在右侧浏览文件夹，双击视频开始粗筛。");
            if (input is not null) ActivateInput(input);
            else _ = browser.NavigateAsync(store.Data.BrowserFolder
                ?? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
            StartQueue();
        };
    }

    public void ActivateInput(string? path)
    {
        if (closing) return;
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        if (string.IsNullOrWhiteSpace(path)) return;
        if (Directory.Exists(path))
        {
            ShowSection(0);
            _ = browser.NavigateAsync(path);
            return;
        }
        if (!File.Exists(path) || !IsVideo(path))
        {
            SetStatus($"找不到可用的 MP4 或 MOV：{path}");
            return;
        }
        OpenFile(path);
        var parent = Path.GetDirectoryName(path)!;
        if (!string.Equals(browser.CurrentFolder, parent, StringComparison.OrdinalIgnoreCase))
            _ = browser.NavigateAsync(parent, path);
        else browser.SelectPath(path);
    }

    private static bool IsVideo(string path) => Path.GetExtension(path).ToLowerInvariant() is ".mp4" or ".mov";

    private void OpenFile(string path)
    {
        try
        {
            var stamp = SourceStamp.Read(path);
            player ??= new Player(video.Handle);
            player.Open(path);
            preview?.ResetMedia();
            current = path;
            sourceStamp = stamp;
            start = null;
            selectedClip = null;
            mediaDuration = 0;
            timeline.SetMedia(0);
            RefreshClips();
            opening = true;
            Text = $"Viewcut  ·  {Path.GetFileName(path)}";
            mediaName.Text = Path.GetFileName(path);
            SetStatus($"正在打开 {Path.GetFileName(path)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DllNotFoundException
            or BadImageFormatException or InvalidOperationException)
        {
            SetStatus(ex is DllNotFoundException ? "缺少 libmpv-2.dll，请用带依赖的测试版本。" : ex.Message);
        }
    }

    private void MoveFile(int offset)
    {
        var index = Array.FindIndex(sources, path => string.Equals(path, current, StringComparison.OrdinalIgnoreCase));
        index += offset;
        if (index < 0 || index >= sources.Length)
        {
            SetStatus("已到当前列表的边界。");
            return;
        }
        OpenFile(sources[index]);
        browser.SelectPath(current);
    }

    private void MarkStart()
    {
        if (!CanMark()) return;
        start = player!.Position;
        SetStatus($"已标记起点 {Clock(start.Value)}，按 O 标记终点。");
    }

    private void MarkEnd()
    {
        if (!CanMark()) return;
        var end = player!.Position;
        if (start is not { } from || end - from < 0.05)
        {
            SetStatus("先按 I 标记起点，再在更晚的画面按 O。");
            return;
        }
        var clip = new ClipDraft(current!, sourceStamp!, from, end);
        store.Data.Clips.Add(clip);
        if (!Save())
        {
            store.Data.Clips.RemoveAt(store.Data.Clips.Count - 1);
            return;
        }
        start = null;
        selectedClip = clip.Id;
        SetStatus($"已标记片段 {Clock(from)}–{Clock(end)}，选完后点击导出。");
        RefreshClips();
    }

    private bool CanMark()
    {
        if (player is { Ready: true, Seeking: false, Duration: > 0 } && sourceStamp is not null)
            return true;
        SetStatus("请等画面加载或定位完成后再标记。");
        return false;
    }

    private void ExportCurrent()
    {
        if (current is not { } path || sourceStamp is not { } stamp || clipDrafts.Length == 0) return;
        if (store.Data.Destination is null) ChooseDestination();
        if (store.Data.Destination is not { } folder || closing) return;
        var batch = store.Data.QueueExport(path, stamp, folder);
        if (!Save())
        {
            foreach (var job in batch) store.Data.Jobs.Remove(job);
            return;
        }
        SetStatus(batch.Length > 0 ? $"已提交当前素材的 {batch.Length} 个片段。无损快切的起点可能提前至关键帧。" : "这些片段已在队列中。");
        RefreshJobs();
        StartQueue();
    }

    private void StartQueue()
    {
        if (!closing && worker is not { IsCompleted: false })
            worker = ExportQueueAsync();
    }

    private async Task ExportQueueAsync()
    {
        while (!closing && store.Data.Jobs.FirstOrDefault(job => job.State == JobState.Waiting) is { } job)
        {
            job.State = JobState.Running;
            if (!Save())
            {
                job.State = JobState.Waiting;
                break;
            }
            RefreshJobs();
            try
            {
                job.Output = await Task.Run(() => exporter.ExportAsync(job, shutdown.Token));
                job.State = JobState.Done;
                SetStatus($"已保存 {Path.GetFileName(job.Output)}");
            }
            catch (OperationCanceledException) when (closing)
            {
                job.State = JobState.Waiting;
            }
            catch (Exception ex)
            {
                job.State = JobState.Failed;
                job.Error = ex.Message;
                SetStatus($"导出失败：{ex.Message}");
            }
            RefreshJobs();
            if (!Save()) break;
        }
    }

    private void RetrySelected()
    {
        if (jobs.SelectedIndices.Count != 1) return;
        var job = store.Data.Jobs[jobs.SelectedIndices[0]];
        if (job.State != JobState.Failed) return;
        var error = job.Error;
        job.State = JobState.Waiting;
        job.Error = null;
        if (!Save())
        {
            job.State = JobState.Failed;
            job.Error = error;
        }
        RefreshJobs();
        StartQueue();
    }

    private void UpdatePlayback()
    {
        preview?.UpdateFrame();
        if (player is not { } engine) return;
        engine.Poll();
        if (engine.Error is { } error)
        {
            if (opening) SetStatus($"播放失败：{error}");
            opening = false;
            return;
        }
        if (!engine.Ready) return;
        if (engine.Duration > 0 && engine.Duration != mediaDuration)
        {
            mediaDuration = engine.Duration;
            timeline.SetMedia(mediaDuration);
        }
        if (opening && engine.Position > 0)
        {
            opening = false;
            SetStatus("按 I / O 标记片段，选完后点击导出当前素材全部片段。");
        }
        pause.Text = engine.Paused ? "播放" : "暂停";
        timeline.UpdateCursor(engine.Position, start);
        time.Text = $"{Clock(engine.Position)} / {Clock(engine.Duration)}";
    }

    private void PreviewAt(double? seconds, Point anchor)
    {
        if (seconds is null || current is null || closing)
        {
            preview?.Hide();
            return;
        }
        preview ??= new FramePreview();
        var position = seconds.Value >= mediaDuration ? Math.Max(0, mediaDuration - 0.04) : seconds.Value;
        preview.Request(this, current, position, anchor);
    }

    private void AdjustClip(ClipRange edited)
    {
        var previous = clipDrafts.FirstOrDefault(clip => clip.Id == edited.Id);
        if (previous is null || edited.End <= edited.Start) return;
        var drafts = store.Data.Clips;
        var index = drafts.IndexOf(previous);
        var updated = previous with
        {
            Start = edited.Start,
            End = edited.End
        };
        drafts[index] = updated;
        if (!Save())
        {
            drafts[index] = previous;
            RefreshClips();
            return;
        }
        SetStatus($"已调整片段 {edited.Number}，点击导出应用当前范围。");
        RefreshClips();
    }

    private void RefreshClips()
    {
        selectingClip = true;
        clips.VirtualListSize = 0;
        clipDrafts = store.Data.LatestClips(current, sourceStamp);
        ranges = clipDrafts.Select((clip, index) => new ClipRange(clip.Id, index + 1, clip.Start, clip.End)).ToArray();
        clips.VirtualListSize = ranges.Length;
        selectingClip = false;
        sectionButtons[1].Text = ranges.Length == 0 ? "片段" : $"片段  {ranges.Length}";
        SelectClip(selectedClip);
        exportAll.Enabled = clipDrafts.Any(clip => store.Data.ExportFor(clip, store.Data.Destination)
            is not { State: JobState.Waiting or JobState.Running });
        exportAll.Text = ranges.Length == 0 ? "导出当前素材全部片段" : $"导出当前素材全部片段 ({ranges.Length})";
    }

    private void SelectClip(Guid? id, bool reveal = false)
    {
        selectedClip = id;
        timeline.SetClips(ranges, id);
        var index = Array.FindIndex(ranges, clip => clip.Id == id);
        if (index < 0) return;
        if (reveal) timeline.Reveal(ranges[index]);
        if (clips.SelectedIndices.Count == 1 && clips.SelectedIndices[0] == index) return;
        selectingClip = true;
        clips.SelectedIndices.Clear();
        clips.SelectedIndices.Add(index);
        clips.EnsureVisible(index);
        selectingClip = false;
    }

    private void ShowSection(int index)
    {
        for (var i = 0; i < sections.Length; i++)
        {
            sections[i].Visible = i == index;
            Emphasize(sectionButtons[i], i == index);
        }
    }

    private string ClipState(ClipDraft clip) => store.Data.ExportFor(clip, store.Data.Destination) is { } job
        ? JobLabel(job.State) : "待导出";

    private static string JobLabel(JobState state) => state switch
    {
        JobState.Waiting => "排队中",
        JobState.Running => "导出中",
        JobState.Done => "已保存",
        _ => "失败"
    };

    private void RefreshJobs()
    {
        jobs.VirtualListSize = store.Data.Jobs.Count;
        jobs.Invalidate();
        var active = store.Data.Jobs.Count(job => job.State is JobState.Waiting or JobState.Running);
        sectionButtons[2].Text = active == 0 ? "导出" : $"导出  {active}";
        UpdateRetry();
        RefreshClips();
    }

    private void UpdateRetry() => retry.Enabled = jobs.SelectedIndices.Count == 1
        && store.Data.Jobs[jobs.SelectedIndices[0]].State == JobState.Failed;

    private void ChooseDestination()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择片段保存目录",
            SelectedPath = store.Data.Destination ?? ""
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var before = store.Data.Destination;
        store.Data.Destination = dialog.SelectedPath;
        if (Save())
        {
            SetStatus("保存目录已设定。");
            RefreshClips();
        }
        else store.Data.Destination = before;
    }

    private void OpenDestination()
    {
        if (store.Data.Destination is not { } folder || !Directory.Exists(folder))
        {
            ChooseDestination();
            return;
        }
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
    }

    private bool Save()
    {
        try
        {
            store.Save();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"无法保存粗筛记录：{ex.Message}");
            return false;
        }
    }

    private async void HandleClosing(object? sender, FormClosingEventArgs e)
    {
        if (!closing && worker is { IsCompleted: false } pending)
        {
            closing = true;
            e.Cancel = true;
            shutdown.Cancel();
            await pending;
            Close();
            return;
        }
        if (worker is { IsCompleted: false })
        {
            e.Cancel = true;
            return;
        }
        closing = true;
        timer.Dispose();
        preview?.Dispose();
        player?.Dispose();
        shutdown.Dispose();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (closing) return true;
        if (browser.ContainsFocus) return base.ProcessCmdKey(ref msg, keyData);
        switch (keyData)
        {
            case Keys.Escape when timeline.CancelGesture(): return true;
            case Keys.Space when player is { Ready: true }: player.TogglePause(); return true;
            case Keys.I: MarkStart(); return true;
            case Keys.O: MarkEnd(); return true;
            case Keys.P: MoveFile(-1); return true;
            case Keys.N: MoveFile(1); return true;
            case Keys.Left when player is { Ready: true } left: left.Seek(left.Position - 3, true); return true;
            case Keys.Right when player is { Ready: true } right: right.Seek(right.Position + 3, true); return true;
            default: return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    private void SetStatus(string message)
    {
        status.Text = store.Data.Destination is { } folder ? $"{message}    ·    保存到 {folder}" : message;
        status.AccessibleDescription = status.Text;
    }

    private static string Clock(double seconds) => TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss\.ff", CultureInfo.InvariantCulture);

    private static Button Button(string label, Action? action = null)
    {
        var button = new FluentButton
        {
            Text = label
        };
        button.FlatAppearance.BorderColor = EditorTheme.Border;
        Emphasize(button, false);
        if (action is not null) button.Click += (_, _) => action();
        return button;
    }

    private static void Emphasize(Button button, bool selected)
    {
        if (button is FluentButton fluent) fluent.Emphasized = selected;
        button.Invalidate();
    }

    private void DrawEmptyVideo(object? sender, PaintEventArgs e)
    {
        if (current is not null) return;
        var center = video.ClientRectangle;
        center.Height = Math.Max(0, center.Height - 24);
        using var title = new Font("Segoe UI Variable Text", 16);
        const TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
        TextRenderer.DrawText(e.Graphics, "打开素材开始粗筛", title, center, EditorTheme.Text, flags);
        center.Offset(0, 32);
        TextRenderer.DrawText(e.Graphics, "在右侧文件浏览器中双击视频，然后用 I / O 标记片段。", Font, center, EditorTheme.Muted, flags);
    }

    private static ListView CreateList(params (string Label, int Width)[] columns)
    {
        var list = new ListView { Dock = DockStyle.Fill, VirtualMode = true, View = View.Details,
            FullRowSelect = true, MultiSelect = false, ShowItemToolTips = true, HideSelection = false,
            BackColor = EditorTheme.Surface, ForeColor = EditorTheme.Text, BorderStyle = BorderStyle.None,
            OwnerDraw = true };
        foreach (var column in columns) list.Columns.Add(column.Label, column.Width);
        list.SizeChanged += (_, _) =>
        {
            var reserved = columns.Take(columns.Length - 1).Sum(column => column.Width);
            list.Columns[^1].Width = Math.Max(columns[^1].Width, list.ClientSize.Width - reserved - 1);
        };
        list.DrawColumnHeader += (_, e) =>
        {
            using var fill = new SolidBrush(EditorTheme.Raised);
            e.Graphics.FillRectangle(fill, e.Bounds);
            DrawListText(e.Graphics, e.Header?.Text ?? "", list.Font, e.Bounds, EditorTheme.Muted);
        };
        list.DrawSubItem += (_, e) =>
        {
            using var fill = new SolidBrush(e.Item?.Selected == true ? EditorTheme.Border : EditorTheme.Surface);
            e.Graphics.FillRectangle(fill, e.Bounds);
            DrawListText(e.Graphics, e.SubItem?.Text ?? "", list.Font, e.Bounds, EditorTheme.Text);
        };
        return list;
    }

    private static void DrawListText(Graphics graphics, string text, Font font, Rectangle bounds, Color color) =>
        TextRenderer.DrawText(graphics, text, font, Rectangle.Inflate(bounds, -8, 0), color,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        var dark = 1;
        DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
