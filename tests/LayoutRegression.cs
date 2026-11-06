using System.Reflection;
using System.Runtime.InteropServices;
using Viewcut;

internal static class LayoutRegression
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var mediaFolder = Path.Combine(output, "素材目录");
        Directory.CreateDirectory(Path.Combine(mediaFolder, "精选片段"));
        File.WriteAllText(Path.Combine(mediaFolder, "相机素材.mp4"), "");
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var store = new SessionStore(Path.Combine(output, "session.json"));
            store.Data.Language = Environment.GetEnvironmentVariable("VIEWCUT_TEST_LANGUAGE") ?? "zh-CN";
            store.Data.Theme = Environment.GetEnvironmentVariable("VIEWCUT_TEST_THEME") ?? "dark";
            using var app = new MainWindow(store, mediaFolder)
            {
                ShowInTaskbar = false,
                Opacity = 0
            };
            app.Shown += async (_, _) =>
            {
                try
                {
                    await Task.Delay(350);
                    Check(app, output);
                    foreach (var dpi in new[] { 96, 120, 144, 168, 192 })
                    {
                        var children = Descendants(app).ToArray();
                        // WinForms accepts the target DPI in BEFOREPARENT for regression tests.
                        // This exercises its real font and control rescaling without changing the display.
                        foreach (var control in children.Reverse())
                            SendDpiMessage(control.Handle, 0x02E2, (IntPtr)dpi, IntPtr.Zero);
                        var rect = new NativeRectangle
                        {
                            Right = 1240 * dpi / 96,
                            Bottom = 680 * dpi / 96
                        };
                        SendMessage(app.Handle, 0x02E0, (IntPtr)(dpi | (dpi << 16)), ref rect);
                        foreach (var control in children)
                            SendDpiMessage(control.Handle, 0x02E3, (IntPtr)dpi, IntPtr.Zero);
                        app.ClientSize = new Size(1240 * dpi / 96, 680 * dpi / 96);
                        await Task.Delay(80);
                        Console.WriteLine($"Requested DPI {dpi} -> window {app.DeviceDpi}, timeline {Descendants(app).OfType<ClipTimeline>().Single().DeviceDpi}, font {app.Font.Size}, height {app.Font.Height}");
                        var target = Path.Combine(output, dpi.ToString());
                        Directory.CreateDirectory(target);
                        Check(app, target);
                        app.ClientSize = new Size(900 * dpi / 96, 530 * dpi / 96);
                        Check(app, null);
                    }
                }
                catch (Exception ex) { failure = ex; }
                finally { app.Close(); }
            };
            Application.Run(app);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("DPI layout regression failed.", failure);
    }

    private static void Check(MainWindow app, string? output)
    {
        var all = Descendants(app).ToArray();
        var timeline = all.OfType<ClipTimeline>().Single();
        timeline.SetMedia(200);
        timeline.SetClips(Enumerable.Range(0, 5).Select(index =>
            new ClipRange(Guid.NewGuid(), index + 1, 15 + index * 35, 35 + index * 35)).ToArray(), null);
        var browser = all.OfType<FileBrowser>().Single();
        browser.SetView(false);
        app.PerformLayout();
        foreach (var control in all) control.PerformLayout();
        Console.WriteLine($"Actual window DPI {app.DeviceDpi}. Timeline {timeline.Size}.");
        if (output is not null)
        {
            using var screenshot = new Bitmap(app.Width, app.Height);
            app.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
            screenshot.Save(Path.Combine(output, "layout.png"));
            CheckPreferences(app, output);
        }
        var track = (Rectangle)typeof(ClipTimeline).GetProperty("Track", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(timeline)!;
        Require(track.Height >= timeline.Font.Height + app.DeviceDpi / 96d * 12, "Timeline clip text and handles fit vertically");
        foreach (var label in all.OfType<Label>().Where(control => control.AutoSize && control.Visible))
            Require(label.Bottom + label.Margin.Bottom <= label.Parent!.ClientSize.Height,
                $"Auto-sized label fits its row: {label.Text}");
        var time = all.OfType<Label>().Single(label => label.Text.Contains(" / "));
        Require(time.Width >= TextRenderer.MeasureText(time.Text, time.Font).Width + time.Padding.Horizontal,
            "Full current position and duration fit");
        var entries = all.OfType<ListView>().Single(control => control.AccessibleName == L.T("文件浏览器"));
        Require(entries.Columns.Cast<ColumnHeader>().Sum(column => column.Width) == entries.ClientSize.Width,
            "File header columns fill the available width");
        var clipped = all.Where(control => control.Visible && control.Parent is TableLayoutPanel or FlowLayoutPanel)
            .FirstOrDefault(control => control.Right > control.Parent!.ClientSize.Width + 1 || control.Bottom > control.Parent.ClientSize.Height + 1);
        Require(clipped is null, $"Controls stay inside their layouts{(clipped is null ? "" : $" ({clipped.GetType().Name} {clipped.Text} {clipped.Bounds} in {clipped.Parent!.ClientSize})")}");
    }

    private static void CheckPreferences(MainWindow app, string output)
    {
        using var dialog = new PreferencesDialog(_ => false, _ => false) { Opacity = 0 };
        dialog.Show(app);
        var dpi = app.DeviceDpi;
        var children = Descendants(dialog).ToArray();
        foreach (var control in children.Reverse()) SendDpiMessage(control.Handle, 0x02E2, (IntPtr)dpi, IntPtr.Zero);
        var rectangle = new NativeRectangle { Right = 480 * dpi / 96, Bottom = 320 * dpi / 96 };
        SendMessage(dialog.Handle, 0x02E0, (IntPtr)(dpi | (dpi << 16)), ref rectangle);
        foreach (var control in children) SendDpiMessage(control.Handle, 0x02E3, (IntPtr)dpi, IntPtr.Zero);
        dialog.ClientSize = new Size(480 * dpi / 96, 320 * dpi / 96);
        dialog.PerformLayout();
        foreach (var control in children) control.PerformLayout();
        var clipped = children.FirstOrDefault(control => control.Visible && control.Parent is TableLayoutPanel or FlowLayoutPanel
            && (control.Right > control.Parent.ClientSize.Width + 1 || control.Bottom > control.Parent.ClientSize.Height + 1));
        Require(clipped is null, $"Preferences fit at {dpi} DPI{(clipped is null ? "" : $": {clipped.Text} {clipped.Bounds}")}");
        using var screenshot = new Bitmap(dialog.Width, dialog.Height);
        dialog.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
        screenshot.Save(Path.Combine(output, "preferences.png"));
        dialog.Close();
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Console.WriteLine("PASS: " + description);
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr parameter, ref NativeRectangle rectangle);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendDpiMessage(IntPtr window, uint message, IntPtr parameter, IntPtr data);
}
