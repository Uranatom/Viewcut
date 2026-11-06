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
var fast = new ExportJob(source, stamp, output, 1.8, 4.2, false);
var exact = new ExportJob(source, stamp, output, 1.8, 4.2, true);
var firstOutput = await exporter.ExportAsync(fast, CancellationToken.None);
var secondOutput = await exporter.ExportAsync(exact, CancellationToken.None);
Assert(firstOutput != secondOutput, "Repeated names get a suffix");
foreach (var clip in new[] { firstOutput, secondOutput })
    await Run(ffmpeg, "-v", "error", "-xerror", "-i", clip, "-f", "null", "-");
var fastLength = await Duration(firstOutput);
var exactLength = await Duration(secondOutput);
Assert(fastLength > 2.4 && fastLength < 4.5, $"Fast cut keeps keyframe lead-in ({fastLength:F3}s)");
Assert(Math.Abs(exactLength - 2.4) < 0.07, $"Exact cut duration ({exactLength:F3}s)");
var hevc = new ExportJob(second, SourceStamp.Read(second), output, 0.5, 2.5, false);
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
store.Data.Jobs.Add(exact with { State = JobState.Running });
store.Data.Jobs.Add(hevc);
store.Save();
var restored = new SessionStore(state).Data;
Assert(restored.Destination == output && restored.Jobs[0].Output == firstOutput
    && restored.Jobs[1].State == JobState.Failed && restored.Jobs[2].State == JobState.Waiting,
    "Saved clips, interrupted tasks, and pending tasks restore");

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
    using var app = new MainWindow(uiStore, source) { ShowInTaskbar = false, Opacity = 0 };
    var controls = Descendants(app).ToArray();
    var list = controls.OfType<ListView>().Single(list => list.Columns[0].Text == "素材");
    var markIn = controls.OfType<Button>().Single(button => button.Text == "I  起点");
    var markOut = controls.OfType<Button>().Single(button => button.Text == "O  保存片段");
    var playPause = controls.OfType<Button>().Single(button => button.Text == "暂停");
    var previous = controls.OfType<Button>().Single(button => button.Text == "P  上一条");
    var timeline = controls.OfType<ClipTimeline>().Single();
    var labels = controls.OfType<Label>().ToArray();
    Exception? uiError = null;
    app.Shown += async (_, _) =>
    {
        try
        {
            await WaitForUi(() => list.VirtualListSize == 2 && labels.Any(label => label.Text.Contains("/ 00:00:09")));
            Assert(list.SelectedIndices.Count == 1, "File activation selects the source in the folder list");
            playPause.PerformClick();
            await WaitForUi(() => playPause.Text == "播放");
            var timeLabel = labels.Single(label => label.Text.Contains("/ 00:00:09"));
            TimeSpan CurrentTime() => TimeSpan.Parse(timeLabel.Text.Split(" / ")[0], CultureInfo.InvariantCulture);

            await JumpTo(0.5);
            markIn.PerformClick();
            await WaitForUi(() => labels.Any(label => label.Text.Contains("已标记起点")));
            await JumpTo(1.5);
            markOut.PerformClick();
            await WaitForUi(() => uiStore.Data.Jobs.Count == 1);
            await JumpTo(3.1);
            markIn.PerformClick();
            await JumpTo(4.0);
            markOut.PerformClick();
            await WaitForUi(() => uiStore.Data.Jobs.Count == 2 && uiStore.Data.Jobs.All(job => job.State == JobState.Done));
            Assert(uiStore.Data.LatestClips(source, stamp).Length == 2, "Two clips appear as separate ranges");

            var beforeHover = CurrentTime();
            Mouse("Move", 2.45, Scale(12), MouseButtons.None);
            await WaitForUi(() => PreviewMatches(2.45));
            Assert(CurrentTime() == beforeHover, "Hover decodes its frame without moving the main playhead");

            var original = uiStore.Data.Jobs[0];
            Mouse("Down", original.Start, Scale(40), MouseButtons.Left);
            Mouse("Move", 0.76, Scale(40), MouseButtons.Left);
            await WaitForUi(() => PreviewMatches(0.76));
            Assert(uiStore.Data.Jobs.Count == 2, "Dragging previews the new start before saving");
            Mouse("Up", 0.76, Scale(40), MouseButtons.Left);
            await WaitForUi(() => uiStore.Data.Jobs.Count == 3);
            var adjusted = uiStore.Data.LatestClips(source, stamp)[0];
            Assert(adjusted.ClipId == original.Id && Math.Abs(adjusted.Start - 0.76) < 0.05 && adjusted.End == original.End,
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
            Assert(File.Exists(original.Output), "Resizing keeps the earlier exported file");
            using (var screenshot = new Bitmap(app.Width, app.Height))
            {
                app.DrawToBitmap(screenshot, new Rectangle(0, 0, app.Width, app.Height));
                screenshot.Save(Path.Combine(directory, "editor.png"));
            }

            previous.PerformClick();
            await WaitForUi(() => app.Text.Contains("HEVC") && revised.State is JobState.Done or JobState.Failed);
            Assert(revised.State == JobState.Done && File.Exists(revised.Output),
                $"Main window exports an edited clip while switching files ({revised.Error})");
            var latest = new SessionStore(Path.Combine(directory, "ui", "session.json")).Data.LatestClips(source, stamp);
            Assert(latest.Length == 2 && latest[0].Id == revised.Id, "The timeline restores only the latest revision of each clip");
            app.ActivateInput(directory);
            await WaitForUi(() => list.VirtualListSize == 2 && app.Text.Contains("HEVC"));
            Console.WriteLine("PASS: Folder activation loads its supported videos");

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
