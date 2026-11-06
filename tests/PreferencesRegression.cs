using System.Reflection;
using Viewcut;

internal static class PreferencesRegression
{
    public static async Task Run(MainWindow app, SessionStore store, string sessionPath)
    {
        var player = Field<Player?>(app, "player");
        var current = Field<string?>(app, "current");
        var browser = Descendants(app).OfType<FileBrowser>().Single();
        var folder = browser.CurrentFolder;
        var clip = store.Data.Clips.FirstOrDefault(item => item.Name is not null);
        var oldName = clip?.Name;
        var clipIds = store.Data.Clips.Select(item => item.Id).ToArray();
        var exports = store.Data.Jobs.ToArray();
        var undo = Descendants(app).OfType<FluentButton>().Single(button => button.Text == L.T("撤销"));
        var redo = Descendants(app).OfType<FluentButton>().Single(button => button.Text == L.T("重做"));
        var history = (undo.Enabled, redo.Enabled);
        Assert(Descendants(app).OfType<FluentButton>().All(button => !string.IsNullOrEmpty(button.Icon) && !string.IsNullOrEmpty(button.ToolTipText)),
            "Every action button has an icon and a named tooltip");
        var preferences = Descendants(app).OfType<Button>().Single(button => button.AccessibleName == "Viewcut Preferences");
        app.BeginInvoke(() => preferences.PerformClick());
        await Until(() => Application.OpenForms.OfType<PreferencesDialog>().Any());
        using var dialog = Application.OpenForms.OfType<PreferencesDialog>().Single();
        var radios = Descendants(dialog).OfType<RadioButton>().ToDictionary(button => button.AccessibleName!);
        radios["en-US"].Checked = true;
        Assert(L.IsEnglish && preferences.Text == "Preferences" && dialog.Text == "Preferences", "English applies instantly to the app and open preferences dialog");
        Assert(Descendants(app).OfType<Button>().Any(button => button.Text == "Choose save folder")
            && Descendants(app).OfType<Button>().Any(button => button.Text == "Open save folder"), "Top toolbar is translated");
        Assert(Descendants(app).OfType<FluentButton>().Single(button => button.Text == "Choose save folder").ToolTipText == "Choose save folder"
            && Descendants(app).OfType<FluentButton>().Single(button => button.Text == "Undo").ToolTipText.Contains("Ctrl+Z"),
            "Icon tooltips follow the language and include keyboard shortcuts");
        var fileList = Descendants(browser).OfType<ListView>().Single();
        Assert(fileList.Columns[0].Text == "Name" && fileList.Columns[1].Text == "Date modified"
            && fileList.AccessibleName == "File browser", "File columns and accessibility text are translated");
        Assert(new ClipRange(Guid.NewGuid(), 2, 1, 2).DisplayName == "Clip 2"
            && new ClipRange(Guid.NewGuid(), 1, 1, 2, "日落 Sunrise").DisplayName == "日落 Sunrise", "Default clip labels translate while custom names remain unchanged");
        Assert(L.Translate("已删除 3 个片段标记。") == "Removed 3 clip markers.", "Existing dynamic status messages translate");
        Assert(L.Translate("片段已命名为 日落 Sunrise。") == "Clip renamed to 日落 Sunrise.", "Status translation preserves user-entered names");
        var timeline = Descendants(app).OfType<ClipTimeline>().Single();
        var menu = Field<ContextMenuStrip>(timeline, "clipMenu");
        Assert(menu.Items[0].Text == "Rename" && menu.Items[1].Text == "Delete", "Existing clip context menu translates immediately");

        foreach (var theme in new[] { "light", "dark", "system", "light" })
        {
            radios[theme].Checked = true;
            Assert(EditorTheme.Mode == theme && store.Data.Theme == theme, $"Theme preference applies and saves: {theme}");
            if (theme == "system")
            {
                using var settings = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var expected = settings?.GetValue("AppsUseLightTheme") is not int value || value != 0;
                Assert(EditorTheme.IsLight == expected, "System appearance follows the actual Windows application theme");
            }
            Assert(app.BackColor == EditorTheme.Background && fileList.BackColor == EditorTheme.Surface
                && fileList.ForeColor == EditorTheme.Text, "Window and file list use the active theme palette");
            Assert(Contrast(EditorTheme.Text, EditorTheme.Surface) >= 4.5
                && Contrast(EditorTheme.Muted, EditorTheme.Raised) >= 4.5
                && Contrast(EditorTheme.OnAccent, EditorTheme.Accent) >= 4.5
                && Contrast(EditorTheme.VideoText, Color.Black) >= 4.5, "Theme text and accent buttons retain readable contrast");
        }
        Assert(player is null || ReferenceEquals(player, Field<Player?>(app, "player")), "Changing preferences does not recreate the player");
        Assert(Field<string?>(app, "current") == current && browser.CurrentFolder == folder
            && store.Data.Clips.Select(item => item.Id).SequenceEqual(clipIds) && store.Data.Jobs.SequenceEqual(exports),
            "Language and theme changes preserve the video, browser folder, clips and export history");
        Assert(clip is null || store.Data.Clips.Single(item => item.Id == clip.Id).Name == oldName, "Language switches never rename saved clips");
        Assert((undo.Enabled, redo.Enabled) == history, "Language and theme changes preserve undo and redo availability");
        var restored = new SessionStore(sessionPath);
        Assert(restored.Data.Language == "en-US" && restored.Data.Theme == "light", "Language and theme persist in the saved session");
        using (var next = new MainWindow(restored, null))
            Assert(next.BackColor == EditorTheme.Background && Descendants(next).OfType<Button>().Any(button => button.Text == "Choose save folder"),
                "A new window restores English and light appearance");
        using (var rename = new RenameClipDialog("日落 Sunrise", _ => null))
            Assert(rename.Text == "Rename clip" && Descendants(rename).OfType<Button>().Any(button => button.Text == "OK")
                && rename.BackColor == EditorTheme.Background, "Rename dialog follows the selected language and theme");
        using (var screenshot = new Bitmap(dialog.Width, dialog.Height))
        {
            dialog.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
            screenshot.Save(Path.Combine(Path.GetDirectoryName(sessionPath)!, "preferences-en-light.png"));
        }
        AssertFits(dialog);
        radios["zh-CN"].Checked = true;
        radios["dark"].Checked = true;
        Assert(preferences.Text == "偏好设置" && dialog.Text == "偏好设置"
            && L.Translate("Removed 3 clip markers.") == "已删除 3 个片段标记。", "Language and theme switch back without restarting");
        Descendants(dialog).OfType<Button>().Single(button => button.Text == "关闭").PerformClick();
        await Until(() => !dialog.Visible);
    }

    private static void AssertFits(Form dialog)
    {
        var clipped = Descendants(dialog).FirstOrDefault(control => control.Visible && control.Parent is TableLayoutPanel or FlowLayoutPanel
            && (control.Right > control.Parent.ClientSize.Width + 1 || control.Bottom > control.Parent.ClientSize.Height + 1));
        Assert(clipped is null, $"Preferences controls fit without clipping{(clipped is null ? "" : $": {clipped.Text} {clipped.Bounds} in {clipped.Parent!.ClientSize}")}");
    }
    private static double Contrast(Color a, Color b)
    {
        double Luminance(Color color)
        {
            double Channel(byte c) { var v = c / 255d; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
            return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        }
        var first = Luminance(a); var second = Luminance(b);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>()
        .SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private static async Task Until(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 10000;
        while (!condition()) { if (Environment.TickCount64 > deadline) throw new TimeoutException("Preference UI did not update."); await Task.Delay(40); }
    }
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
}
