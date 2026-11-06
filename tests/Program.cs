using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Viewcut;

if (args.Length != 2)
    throw new ArgumentException("Expected a tool directory and an empty test directory.");

var tools = Path.GetFullPath(args[0]);
var directory = Path.GetFullPath(args[1]);
Directory.CreateDirectory(directory);
var source = Path.Combine(directory, "相机 素材.mp4");
var second = Path.Combine(directory, "下一条 4K HEVC.mov");
var output = Path.Combine(directory, "片段 输出");
Directory.CreateDirectory(output);
var ffmpeg = Path.Combine(tools, "ffmpeg.exe");
var ffprobe = Path.Combine(tools, "ffprobe.exe");
await Run(ffmpeg, "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=30",
    "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "9", "-c:v", "libx264",
    "-g", "120", "-sc_threshold", "0", "-c:a", "aac", source);
await Run(ffmpeg, "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=3840x2160:rate=30",
    "-t", "4", "-c:v", "libx265", "-preset", "ultrafast", "-x265-params", "pools=4:frame-threads=2:log-level=error", "-an", second);

var stamp = SourceStamp.Read(source);
var exporter = new Exporter(tools);
var fast = new ExportJob(source, stamp, output, 1.8, 4.2);
var firstOutput = await exporter.ExportAsync(fast, CancellationToken.None);
var secondOutput = await exporter.ExportAsync(fast, CancellationToken.None);
Assert(firstOutput != secondOutput, "Repeated names get a suffix");
foreach (var clip in new[] { firstOutput, secondOutput })
    await Run(ffmpeg, "-v", "error", "-xerror", "-i", clip, "-f", "null", "-");
var fastLength = await Duration(firstOutput);
Assert(fastLength > 2.4 && fastLength < 4.5, $"Fast cut keeps keyframe lead-in ({fastLength:F3}s)");
var hevc = new ExportJob(second, SourceStamp.Read(second), output, 0.5, 2.5);
var hevcOutput = await exporter.ExportAsync(hevc, CancellationToken.None);
await Run(ffmpeg, "-v", "error", "-xerror", "-i", hevcOutput, "-f", "null", "-");
Assert(Path.GetExtension(hevcOutput) == ".mov", "Silent HEVC MOV can be cut and decoded");

try
{
    await exporter.ExportAsync(fast with { Stamp = stamp with { Size = 1 } }, CancellationToken.None);
    throw new Exception("Expected source verification failure");
}
catch (IOException) { Console.WriteLine("PASS: Changed source is rejected"); }
Assert(SourceStamp.Read(source) == stamp, "Original file is unchanged");
Assert(!Directory.EnumerateFiles(output, "*.partial*").Any(), "No partial files after successful exports");

var state = Path.Combine(directory, "state", "session.json");
var store = new SessionStore(state);
store.Data.Destination = output;
store.Data.Jobs.Add(fast with { State = JobState.Done, Output = firstOutput });
store.Data.Jobs.Add(fast with { State = JobState.Running });
store.Data.Jobs.Add(hevc);
store.Save();
var restored = new SessionStore(state).Data;
Assert(restored.Destination == output && restored.Jobs[0].Output == firstOutput
    && restored.Jobs[1].State == JobState.Failed && restored.Jobs[2].State == JobState.Waiting,
    "Saved clips, interrupted tasks, and pending tasks restore");
var legacyPath = Path.Combine(directory, "legacy.json");
var legacy = new
{
    Destination = output,
    Jobs = new[]
    {
        new { fast.Source, fast.Stamp, fast.Folder, fast.Start, fast.End, Exact = true, fast.Id, ClipId = (Guid?)null, State = 0 }
    }
};
File.WriteAllText(legacyPath, System.Text.Json.JsonSerializer.Serialize(legacy));
var migrated = new SessionStore(legacyPath);
Assert(migrated.Data.Clips.Count == 1 && migrated.Data.Clips[0].Id == fast.Id
    && migrated.Data.Jobs[0].State == JobState.Failed, "Old markers migrate to drafts without silently exporting");
migrated.Save();
Assert(new SessionStore(legacyPath).Data.Clips.Count == 1, "Migration runs once");

var thread = new Thread(() =>
{
    using var window = new Form { ClientSize = new Size(640, 360), ShowInTaskbar = false, Opacity = 0 };
    using var video = new Panel { Dock = DockStyle.Fill };
    window.Controls.Add(video);
    window.Show();
    using var player = new Player(video.Handle);
    var watch = Stopwatch.StartNew();
    player.Open(source);
    WaitUntil(() => player.Ready && player.Position > 0.1 && player.Duration > 8);
    Console.WriteLine($"PASS: Embedded H.264 playback clock running ({watch.ElapsedMilliseconds}ms, includes 100ms playback)");
    player.TogglePause();
    WaitUntil(() => player.Paused);
    player.Seek(5.2, exact: true);
    WaitUntil(() => !player.Seeking && Math.Abs(player.Position - 5.2) < 0.07);
    Console.WriteLine("PASS: Exact paused seek");
    player.Open(second);
    WaitUntil(() => player.Ready && !player.Paused && player.Position > 0.1 && player.Duration < 5);
    Console.WriteLine("PASS: Reuse embedded player for silent HEVC MOV");
    player.Open(Path.Combine(directory, "missing.mp4"));
    WaitUntil(() => player.Error is not null);
    player.Open(source);
    WaitUntil(() => player.Ready && player.Duration > 8);
    Console.WriteLine("PASS: Player recovers after a failed open");
    player.Dispose();
    window.Close();

    var uiStore = new SessionStore(Path.Combine(directory, "ui", "session.json"));
    uiStore.Data.Destination = output;
    using var app = new MainWindow(uiStore, directory) { ShowInTaskbar = false, Opacity = 0 };
    var controls = Descendants(app).ToArray();
    var browser = controls.OfType<FileBrowser>().Single();
    var list = controls.OfType<ListView>().Single(list => list.AccessibleName == "文件浏览器");
    var markIn = controls.OfType<Button>().Single(button => button.Text == "I  起点");
    var markOut = controls.OfType<Button>().Single(button => button.Text == "O  终点");
    var export = controls.OfType<Button>().Single(button => button.Text == "导出当前素材全部片段");
    var playPause = controls.OfType<Button>().Single(button => button.Text == "暂停");
    var previous = controls.OfType<Button>().Single(button => button.Text == "P  上一条");
    var timeline = controls.OfType<ClipTimeline>().Single();
    var labels = controls.OfType<Label>().ToArray();
    Exception? uiError = null;
    app.Shown += async (_, _) =>
    {
        try
        {
            await WaitForUi(() => browser.CurrentFolder == directory && list.VirtualListSize >= 2);
            Assert(app.Text == "Viewcut", "Opening a folder only browses its contents");
            var sourceIndex = Enumerable.Range(0, list.VirtualListSize).Single(index => list.Items[index].Text == Path.GetFileName(source));
            list.SelectedIndices.Add(sourceIndex);
            await Task.Delay(150);
            Assert(app.Text == "Viewcut", "Selecting a video once does not load it");
            ActivateEntry();
            await WaitForUi(() => app.Text.Contains("相机 素材.mp4") && labels.Any(label => label.Text.Contains("/ 00:00:09")));
            Console.WriteLine("PASS: Double-click file activation loads the selected video");
            Assert(!controls.OfType<Button>().Any(button => button.Text is "打开视频" or "打开文件夹" or "X  跳过")
                && !controls.OfType<CheckBox>().Any(), "Removed toolbar actions and export mode are absent");

            browser.SetView(false);
            Assert(list.View == View.Details, "Browser switches to list view");
            Snapshot("browser-list.png");
            var searchBox = controls.OfType<TextBox>().Single(box => box.AccessibleName == "搜索当前文件夹");
            searchBox.Text = "相机";
            Assert(list.VirtualListSize == 1 && list.Items[0].Text == Path.GetFileName(source), "Browser filters names in the current directory");
            searchBox.Clear();
            browser.SetView(true);
            Assert(list.View == View.LargeIcon, "Browser switches to large icons");
            Snapshot("browser-icons.png");
            var title = app.Text;
            await browser.NavigateAsync(output);
            Assert(browser.CurrentFolder == output && app.Text == title, "Browsing another folder keeps the loaded video");
            await browser.NavigateAsync(directory, source);
            await browser.NavigateAsync(output);
            var back = controls.OfType<Button>().Single(button => button.AccessibleName == "后退");
            back.PerformClick();
            await WaitForUi(() => browser.CurrentFolder == directory);
            var forward = controls.OfType<Button>().Single(button => button.AccessibleName == "前进");
            forward.PerformClick();
            await WaitForUi(() => browser.CurrentFolder == output);
            await browser.NavigateAsync(directory, source);
            Console.WriteLine("PASS: Browser back and forward navigation");

            var divider = controls.OfType<SidebarDivider>().Single();
            var columns = (TableLayoutPanel)divider.Parent!;
            var oldFraction = columns.ColumnStyles[2].Width;
            typeof(Control).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(divider, [new MouseEventArgs(MouseButtons.Left, 1, 2, 25, 0)]);
            typeof(Control).GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(divider, [new MouseEventArgs(MouseButtons.Left, 1, -80, 25, 0)]);
            typeof(Control).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(divider, [new MouseEventArgs(MouseButtons.Left, 1, 2, 25, 0)]);
            Assert(columns.ColumnStyles[2].Width > oldFraction, "Dragging the sidebar divider changes the width");

            playPause.PerformClick();
            await WaitForUi(() => playPause.Text == "播放");
            var timeLabel = labels.Single(label => label.Text.Contains("/ 00:00:09"));
            TimeSpan CurrentTime() => TimeSpan.Parse(timeLabel.Text.Split(" / ")[0], CultureInfo.InvariantCulture);

            await JumpTo(0.5);
            markIn.PerformClick();
            await WaitForUi(() => labels.Any(label => label.Text.Contains("已标记起点")));
            await JumpTo(1.5);
            markOut.PerformClick();
            await WaitForUi(() => uiStore.Data.Clips.Count == 1);
            await JumpTo(3.1);
            markIn.PerformClick();
            await JumpTo(4.0);
            markOut.PerformClick();
            await WaitForUi(() => uiStore.Data.Clips.Count == 2);
            await Task.Delay(200);
            Assert(uiStore.Data.Jobs.Count == 0, "Marking multiple clips does not create export tasks");
            Assert(uiStore.Data.LatestClips(source, stamp).Length == 2, "Two clips appear as separate ranges");

            var beforeHover = CurrentTime();
            Mouse("Move", 2.45, Scale(12), MouseButtons.None);
            await WaitForUi(() => PreviewMatches(2.45));
            Assert(CurrentTime() == beforeHover, "Hover decodes its frame without moving the main playhead");

            var original = uiStore.Data.Clips[0];
            Mouse("Down", original.Start, Scale(40), MouseButtons.Left);
            Mouse("Move", 0.76, Scale(40), MouseButtons.Left);
            await WaitForUi(() => PreviewMatches(0.76));
            Assert(uiStore.Data.Jobs.Count == 0, "Dragging previews the new start without exporting");
            Mouse("Up", 0.76, Scale(40), MouseButtons.Left);
            await WaitForUi(() => uiStore.Data.Clips[0].Start != original.Start);
            var adjusted = uiStore.Data.LatestClips(source, stamp)[0];
            Assert(adjusted.Id == original.Id && Math.Abs(adjusted.Start - 0.76) < 0.05 && adjusted.End == original.End,
                "Dragging the start handle updates only the start of the same clip");

            Mouse("Down", adjusted.End, Scale(40), MouseButtons.Left);
            Mouse("Move", 1.8, Scale(40), MouseButtons.Left);
            await WaitForUi(() => PreviewMatches(1.8));
            Mouse("Up", 1.8, Scale(40), MouseButtons.Left);
            var revised = uiStore.Data.LatestClips(source, stamp)[0];
            Assert(Math.Abs(revised.End - 1.8) < 0.05 && revised.Start == adjusted.Start,
                "Dragging the end handle updates only the end of the same clip");
            LeaveTimeline();
            Assert(app.OwnedForms.OfType<FramePreview>().All(frame => !frame.Visible), "Preview closes when the pointer leaves");
            Assert(uiStore.Data.Jobs.Count == 0, "Adjusting both handles only changes saved draft ranges");
            using (var screenshot = new Bitmap(app.Width, app.Height))
            {
                app.DrawToBitmap(screenshot, new Rectangle(0, 0, app.Width, app.Height));
                screenshot.Save(Path.Combine(directory, "editor.png"));
            }

            var newDirectory = Path.Combine(directory, "批量导出");
            Directory.CreateDirectory(newDirectory);
            uiStore.Data.Destination = newDirectory;
            Assert(Directory.GetFiles(newDirectory).Length == 0, "No outputs exist before clicking batch export");
            export.PerformClick();
            Assert(uiStore.Data.Jobs.Count == 2, "One click queues every clip of the current source");
            export.PerformClick();
            Assert(uiStore.Data.Jobs.Count == 2, "Repeated clicks cannot duplicate pending tasks");
            var batch = uiStore.Data.Jobs.ToArray();
            previous.PerformClick();
            await WaitForUi(() => app.Text.Contains("HEVC") && batch.All(job => job.State is JobState.Done or JobState.Failed));
            Assert(batch.All(job => job.State == JobState.Done && File.Exists(job.Output)), "Batch export completes while switching files");
            Assert(batch[0].Start == revised.Start && batch[0].End == revised.End, "Batch export uses the final edited boundaries");
            var latest = new SessionStore(Path.Combine(directory, "ui", "session.json")).Data.LatestClips(source, stamp);
            Assert(latest.Length == 2 && latest[0] == revised, "Draft markers restore independently of exported snapshots");
            app.ActivateInput(directory);
            await WaitForUi(() => browser.CurrentFolder == directory);
            Assert(app.Text.Contains("HEVC"), "Folder activation does not automatically start a video");
            var restoredUi = new SessionStore(Path.Combine(directory, "ui", "session.json"));
            Assert(restoredUi.Data.SidebarFraction > 0.4 && restoredUi.Data.BrowserLargeIcons, "Browser and sidebar preferences persist");
            var frozenStart = batch[0].Start;
            uiStore.Data.Clips[0] = revised with { Start = revised.Start + 0.1 };
            Assert(batch[0].Start == frozenStart, "Editing a draft cannot mutate an already queued batch");

            void ActivateEntry() => typeof(ListView).GetMethod("OnItemActivate", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(list, [EventArgs.Empty]);

            void Snapshot(string filename)
            {
                using var screenshot = new Bitmap(app.Width, app.Height);
                app.DrawToBitmap(screenshot, new Rectangle(0, 0, app.Width, app.Height));
                screenshot.Save(Path.Combine(directory, filename));
            }

            async Task JumpTo(double seconds)
            {
                Mouse("Down", seconds, Scale(12), MouseButtons.Left);
                Mouse("Up", seconds, Scale(12), MouseButtons.Left);
                LeaveTimeline();
                await WaitForUi(() => Math.Abs(CurrentTime().TotalSeconds - seconds) < 0.08);
            }
        }
        catch (Exception ex) { uiError = ex; }
        finally { app.Close(); }
    };
    Application.Run(app);
    if (uiError is not null) throw new InvalidOperationException("Main window smoke test failed.", uiError);

    int Scale(int size) => (int)Math.Round(size * timeline.DeviceDpi / 96d);

    void Mouse(string action, double seconds, int y, MouseButtons buttons)
    {
        var x = Scale(12) + (int)Math.Round(seconds / 9 * (timeline.Width - Scale(24)));
        typeof(Control).GetMethod("OnMouse" + action, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(timeline, [new MouseEventArgs(buttons, 1, x, y, 0)]);
    }

    void LeaveTimeline() => typeof(Control).GetMethod("OnMouseLeave", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(timeline, [EventArgs.Empty]);

    bool PreviewMatches(double seconds) => app.OwnedForms.OfType<FramePreview>()
        .Any(frame => frame.Visible && frame.FramePosition is { } position && Math.Abs(position - seconds) < 0.08);

    async Task WaitForUi(Func<bool> done)
    {
        var wait = Stopwatch.StartNew();
        while (wait.Elapsed < TimeSpan.FromSeconds(8))
        {
            if (done()) return;
            await Task.Delay(15);
        }
        throw new TimeoutException(string.Join(" | ", labels.Select(label => label.Text)));
    }

    void WaitUntil(Func<bool> done)
    {
        PumpUntil(() =>
        {
            player.Poll();
            return done();
        }, () => $"ready={player.Ready}, duration={player.Duration}, position={player.Position}, error={player.Error}");
    }

    void PumpUntil(Func<bool> done, Func<string>? details = null)
    {
        var wait = Stopwatch.StartNew();
        while (wait.Elapsed < TimeSpan.FromSeconds(8))
        {
            Application.DoEvents();
            if (done()) return;
            Thread.Sleep(15);
        }
        throw new TimeoutException($"UI event timed out. {details?.Invoke()}");
    }
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();

async Task<double> Duration(string clip)
{
    var value = await Run(ffprobe, "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", clip);
    return double.Parse(value, CultureInfo.InvariantCulture);
}

static async Task<string> Run(string executable, params string[] arguments)
{
    var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var value in arguments) start.ArgumentList.Add(value);
    using var process = Process.Start(start)!;
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    if (process.ExitCode != 0) throw new Exception(await stderr);
    return await stdout;
}

static void Assert(bool success, string message)
{
    if (!success) throw new Exception(message);
    Console.WriteLine($"PASS: {message}");
}

static IEnumerable<Control> Descendants(Control parent)
{
    foreach (Control control in parent.Controls)
    {
        yield return control;
        foreach (var child in Descendants(control)) yield return child;
    }
}
