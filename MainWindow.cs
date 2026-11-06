using System.Globalization;

namespace Viewcut;

internal sealed class MainWindow : Form
{
    private readonly SessionStore store;
    private readonly Exporter exporter = new(AppContext.BaseDirectory);
    private readonly CancellationTokenSource shutdown = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 50 };
    private readonly Panel video = new() { Dock = DockStyle.Fill, BackColor = Color.Black };
    private readonly TrackBar timeline = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 10_000, TickStyle = TickStyle.None };
    private readonly ListView files = CreateList(("素材", 235), ("状态", 75));
    private readonly ListView jobs = CreateList(("状态", 65), ("片段", 215));
    private readonly Label time = new() { AutoSize = true };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button pause = Button("暂停");
    private readonly CheckBox exact = new() { Text = "按标记导出", AutoSize = true, Margin = new Padding(8, 8, 0, 0) };
    private readonly Button retry = Button("重试选中任务");
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };

    private Player? player;
    private Task? worker;
    private string? current;
    private string? directory;
    private SourceStamp? sourceStamp;
    private string[] sources = [];
    private int folderRequest;
    private double? start;
    private bool selectingFile;
    private bool opening;
    private bool scrubbing;
    private long lastSeek;
    private bool closing;

    public MainWindow(SessionStore store, string? input)
    {
        this.store = store;
        Text = "Viewcut";
        ClientSize = new Size(1110, 670);
        MinimumSize = new Size(840, 480);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);

        var split = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 2 };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 328));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var playback = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        playback.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        playback.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        playback.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        playback.Controls.Add(video, 0, 0);
        playback.Controls.Add(timeline, 0, 1);

        var playbackControls = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true };
        playbackControls.Controls.AddRange([pause, Button("I  起点", MarkStart), Button("O  保存片段", MarkEnd),
            Button("P  上一条", () => MoveFile(-1)), Button("N  下一条", () => MoveFile(1)), Button("X  跳过", Skip), exact, time]);
        playback.Controls.Add(playbackControls, 0, 2);
        split.Controls.Add(playback, 0, 0);

        var sourceTab = new TabPage("素材");
        sourceTab.Controls.Add(files);
        var exportTab = new TabPage("导出");
        exportTab.Controls.Add(jobs);
        retry.Dock = DockStyle.Bottom;
        exportTab.Controls.Add(retry);
        tabs.TabPages.AddRange([sourceTab, exportTab]);
        split.Controls.Add(tabs, 1, 0);

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        toolbar.Controls.AddRange([Button("打开视频", ChooseFile), Button("打开文件夹", ChooseFolder),
            Button("选择保存目录", ChooseDestination), Button("打开保存目录", OpenDestination)]);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(6) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(split, 0, 1);
        layout.Controls.Add(status, 0, 2);
        Controls.Add(layout);

        files.RetrieveVirtualItem += (_, e) =>
        {
            var path = sources[e.ItemIndex];
            e.Item = new ListViewItem([Path.GetFileName(path), FileState(path)]) { ToolTipText = path };
        };
        files.SelectedIndexChanged += (_, _) =>
        {
            if (!selectingFile && files.SelectedIndices.Count == 1)
                OpenFile(sources[files.SelectedIndices[0]]);
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
        timeline.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            scrubbing = true;
            lastSeek = 0;
            Scrub(e.X, false);
        };
        timeline.MouseMove += (_, e) =>
        {
            if (scrubbing) Scrub(e.X, false);
        };
        timeline.MouseUp += (_, e) =>
        {
            if (!scrubbing) return;
            scrubbing = false;
            Scrub(e.X, true);
        };
        timeline.MouseCaptureChanged += (_, _) => scrubbing = false;
        timeline.Scroll += (_, _) =>
        {
            if (!scrubbing && player is { Ready: true } engine)
                engine.Seek(engine.Duration * timeline.Value / timeline.Maximum, true);
        };
        timer.Tick += (_, _) => UpdatePlayback();
        exact.CheckedChanged += (_, _) => SetStatus(exact.Checked
            ? "按标记导出会重编码片段，支持 8 位 SDR 4:2:0。"
            : "无损快切会从附近的前一个关键帧开始，导出起点可能提前。");
        FormClosing += HandleClosing;
        Shown += (_, _) =>
        {
            timer.Start();
            RefreshJobs();
            SetStatus("打开一条视频或文件夹开始粗筛。");
            if (input is not null) ActivateInput(input);
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
            _ = LoadFolderAsync(path, true);
            return;
        }
        if (!File.Exists(path) || !IsVideo(path))
        {
            SetStatus($"找不到可用的 MP4 或 MOV：{path}");
            return;
        }
        OpenFile(path);
        var parent = Path.GetDirectoryName(path)!;
        if (!string.Equals(directory, parent, StringComparison.OrdinalIgnoreCase))
            _ = LoadFolderAsync(parent, false);
        else SelectCurrent();
    }

    private async Task LoadFolderAsync(string path, bool openFirst)
    {
        var request = ++folderRequest;
        directory = null;
        ReplaceSources([]);
        try
        {
            var results = await Task.Run(() => Directory.EnumerateFiles(path).Where(IsVideo)
                .Order(StringComparer.OrdinalIgnoreCase).ToArray());
            if (closing || request != folderRequest) return;
            directory = path;
            ReplaceSources(results);
            if (openFirst && results.Length > 0)
                OpenFile(results[0]);
            else if (results.Length == 0)
                SetStatus("此文件夹里没有 MP4 或 MOV 视频。");
            SelectCurrent();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!closing && request == folderRequest)
            {
                directory = null;
                SetStatus($"读取文件夹失败：{ex.Message}");
            }
        }
    }

    private static bool IsVideo(string path) => Path.GetExtension(path).ToLowerInvariant() is ".mp4" or ".mov";

    private void OpenFile(string path)
    {
        try
        {
            var stamp = SourceStamp.Read(path);
            player ??= new Player(video.Handle);
            player.Open(path);
            current = path;
            sourceStamp = stamp;
            start = null;
            opening = true;
            Text = $"Viewcut  ·  {Path.GetFileName(path)}";
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
        SelectCurrent();
    }

    private void MarkStart()
    {
        if (!CanMark()) return;
        start = player!.Position;
        SetStatus($"已标记起点 {Clock(start.Value)}。按 O 保存这段。");
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
        var path = current!;
        var stamp = sourceStamp!;
        var precise = exact.Checked;
        if (store.Data.Destination is null) ChooseDestination();
        if (store.Data.Destination is not { } destination || closing) return;

        var job = new ExportJob(path, stamp, destination, from, end, precise);
        store.Data.Jobs.Add(job);
        if (!Save())
        {
            store.Data.Jobs.RemoveAt(store.Data.Jobs.Count - 1);
            return;
        }
        start = null;
        SetStatus($"片段 {Clock(from)}–{Clock(end)} 已排队。");
        RefreshJobs();
        StartQueue();
    }

    private bool CanMark()
    {
        if (player is { Ready: true, Seeking: false, Duration: > 0 } && !scrubbing && sourceStamp is not null)
            return true;
        SetStatus("请等画面加载或定位完成后再标记。");
        return false;
    }

    private void Skip()
    {
        if (current is null || sourceStamp is null) return;
        store.Data.Skipped[current] = sourceStamp;
        if (!Save()) return;
        files.Invalidate();
        MoveFile(1);
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
        if (player is not { } engine) return;
        engine.Poll();
        if (engine.Error is { } error)
        {
            if (opening) SetStatus($"播放失败：{error}");
            opening = false;
            return;
        }
        if (!engine.Ready) return;
        if (opening && engine.Position > 0)
        {
            opening = false;
            SetStatus("按 I 标记起点，按 O 保存片段。");
        }
        pause.Text = engine.Paused ? "播放" : "暂停";
        if (!scrubbing && !engine.Seeking && engine.Duration > 0)
            timeline.Value = (int)Math.Clamp(engine.Position / engine.Duration * timeline.Maximum, 0, timeline.Maximum);
        var marker = start is { } value ? $"  ·  入点 {Clock(value)}" : "";
        time.Text = $"{Clock(engine.Position)} / {Clock(engine.Duration)}{marker}";
    }

    private void Scrub(int x, bool finished)
    {
        if (player is not { Ready: true, Duration: > 0 } engine) return;
        var ratio = Math.Clamp((x - 12.0) / Math.Max(1, timeline.ClientSize.Width - 24), 0, 1);
        timeline.Value = (int)(ratio * timeline.Maximum);
        var now = Environment.TickCount64;
        if (!finished && now - lastSeek < 90) return;
        lastSeek = now;
        engine.Seek(ratio * engine.Duration, finished);
    }

    private void ReplaceSources(string[] result)
    {
        selectingFile = true;
        files.VirtualListSize = 0;
        sources = result;
        files.VirtualListSize = sources.Length;
        selectingFile = false;
    }

    private void SelectCurrent()
    {
        var index = Array.FindIndex(sources, path => string.Equals(path, current, StringComparison.OrdinalIgnoreCase));
        selectingFile = true;
        files.SelectedIndices.Clear();
        if (index >= 0)
        {
            files.SelectedIndices.Add(index);
            files.EnsureVisible(index);
        }
        selectingFile = false;
    }

    private string FileState(string path)
    {
        var related = store.Data.Jobs.Where(job => string.Equals(job.Source, path, StringComparison.OrdinalIgnoreCase));
        if (related.Any(job => job.State is JobState.Waiting or JobState.Running)) return "导出中";
        if (related.Any(job => job.State == JobState.Failed)) return "有失败";
        if (related.Any(job => job.State == JobState.Done)) return "已保存";
        return store.Data.Skipped.ContainsKey(path) ? "已跳过" : "";
    }

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
        files.Invalidate();
        var active = store.Data.Jobs.Count(job => job.State is JobState.Waiting or JobState.Running);
        tabs.TabPages[1].Text = active == 0 ? "导出" : $"导出 ({active})";
        UpdateRetry();
    }

    private void UpdateRetry() => retry.Enabled = jobs.SelectedIndices.Count == 1
        && store.Data.Jobs[jobs.SelectedIndices[0]].State == JobState.Failed;

    private void ChooseFile()
    {
        using var dialog = new OpenFileDialog { Filter = "视频 (*.mp4;*.mov)|*.mp4;*.mov" };
        if (dialog.ShowDialog(this) == DialogResult.OK) ActivateInput(dialog.FileName);
    }

    private void ChooseFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "选择素材文件夹" };
        if (dialog.ShowDialog(this) == DialogResult.OK) ActivateInput(dialog.SelectedPath);
    }

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
        if (Save()) SetStatus("保存目录已设定。");
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
        player?.Dispose();
        shutdown.Dispose();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (closing) return true;
        switch (keyData)
        {
            case Keys.Space when player is { Ready: true }: player.TogglePause(); return true;
            case Keys.I: MarkStart(); return true;
            case Keys.O: MarkEnd(); return true;
            case Keys.P: MoveFile(-1); return true;
            case Keys.N: MoveFile(1); return true;
            case Keys.X: Skip(); return true;
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
        var button = new Button { Text = label, AutoSize = true };
        if (action is not null) button.Click += (_, _) => action();
        return button;
    }

    private static ListView CreateList(params (string Label, int Width)[] columns)
    {
        var list = new ListView { Dock = DockStyle.Fill, VirtualMode = true, View = View.Details,
            FullRowSelect = true, MultiSelect = false, ShowItemToolTips = true, HideSelection = false };
        foreach (var column in columns) list.Columns.Add(column.Label, column.Width);
        return list;
    }
}
