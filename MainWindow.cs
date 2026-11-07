using System.Globalization;

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
    private readonly ListView clips = CreateList((L.T("片段"), 62), (L.T("时间范围"), 240));
    private readonly ListView jobs = CreateList((L.T("状态"), 72), (L.T("导出范围"), 230));
    private readonly Label time = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight };
    private readonly Label mediaName = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Text = L.T("播放器") };
    private readonly Label activeFile = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(10, 5, 10, 5), Margin = new Padding(6, 0, 6, 4),
        BackColor = EditorTheme.Raised, ForeColor = EditorTheme.Accent, Text = L.T("尚未加载视频") };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = EditorTheme.Muted };
    private readonly Button pause = Button(L.T("暂停"));
    private readonly Button exportAll = Button(L.T("导出当前素材全部片段"));
    private readonly Button retry = Button(L.T("重试选中任务"));
    private readonly FluentButton undo = new() { Text = L.T("撤销") };
    private readonly FluentButton redo = new() { Text = L.T("重做") };
    private readonly Dictionary<(string Path, SourceStamp Stamp), ClipHistory> clipHistories = [];
    private long historyAccess;
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
    private readonly HashSet<Guid> selectedClips = [];
    private double? start;
    private double mediaDuration;
    private bool selectingClip;
    private bool opening;
    private bool closing;
    private string lastStatus = "";
    private readonly Button preferences = Button(L.T("偏好设置"));

    public MainWindow(SessionStore store, string? input)
    {
        SuspendLayout();
        this.store = store;
        L.SetLanguage(store.Data.Language);
        EditorTheme.SetMode(store.Data.Theme);
        Text = "Viewcut";
        var executable = Path.Combine(AppContext.BaseDirectory, "Viewcut.exe");
        if (File.Exists(executable)) Icon = Icon.ExtractAssociatedIcon(executable);
        ClientSize = new Size(1360, 800);
        MinimumSize = new Size(860, 490);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font(UiFonts.Text, 9.5f);
        BackColor = EditorTheme.Background;
        ForeColor = EditorTheme.Text;
        time.Font = new Font(UiFonts.Text, 10);
        time.AutoSize = false;
        time.Margin = new Padding(12, 0, 6, 0);
        time.ForeColor = EditorTheme.Accent;
        time.BackColor = EditorTheme.Surface;
        time.Padding = new Padding(8, 0, 8, 0);
        time.Text = "00:00:00.00 / 00:00:00.00";
        mediaName.Padding = new Padding(11, 0, 11, 0);

        var fraction = double.IsFinite(store.Data.SidebarFraction) ? Math.Clamp(store.Data.SidebarFraction, 0.28, 0.62) : 0.4;
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 3, Margin = Padding.Empty,
            AccessibleName = L.T("播放器与文件浏览器布局") };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, (float)(100 * (1 - fraction))));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 7));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, (float)(100 * fraction)));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var playback = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
            BackColor = EditorTheme.Surface, Margin = Padding.Empty };
        playback.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        playback.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        playback.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        video.Margin = Padding.Empty;
        mediaName.AutoSize = true;
        mediaName.Padding = new Padding(12, 9, 12, 9);
        video.Paint += DrawEmptyVideo;
        playback.Controls.Add(mediaName, 0, 0);
        playback.Controls.Add(video, 0, 1);
        video.Name = "videoSurface";
        EnableVideoDrop(playback);
        EnableVideoDrop(video);
        EnableVideoDrop(mediaName);
        split.Controls.Add(playback, 0, 0);

        var exportPage = new Panel { Dock = DockStyle.Fill };
        exportPage.Controls.Add(jobs);
        retry.Dock = DockStyle.Bottom;
        exportPage.Controls.Add(retry);
        var browserViewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        browser.Dock = DockStyle.Top;
        browserViewport.Controls.Add(browser);
        browserViewport.SizeChanged += (_, _) => browser.Height = Math.Max(browserViewport.ClientSize.Height, LogicalToDeviceUnits(176));
        sections = [browserViewport, clips, exportPage];
        sectionButtons = [Button(L.T("文件"), () => ShowSection(0)), Button(L.T("片段"), () => ShowSection(1)),
            Button(L.T("导出"), () => ShowSection(2))];
        var sectionNav = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty, Padding = new Padding(6, 4, 0, 4) };
        sectionNav.Controls.AddRange(sectionButtons);
        var sectionBody = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        sectionBody.Controls.AddRange(sections);
        var sidebar = new BufferedLayout { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            BackColor = EditorTheme.Surface, Margin = Padding.Empty };
        sidebar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidebar.Controls.Add(sectionNav, 0, 0);
        sidebar.Controls.Add(activeFile, 0, 1);
        sidebar.Controls.Add(sectionBody, 0, 2);
        split.Controls.Add(sidebar, 2, 0);
        split.Controls.Add(new SidebarDivider(split, horizontal: false, x =>
        {
            var width = Math.Max(1, split.ClientSize.Width - (int)split.ColumnStyles[1].Width);
            ApplySidebarWidth((width - x) / (double)width);
        }, () =>
        {
            store.Data.SidebarFraction = split.ColumnStyles[2].Width / 100;
            Save();
        }), 1, 0);
        split.SizeChanged += (_, _) => ApplySidebarWidth(store.Data.SidebarFraction);

        void ApplySidebarWidth(double preferred)
        {
            var width = Math.Max(1, split.ClientSize.Width - (int)split.ColumnStyles[1].Width);
            var min = Math.Min(0.62, Math.Max(0.28, LogicalToDeviceUnits(390) / (double)width));
            var max = Math.Max(min, Math.Min(0.62, 1 - LogicalToDeviceUnits(320) / (double)width));
            var value = Math.Clamp(double.IsFinite(preferred) ? preferred : 0.4, min, max);
            var percent = (float)(100 * value);
            if (split.ColumnStyles[2].Width == percent) return;
            split.SuspendLayout();
            split.ColumnStyles[0].Width = 100 - percent;
            split.ColumnStyles[2].Width = percent;
            split.ResumeLayout(true);
            sidebar.Invalidate(true);
        }
        ShowSection(0);

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty, Padding = new Padding(2, 3, 0, 9) };
        var wordmark = new Label { Text = "Viewcut", AutoSize = true, Font = new Font(UiFonts.Display, 16),
            Margin = new Padding(0, 1, 22, 1), ForeColor = EditorTheme.Text, AccessibleName = "Viewcut" };
        preferences.AccessibleName = "Viewcut Preferences";
        preferences.Click += (_, _) =>
        {
            using var dialog = new PreferencesDialog(language => ChangePreferences(language, null), theme => ChangePreferences(null, theme));
            dialog.Opacity = Opacity;
            dialog.ShowDialog(this);
        };
        toolbar.Controls.AddRange([wordmark, Button(L.T("选择保存目录"), ChooseDestination), Button(L.T("打开保存目录"), OpenDestination), preferences]);

        var transport = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false,
            Margin = Padding.Empty, Padding = new Padding(0, 5, 0, 5) };
        exportAll.Click += (_, _) => ExportCurrent();
        Emphasize(exportAll, true);
        transport.Controls.AddRange([pause, Button(L.T("P  上一条"), () => MoveFile(-1)), Button(L.T("N  下一条"), () => MoveFile(1)),
            Button(L.T("I  起点"), MarkStart), Button(L.T("O  终点"), MarkEnd)]);
        var transportBar = new BufferedLayout { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 2, Margin = Padding.Empty };
        transportBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        transportBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute));
        transportBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        transportBar.Controls.Add(transport, 0, 0);
        transportBar.Controls.Add(time, 1, 0);
        void FitClock()
        {
            // Keep playback ticks from relaying out the transport controls during a drag.
            var textWidth = TextRenderer.MeasureText("88:88:88.88 / 88:88:88.88", time.Font).Width;
            transportBar.ColumnStyles[1].Width = textWidth + time.Padding.Horizontal + time.Margin.Horizontal;
        }
        time.FontChanged += (_, _) => FitClock();
        time.DpiChangedAfterParent += (_, _) => FitClock();
        FitClock();

        var timelineHeader = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty, Padding = new Padding(8, 5, 8, 5) };
        timelineHeader.Controls.AddRange([
            new Label { Text = L.T("片段时间轴"), AutoSize = true, Margin = new Padding(12, 9, 15, 0), ForeColor = EditorTheme.Text },
            Button("−", () => timeline.Zoom(1.5)), Button("+", () => timeline.Zoom(0.67)), Button(L.T("全览"), timeline.Fit), undo, redo, exportAll,
            new Label { Text = L.T("悬停预览　·　滚轮缩放　·　Shift + 滚轮平移"), AutoSize = true,
                ForeColor = EditorTheme.Muted, Margin = new Padding(12, 9, 0, 0) }
        ]);
        undo.Click += (_, _) => ApplyHistory(false);
        redo.Click += (_, _) => ApplyHistory(true);
        var timelinePanel = new BufferedLayout { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
            BackColor = EditorTheme.Surface, Margin = Padding.Empty };
        timelinePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        timelinePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        timelinePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        timeline.Margin = Padding.Empty;
        timelinePanel.Controls.Add(timelineHeader, 0, 0);
        timelinePanel.Controls.Add(timeline, 0, 1);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1, Padding = new Padding(10) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 7));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 158));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(split, 0, 1);
        layout.Controls.Add(transportBar, 0, 2);
        var timelineFraction = store.Data.TimelineFraction;
        layout.Controls.Add(new SidebarDivider(layout, horizontal: true, y =>
        {
            var available = split.Height + timelinePanel.Height;
            var bottom = timelinePanel.Bottom;
            timelineFraction = ApplyTimelineHeight(bottom - y - (int)layout.RowStyles[3].Height, available);
        }, () =>
        {
            store.Data.TimelineFraction = timelineFraction;
            Save();
        }), 0, 3);
        layout.Controls.Add(timelinePanel, 0, 4);
        layout.Controls.Add(status, 0, 5);
        Controls.Add(layout);

        void FitTimeline()
        {
            var available = layout.ClientSize.Height - layout.Padding.Vertical
                - toolbar.PreferredSize.Height - LogicalToDeviceUnits(52 + 7 + 28);
            ApplyTimelineHeight((int)(available * (double.IsFinite(timelineFraction) ? timelineFraction : 0.24)), available);
        }
        double ApplyTimelineHeight(int height, int available)
        {
            var headerHeight = timelineHeader.GetPreferredSize(new Size(Math.Max(1, layout.ClientSize.Width - layout.Padding.Horizontal), 0)).Height;
            var minimum = headerHeight + timeline.GetPreferredSize(Size.Empty).Height;
            var maximum = Math.Max(minimum, available - LogicalToDeviceUnits(245));
            var value = Math.Clamp(height, minimum, maximum);
            if (layout.RowStyles[4].Height != value)
            {
                layout.RowStyles[4].Height = value;
                transportBar.Invalidate(true);
                timelinePanel.Invalidate(true);
                sidebar.Invalidate(true);
            }
            return value / (double)Math.Max(1, available);
        }
        layout.SizeChanged += (_, _) => FitTimeline();
        timelineHeader.FontChanged += (_, _) => FitTimeline();

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
            e.Item = new ListViewItem([JobLabel(job.State), $"{job.Name ?? Path.GetFileName(job.Source)}  {Clock(job.Start)}–{Clock(job.End)}"])
            {
                ToolTipText = job.Error is { } error ? L.Translate(error) : job.Output ?? job.Folder
            };
        };
        jobs.SelectedIndexChanged += (_, _) => UpdateRetry();
        retry.Click += (_, _) => RetrySelected();
        pause.Click += (_, _) => player?.TogglePause();
        clips.RetrieveVirtualItem += (_, e) =>
        {
            var clip = ranges[e.ItemIndex];
            e.Item = new ListViewItem([clip.DisplayName, $"{Clock(clip.Start)}  →  {Clock(clip.End)}"])
                { ToolTipText = $"{clip.DisplayName}  ·  {ClipState(clipDrafts[e.ItemIndex])}  ·  {Clock(clip.End - clip.Start)}" };
        };
        clips.SelectedIndexChanged += (_, _) =>
        {
            if (selectingClip) return;
            selectedClips.Clear();
            foreach (int index in clips.SelectedIndices) selectedClips.Add(ranges[index].Id);
            selectedClip = ranges.FirstOrDefault(range => selectedClips.Contains(range.Id))?.Id;
            timeline.SetSelection(selectedClips, selectedClip);
            if (selectedClip is { } id) timeline.Reveal(ranges.First(range => range.Id == id));
        };
        clips.DoubleClick += (_, _) =>
        {
            if (clips.SelectedIndices.Count == 1) player?.Seek(ranges[clips.SelectedIndices[0]].Start, true);
        };
        clips.MultiSelect = true;
        var clipContext = EditorTheme.ContextMenu();
        clipContext.Items.Add(L.T("重命名"), null, (_, _) =>
        {
            if (selectedClips.Count == 1) RenameClip(selectedClips.Single());
        });
        clipContext.Items.Add(L.T("删除"), null, (_, _) => DeleteSelectedClips());
        clipContext.Opening += (_, e) =>
        {
            e.Cancel = selectedClips.Count == 0;
            clipContext.Items[0].Enabled = selectedClips.Count == 1;
            clipContext.Items[1].Text = selectedClips.Count > 1 ? L.T("删除选中片段") : L.T("删除");
        };
        clips.ContextMenuStrip = clipContext;
        clips.Disposed += (_, _) => clipContext.Dispose();
        timeline.ClipSelected += (id, toggle) =>
        {
            SelectClip(id, toggle: toggle);
            ShowSection(1);
        };
        timeline.SelectionCleared += () => SelectClip(null);
        timeline.ClipAdjusted += AdjustClip;
        timeline.RenameRequested += RenameClip;
        timeline.DeleteRequested += DeleteSelectedClips;
        timeline.SeekRequested += seconds => player?.Seek(seconds, true);
        timeline.PreviewRequested += PreviewAt;
        Deactivate += (_, _) => timeline.CancelGesture();
        timer.Tick += (_, _) => UpdatePlayback();
        FormClosing += HandleClosing;
        Shown += (_, _) =>
        {
            var workArea = Screen.FromControl(this).WorkingArea;
            if (Width > workArea.Width || Height > workArea.Height)
                Size = new Size(Math.Min(Width, workArea.Width), Math.Min(Height, workArea.Height));
            FitTimeline();
            timer.Start();
            RefreshJobs();
            SetStatus(L.T("在右侧浏览文件夹，双击视频开始粗筛。"));
            if (input is not null) ActivateInput(input);
            else _ = browser.NavigateAsync(store.Data.BrowserFolder
                ?? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
            StartQueue();
        };
        ResumeLayout(performLayout: true);
        L.Changed += ApplyPreferences;
        EditorTheme.Changed += ApplyPreferences;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += SystemPreferencesChanged;
        Disposed += (_, _) =>
        {
            L.Changed -= ApplyPreferences;
            EditorTheme.Changed -= ApplyPreferences;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= SystemPreferencesChanged;
        };
        ApplyPreferences();
    }

    private bool ChangePreferences(string? language, string? theme)
    {
        var before = (store.Data.Language, store.Data.Theme);
        if (language is not null) store.Data.Language = language;
        if (theme is not null) store.Data.Theme = theme;
        if (!Save())
        {
            (store.Data.Language, store.Data.Theme) = before;
            return false;
        }
        L.SetLanguage(store.Data.Language);
        EditorTheme.SetMode(store.Data.Theme);
        return true;
    }

    private void ApplyPreferences()
    {
        if (IsDisposed) return;
        SuspendLayout();
        L.ApplyTree(this);
        EditorTheme.ApplyTree(this);
        browser.ApplyPreferences();
        preview?.ApplyPreferences();
        mediaName.Text = current is null ? L.T("播放器") : Path.GetFileName(current);
        activeFile.Text = current is null ? L.T("尚未加载视频") : L.T("正在处理\n") + Path.GetFileName(current);
        pause.Text = player?.Paused == true ? L.T("播放") : L.T("暂停");
        RefreshJobs();
        if (lastStatus.Length > 0) SetStatus(L.Translate(lastStatus));
        ResumeLayout(true);
        Invalidate(true);
    }

    private void SystemPreferencesChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (EditorTheme.Mode != "system" || !IsHandleCreated || IsDisposed) return;
        try { BeginInvoke(() => { if (!IsDisposed) EditorTheme.SetMode("system"); }); }
        catch (InvalidOperationException) { }
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
            SetStatus(L.F("找不到可用的 MP4 或 MOV：{0}", path));
            return;
        }
        OpenFile(path);
        var parent = Path.GetDirectoryName(path)!;
        if (!string.Equals(browser.CurrentFolder, parent, StringComparison.OrdinalIgnoreCase))
            _ = browser.NavigateAsync(parent, path);
        else browser.SelectPath(path);
    }

    private void EnableVideoDrop(Control surface)
    {
        surface.AllowDrop = true;
        static string? Input(IDataObject? data) => data?.GetData(DataFormats.FileDrop) is string[] paths
            ? paths.FirstOrDefault(path => File.Exists(path) && IsVideo(path)) : null;
        void Over(object? sender, DragEventArgs e) => e.Effect = !closing && Input(e.Data) is not null
            && (e.AllowedEffect & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;
        surface.DragEnter += Over;
        surface.DragOver += Over;
        surface.DragDrop += (_, e) => { if (Input(e.Data) is { } path) ActivateInput(path); };
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
            activeFile.Text = L.T("正在处理\n") + Path.GetFileName(path);
            activeFile.AccessibleDescription = path;
            browser.SetPlayingFile(path);
            sourceStamp = stamp;
            start = null;
            selectedClip = null;
            selectedClips.Clear();
            mediaDuration = 0;
            timeline.SetMedia(0);
            RefreshClips();
            opening = true;
            Text = $"Viewcut  ·  {Path.GetFileName(path)}";
            mediaName.Text = Path.GetFileName(path);
            SetStatus(L.F("正在打开 {0}", Path.GetFileName(path)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DllNotFoundException
            or BadImageFormatException or InvalidOperationException)
        {
            SetStatus(ex is DllNotFoundException ? L.T("缺少 libmpv-2.dll，请用带依赖的测试版本。") : ex.Message);
        }
    }

    private void MoveFile(int offset)
    {
        var index = Array.FindIndex(sources, path => string.Equals(path, current, StringComparison.OrdinalIgnoreCase));
        index += offset;
        if (index < 0 || index >= sources.Length)
        {
            SetStatus(L.T("已到当前列表的边界。"));
            return;
        }
        OpenFile(sources[index]);
        browser.SelectPath(current);
    }

    private void MarkStart()
    {
        if (!CanMark()) return;
        start = player!.Position;
        SetStatus(L.F("已标记起点 {0}，按 O 标记终点。", Clock(start.Value)));
    }

    private void MarkEnd()
    {
        if (!CanMark()) return;
        var end = player!.Position;
        if (start is not { } from || end - from < 0.05)
        {
            SetStatus(L.T("先按 I 标记起点，再在更晚的画面按 O。"));
            return;
        }
        var clip = new ClipDraft(current!, sourceStamp!, from, end);
        var before = CaptureEditState();
        store.Data.Clips.Add(clip);
        if (!Save())
        {
            store.Data.Clips.RemoveAt(store.Data.Clips.Count - 1);
            return;
        }
        start = null;
        selectedClip = clip.Id;
        selectedClips.Clear();
        selectedClips.Add(clip.Id);
        RecordEdit("新增片段", before);
        SetStatus(L.F("已标记片段 {0}–{1}，选完后点击导出。", Clock(from), Clock(end)));
        RefreshClips();
    }

    private bool CanMark()
    {
        if (player is { Ready: true, Seeking: false, Duration: > 0 } && sourceStamp is not null)
            return true;
        SetStatus(L.T("请等画面加载或定位完成后再标记。"));
        return false;
    }

    private void ExportCurrent()
    {
        if (current is not { } path || sourceStamp is not { } stamp || clipDrafts.Length == 0) return;
        if (store.Data.Destination is null) ChooseDestination();
        if (store.Data.Destination is not { } folder || closing) return;
        foreach (var clip in clipDrafts.Where(clip => store.Data.CanQueue(clip, folder)))
        {
            if (CheckClipName(clip, clip.Name, folder) is not { } conflict) continue;
            MessageBox.Show(this, $"{clip.Name}\n{conflict}", L.T("无法导出片段"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var batch = store.Data.QueueExport(path, stamp, folder);
        if (!Save())
        {
            foreach (var job in batch) store.Data.Jobs.Remove(job);
            return;
        }
        SetStatus(batch.Length > 0 ? L.F("已提交当前素材的 {0} 个片段。无损快切的起点可能提前至关键帧。", batch.Length) : L.T("这些片段已在队列中。"));
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
                SetStatus(L.F("已保存 {0}", Path.GetFileName(job.Output)));
            }
            catch (OperationCanceledException) when (closing)
            {
                job.State = JobState.Waiting;
            }
            catch (Exception ex)
            {
                job.State = JobState.Failed;
                job.Error = ex.Message;
                SetStatus(L.F("导出失败：{0}", ex.Message));
                if (ex is ClipNameConflictException && !closing)
                    MessageBox.Show(this, ex.Message, L.T("导出文件重名"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        if (engine.TakeDroppedFile() is { } dropped) { ActivateInput(dropped); return; }
        if (engine.Error is { } error)
        {
            if (opening) SetStatus(L.F("播放失败：{0}", error));
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
            SetStatus(L.T("按 I / O 标记片段，选完后点击导出当前素材全部片段。"));
        }
        pause.Text = engine.Paused ? L.T("播放") : L.T("暂停");
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
        var before = CaptureEditState();
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
        RecordEdit("调整片段范围", before);
        SetStatus(L.F("已调整片段 {0}，点击导出应用当前范围。", edited.Number));
        RefreshClips();
    }

    private void RenameClip(Guid id)
    {
        var clip = clipDrafts.FirstOrDefault(item => item.Id == id);
        if (clip is null) return;
        preview?.Hide();
        using var dialog = new RenameClipDialog(clip.Name, name => string.Equals(name, clip.Name, StringComparison.Ordinal)
            ? null : CheckClipName(clip, name, store.Data.Destination));
        dialog.Opacity = Opacity;
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.ClipName == clip.Name) return;
        var index = store.Data.Clips.FindIndex(item => item.Id == id);
        if (index < 0) return;
        var before = CaptureEditState();
        store.Data.Clips[index] = clip with { Name = dialog.ClipName };
        if (!Save())
        {
            store.Data.Clips[index] = clip;
            return;
        }
        RecordEdit("重命名片段", before);
        RefreshClips();
        SetStatus(dialog.ClipName is { } name ? L.F("片段已命名为 {0}。", name) : L.T("已恢复默认片段名称。"));
    }

    private string? CheckClipName(ClipDraft clip, string? name, string? folder)
    {
        var extension = Path.GetExtension(clip.Source);
        var error = ClipNames.Validate(name, clip.Id, clipDrafts, folder, extension);
        if (error is not null || name is null || folder is null) return error;
        return store.Data.Jobs.Any(job => job.State is JobState.Waiting or JobState.Running
            && string.Equals(job.Name, name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetExtension(job.Source), extension, StringComparison.OrdinalIgnoreCase)
            && string.Equals(job.Folder, folder, StringComparison.OrdinalIgnoreCase))
            ? L.T("已有同名片段正在排队导出，请换一个名称。") : null;
    }

    private void RefreshClips()
    {
        selectingClip = true;
        clips.VirtualListSize = 0;
        clipDrafts = store.Data.LatestClips(current, sourceStamp);
        ranges = clipDrafts.Select((clip, index) => new ClipRange(clip.Id, index + 1, clip.Start, clip.End, clip.Name)).ToArray();
        clips.VirtualListSize = ranges.Length;
        selectingClip = false;
        sectionButtons[1].Text = ranges.Length == 0 ? L.T("片段") : L.F("片段  {0}", ranges.Length);
        selectedClips.IntersectWith(ranges.Select(range => range.Id));
        if (selectedClip is { } id && !selectedClips.Contains(id))
            selectedClip = selectedClips.Count > 0 ? selectedClips.First() : null;
        SyncClipSelection();
        exportAll.Enabled = clipDrafts.Any(clip => store.Data.CanQueue(clip, store.Data.Destination));
        exportAll.Text = ranges.Length == 0 ? L.T("导出当前素材全部片段") : L.F("导出当前素材全部片段 ({0})", ranges.Length);
        UpdateHistory();
    }

    private void SelectClip(Guid? id, bool reveal = false, bool toggle = false)
    {
        if (!toggle) selectedClips.Clear();
        if (id is { } value && (!toggle || !selectedClips.Remove(value))) selectedClips.Add(value);
        selectedClip = id is { } currentId && selectedClips.Contains(currentId) ? currentId
            : selectedClips.Count > 0 ? selectedClips.First() : null;
        SyncClipSelection();
        if (reveal && selectedClip is { } primary) timeline.Reveal(ranges.First(range => range.Id == primary));
    }

    private void SyncClipSelection()
    {
        timeline.SetClips(ranges, selectedClip);
        timeline.SetSelection(selectedClips, selectedClip);
        selectingClip = true;
        clips.SelectedIndices.Clear();
        for (var index = 0; index < ranges.Length; index++)
            if (selectedClips.Contains(ranges[index].Id)) clips.SelectedIndices.Add(index);
        selectingClip = false;
        var primaryIndex = Array.FindIndex(ranges, range => range.Id == selectedClip);
        if (primaryIndex >= 0) clips.EnsureVisible(primaryIndex);
    }

    private void DeleteSelectedClips()
    {
        if (closing || selectedClips.Count == 0) return;
        var ids = clipDrafts.Where(clip => selectedClips.Contains(clip.Id)).Select(clip => clip.Id).ToHashSet();
        if (ids.Count == 0) return;
        var before = store.Data.Clips.ToArray();
        var beforeEdit = CaptureEditState();
        store.Data.Clips.RemoveAll(clip => ids.Contains(clip.Id));
        if (!Save())
        {
            store.Data.Clips.Clear();
            store.Data.Clips.AddRange(before);
            return;
        }
        timeline.CancelGesture();
        preview?.Hide();
        selectedClip = null;
        selectedClips.Clear();
        RecordEdit("删除片段", beforeEdit);
        RefreshClips();
        SetStatus(L.F("已删除 {0} 个片段标记。", ids.Count));
    }

    private ClipEditState CaptureEditState() => new(store.Data.LatestClips(current, sourceStamp), selectedClips.ToArray(), selectedClip);

    private ClipHistory? History(bool create = false)
    {
        if (current is null || sourceStamp is null) return null;
        var key = (current.ToUpperInvariant(), sourceStamp);
        if (clipHistories.TryGetValue(key, out var history))
        {
            history.Access = ++historyAccess;
            return history;
        }
        if (!create) return null;
        if (clipHistories.Count >= 64) clipHistories.Remove(clipHistories.MinBy(pair => pair.Value.Access).Key);
        history = new ClipHistory { Access = ++historyAccess };
        clipHistories.Add(key, history);
        return history;
    }

    private void RecordEdit(string name, ClipEditState before) => History(true)?.Record(name, before, CaptureEditState());

    private void UpdateHistory()
    {
        var history = History();
        history?.Validate(clipDrafts);
        undo.Enabled = history?.Undo is not null;
        redo.Enabled = history?.Redo is not null;
        undo.Hint = history?.Undo is { } previous ? L.F("撤销：{0}", L.T(previous.Name)) + "  (Ctrl+Z)" : L.T("撤销") + "  (Ctrl+Z)";
        redo.Hint = history?.Redo is { } next ? L.F("重做：{0}", L.T(next.Name)) + "  (Ctrl+Y / Ctrl+Shift+Z)" : L.T("重做") + "  (Ctrl+Y / Ctrl+Shift+Z)";
    }

    private void ApplyHistory(bool forward)
    {
        if (closing || current is null || sourceStamp is null) return;
        var history = History();
        history?.Validate(store.Data.LatestClips(current, sourceStamp));
        var edit = forward ? history?.Redo : history?.Undo;
        if (edit is null) { UpdateHistory(); return; }
        try
        {
            if (SourceStamp.Read(current) != sourceStamp)
            {
                SetStatus(L.T("源视频已更改，无法应用撤销或重做。请重新加载视频。"));
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { SetStatus(L.F("无法撤销或重做：{0}", ex.Message)); return; }
        var state = forward ? edit.After : edit.Before;
        var before = store.Data.Clips.ToArray();
        var position = store.Data.Clips.FindIndex(clip => clip.Stamp == sourceStamp
            && string.Equals(clip.Source, current, StringComparison.OrdinalIgnoreCase));
        store.Data.Clips.RemoveAll(clip => clip.Stamp == sourceStamp
            && string.Equals(clip.Source, current, StringComparison.OrdinalIgnoreCase));
        store.Data.Clips.InsertRange(position < 0 ? store.Data.Clips.Count : position, state.Clips);
        if (!Save())
        {
            store.Data.Clips.Clear();
            store.Data.Clips.AddRange(before);
            return;
        }
        history!.Applied(forward);
        timeline.CancelGesture();
        preview?.Hide();
        selectedClips.Clear();
        selectedClips.UnionWith(state.Selection);
        selectedClip = state.Primary;
        RefreshClips();
        if (ranges.FirstOrDefault(range => range.Id == selectedClip) is { } selected) timeline.Reveal(selected);
        timeline.Focus();
        SetStatus(L.F(forward ? "已重做：{0}。" : "已撤销：{0}。", L.T(edit.Name)));
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
        ? JobLabel(job.State) : L.T("待导出");

    private static string JobLabel(JobState state) => state switch
    {
        JobState.Waiting => L.T("排队中"),
        JobState.Running => L.T("导出中"),
        JobState.Done => L.T("已保存"),
        _ => L.T("失败")
    };

    private void RefreshJobs()
    {
        jobs.VirtualListSize = store.Data.Jobs.Count;
        jobs.Invalidate();
        var active = store.Data.Jobs.Count(job => job.State is JobState.Waiting or JobState.Running);
        sectionButtons[2].Text = active == 0 ? L.T("导出") : L.F("导出  {0}", active);
        UpdateRetry();
        RefreshClips();
    }

    private void UpdateRetry() => retry.Enabled = jobs.SelectedIndices.Count == 1
        && store.Data.Jobs[jobs.SelectedIndices[0]].State == JobState.Failed;

    private void ChooseDestination()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = L.T("选择片段保存目录"),
            SelectedPath = store.Data.Destination ?? ""
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var before = store.Data.Destination;
        store.Data.Destination = dialog.SelectedPath;
        if (Save())
        {
            SetStatus(L.T("保存目录已设定。"));
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
            SetStatus(L.F("无法保存粗筛记录：{0}", ex.Message));
            return false;
        }
    }

    private async void HandleClosing(object? sender, FormClosingEventArgs e)
    {
        if (browser.FileOperationInProgress)
        {
            e.Cancel = true;
            if (closing) return;
            closing = true;
            browser.Enabled = false;
            SetStatus(L.T("正在等待文件操作结束；可在 Windows 文件操作窗口中取消。"));
            while (!IsDisposed && browser.FileOperationInProgress) await Task.Delay(80);
            if (!IsDisposed) { closing = false; Close(); }
            return;
        }
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
        if (!browser.EditingText && (keyData == (Keys.Control | Keys.Z) || keyData == (Keys.Control | Keys.Y)
            || keyData == (Keys.Control | Keys.Shift | Keys.Z)))
        {
            ApplyHistory(keyData != (Keys.Control | Keys.Z));
            return true;
        }
        if (browser.ContainsFocus) return base.ProcessCmdKey(ref msg, keyData);
        switch (keyData)
        {
            case Keys.Delete or Keys.Back when timeline.ContainsFocus || clips.ContainsFocus:
                DeleteSelectedClips();
                return true;
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
        lastStatus = message;
        status.Text = store.Data.Destination is { } folder ? L.F("{0}    ·    保存到 {1}", message, folder) : message;
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
        using var title = new Font(UiFonts.Text, LogicalToDeviceUnits(22), GraphicsUnit.Pixel);
        const TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
        TextRenderer.DrawText(e.Graphics, L.T("打开素材开始粗筛"), title, center, EditorTheme.VideoText, flags);
        center.Offset(0, 32);
        TextRenderer.DrawText(e.Graphics, L.T("在右侧文件浏览器中双击视频，然后用 I / O 标记片段。"), Font, center, EditorTheme.VideoMuted, flags);
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
            var reserved = list.Columns.Cast<ColumnHeader>().Take(columns.Length - 1).Sum(column => column.Width);
            list.Columns[^1].Width = Math.Max((int)(columns[^1].Width * list.DeviceDpi / 96d), list.ClientSize.Width - reserved);
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
        EditorTheme.ApplyWindow(this);
    }

}
