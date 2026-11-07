using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Viewcut;

internal static class BrowserRegression
{
    public static void RunStandalone(string root)
    {
        Directory.CreateDirectory(root);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            using var app = new MainWindow(new SessionStore(Path.Combine(root, "session.json")), null)
                { ShowInTaskbar = false, Opacity = 0 };
            var browser = Descendants(app).OfType<FileBrowser>().Single();
            app.Shown += async (_, _) =>
            {
                try { await Run(app, browser, root); }
                catch (Exception ex) { failure = ex; }
                finally { app.Close(); }
            };
            Application.Run(app);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(60000)) throw new TimeoutException("Browser UI regression exceeded 60 seconds.");
        if (failure is not null) throw new InvalidOperationException("Browser regression failed.", failure);
    }

    public static async Task Run(Form app, FileBrowser browser, string root)
    {
        var folder = Path.Combine(root, "文件操作 " + Guid.NewGuid().ToString("N")[..8]);
        var destination = Path.Combine(folder, "目标 文件夹");
        Directory.CreateDirectory(destination);
        for (var index = 1; index <= 4; index++) File.WriteAllText(Path.Combine(folder, $"素材 {index}.txt"), $"original-{index}");
        await browser.NavigateAsync(folder);
        browser.SetView(false);
        var list = Descendants(browser).OfType<ListView>().Single();
        var file1 = Path.Combine(folder, "素材 1.txt");
        var file2 = Path.Combine(folder, "素材 2.txt");
        var file3 = Path.Combine(folder, "素材 3.txt");
        Assert(list.MultiSelect, "File browser enables native multiple selection");
        browser.SelectPath(file1);
        list.Focus();
        Key(Keys.Control | Keys.A);
        Assert(list.SelectedIndices.Count == 5, "Ctrl+A selects files and folders in the virtual list");
        browser.SelectPath(file1);
        await Click(2, 8); // MK_CONTROL
        Assert(list.SelectedIndices.Count == 2, "Ctrl+click toggles an additional file in the native list");
        await Click(2, 8);
        Assert(list.SelectedIndices.Count == 1, "Ctrl+click toggles a file off");
        await Click(1, 0);
        await Click(4, 4); // MK_SHIFT
        Assert(list.SelectedIndices.Count == 4, "Shift+click selects a contiguous range");

        list.SelectedIndices.Clear();
        var start = new Point(list.ClientSize.Width - 4, list.Items[4].Bounds.Bottom + 25);
        var end = new Point(8, list.Items[1].Bounds.Top + 2);
        PostMessage(list.Handle, 0x201, (IntPtr)1, Coordinates(start));
        for (var step = 1; step <= 12; step++)
        {
            var point = new Point(start.X + (end.X - start.X) * step / 12, start.Y + (end.Y - start.Y) * step / 12);
            PostMessage(list.Handle, 0x200, (IntPtr)1, Coordinates(point));
        }
        PostMessage(list.Handle, 0x202, IntPtr.Zero, Coordinates(end));
        await Task.Delay(120);
        Assert(list.SelectedIndices.Count >= 2, "Dragging from empty space marquee-selects multiple files");

        browser.SetView(true);
        await Task.Delay(100);
        await Click(1, 0);
        await Click(2, 8);
        Assert(list.SelectedIndices.Count == 2, "Large icons support Ctrl multiple selection");
        await Click(0, 0);
        await Click(4, 4);
        Assert(list.SelectedIndices.Count == 5, "Large icons support Shift selection from the first item");
        list.SelectedIndices.Clear();
        list.EnsureVisible(0);
        await Task.Delay(30);
        var iconStart = new Point(list.ClientSize.Width - 2, Math.Min(list.ClientSize.Height - 2, list.Items[4].Bounds.Bottom + 20));
        var iconEnd = new Point(2, list.Items[0].Bounds.Top + 2);
        PostMessage(list.Handle, 0x201, (IntPtr)1, Coordinates(iconStart));
        for (var step = 1; step <= 12; step++)
            PostMessage(list.Handle, 0x200, (IntPtr)1, Coordinates(new Point(iconStart.X + (iconEnd.X - iconStart.X) * step / 12,
                iconStart.Y + (iconEnd.Y - iconStart.Y) * step / 12)));
        PostMessage(list.Handle, 0x202, IntPtr.Zero, Coordinates(iconEnd));
        await Task.Delay(120);
        Assert(list.SelectedIndices.Count >= 2, "Large icons support marquee selection");
        browser.SetView(false);

        using (var menu = new ShellMenu([file1, file2], app.Handle))
        {
            var verbs = menu.Verbs;
            Assert(verbs.Contains("copy") && verbs.Contains("cut") && verbs.Contains("delete") && verbs.Contains("properties"),
                "Multiple files expose the native Explorer operations menu");
        }
        using (var menu = new ShellMenu([file1], app.Handle))
            Assert(menu.Verbs.Contains("rename"), "Native file menu exposes rename");
        // Preserve the user's clipboard even when a regression fails.
        var clipboard = Clipboard.GetDataObject();
        var savedClipboard = new DataObject();
        if (clipboard is not null) foreach (var format in clipboard.GetFormats(false))
        {
            try { if (clipboard.GetData(format, false) is { } data) savedClipboard.SetData(format, data); }
            catch (ExternalException) { }
        }
        try
        {
            browser.SelectPath(file1);
            list.SelectedIndices.Add(2);
            app.Activate();
            list.Focus();
            await Until(() => list.ContainsFocus);
            Key(Keys.Control | Keys.C);
            Assert(Clipboard.GetFileDropList().Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals([file1, file2]),
                "Ctrl+C writes interoperable file-drop clipboard data");
            await browser.NavigateAsync(destination);
            Assert(browser.CurrentFolder == destination, "User navigation wins over automatic folder refresh");
            list.Focus();
            Key(Keys.Control | Keys.V);
            await Until(() => File.Exists(Path.Combine(destination, Path.GetFileName(file1)))
                && File.Exists(Path.Combine(destination, Path.GetFileName(file2))) && !Busy());
            Assert(File.ReadAllText(file1) == "original-1", "Pasting a file selection copies the contents and preserves originals");

            await browser.NavigateAsync(folder, file3);
            list.Focus();
            Key(Keys.Control | Keys.X);
            var effect = (MemoryStream)Clipboard.GetDataObject()!.GetData("Preferred DropEffect")!;
            Assert(effect.ToArray()[0] == 2, "Ctrl+X marks the clipboard as a move for Explorer interoperability");
            await browser.NavigateAsync(destination);
            list.Focus();
            Key(Keys.Control | Keys.V);
            await Until(() => !File.Exists(file3) && File.Exists(Path.Combine(destination, Path.GetFileName(file3))) && !Busy());
            Console.WriteLine("PASS: Cut and paste move the selected file");

            browser.SelectPath(Path.Combine(destination, Path.GetFileName(file1)));
            list.Focus();
            Key(Keys.F2);
            Assert(list.LabelEditInProgress(), "F2 enters native inline filename editing");
            Key(Keys.Control | Keys.A);
            Assert(list.SelectedIndices.Count == 1, "Text-edit shortcuts do not select every file during rename");
            var afterEdit = typeof(ListView).GetMethod("OnAfterLabelEdit", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var index = list.SelectedIndices[0];
            list.Focus();
            SendMessage(list.Handle, 0x1076, (IntPtr)(-1), IntPtr.Zero); // LVM_EDITLABELW: cancel native edit.
            await Until(() => !list.LabelEditInProgress());
            await Task.Delay(100);
            afterEdit.Invoke(list, [new LabelEditEventArgs(index, "重新命名.txt")]);
            await Until(() => File.Exists(Path.Combine(destination, "重新命名.txt")) && !Busy());
            Console.WriteLine("PASS: Inline rename performs a real Shell rename");

            var external = Path.Combine(destination, "外部新建.txt");
            File.WriteAllText(external, "watcher");
            await Until(() => Enumerable.Range(0, list.VirtualListSize).Any(i => list.Items[i].Text == Path.GetFileName(external)));
            Console.WriteLine("PASS: External file changes automatically refresh the browser");
            browser.SelectPath(external);
            await browser.NavigateAsync(destination, addHistory: false);
            Assert(list.SelectedIndices.Count == 1 && list.Items[list.SelectedIndices[0]].Text == Path.GetFileName(external),
                "Refreshing preserves selected file identities");

            var address = Descendants(browser).OfType<TextBox>().Single(box => box.AccessibleName == "文件夹地址");
            address.Focus();
            address.SelectAll();
            Assert(!Key(Keys.Delete), "Delete in the address textbox is left to text editing");
            Assert(!Key(Keys.Control | Keys.Z), "Ctrl+Z in the address textbox is left to text editing");
            Assert(File.Exists(external), "Editing the address cannot delete selected files");
        }
        finally { Clipboard.SetDataObject(savedClipboard, true, 40, 100); }

        var dragFile = Path.Combine(folder, "拖入测试.txt");
        File.WriteAllText(dragFile, "drag-copy");
        var dragData = new DataObject(DataFormats.FileDrop, new[] { dragFile });
        var dropPoint = list.PointToScreen(new Point(list.ClientSize.Width - 4, list.ClientSize.Height - 4));
        var drag = new DragEventArgs(dragData, 8, dropPoint.X, dropPoint.Y, DragDropEffects.Copy | DragDropEffects.Move, DragDropEffects.None);
        typeof(Control).GetMethod("OnDragOver", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(list, [drag]);
        Assert(drag.Effect == DragDropEffects.Copy, "Ctrl dragging chooses a copy into the current directory");
        typeof(Control).GetMethod("OnDragDrop", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(list, [drag]);
        await Until(() => File.Exists(Path.Combine(destination, Path.GetFileName(dragFile))) && !Busy());
        Assert(File.ReadAllText(Path.Combine(destination, Path.GetFileName(dragFile))) == "drag-copy" && File.Exists(dragFile),
            "Dropping files copies their actual contents into the sidebar folder");

        var batch = new[] { Path.Combine(destination, "素材 2.txt"), Path.Combine(destination, "素材 3.txt") };
        Assert(await ShellFiles.ExecuteAsync(app.Handle, FileAction.Rename, batch, name: "批量素材", quiet: true),
            "Shell batch rename completes");
        Assert(File.Exists(Path.Combine(destination, "批量素材 (1).txt")) && File.Exists(Path.Combine(destination, "批量素材 (2).txt")),
            "Batch rename numbers each item while preserving its extension");
        Assert(await ShellFiles.ExecuteAsync(app.Handle, FileAction.NewFolder, [], destination, "新文件夹", quiet: true)
            && Directory.Exists(Path.Combine(destination, "新文件夹")), "Shell creates a Unicode-named folder");
        var recycle = Path.Combine(destination, "Viewcut-test-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(recycle, "recycle-fixture");
        await browser.NavigateAsync(destination, recycle, addHistory: false);
        list.Focus();
        Key(Keys.Delete);
        await Until(() => !File.Exists(recycle) && !Busy());
        Console.WriteLine("PASS: Delete key recycles the selected file from the sidebar");
        // Restore only this uniquely named fixture; never empty the user's recycle bin.
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        try
        {
            dynamic bin = shell.NameSpace(10);
            dynamic items = bin.Items();
            bool restored = false;
            for (var i = 0; i < items.Count; i++)
            {
                dynamic item = items.Item(i);
                if (!((string)item.Name).Contains(Path.GetFileNameWithoutExtension(recycle), StringComparison.Ordinal)) continue;
                item.InvokeVerb("undelete");
                await Until(() => File.Exists(recycle));
                restored = true;
                Marshal.ReleaseComObject(item);
                break;
            }
            Marshal.ReleaseComObject(items);
            Marshal.ReleaseComObject(bin);
            Assert(restored && File.ReadAllText(recycle) == "recycle-fixture", "Deleted file is recoverable from Windows Recycle Bin");
        }
        finally { Marshal.ReleaseComObject(shell); }

        async Task Click(int item, int modifier)
        {
            list.EnsureVisible(item);
            await Task.Delay(30);
            var bounds = list.Items[item].GetBounds(ItemBoundsPortion.Label);
            var point = new Point(bounds.Left + 10, bounds.Top + bounds.Height / 2);
            if (list.GetItemAt(point.X, point.Y)?.Index != item)
                throw new InvalidOperationException($"Native item hit failed: item={item}, bounds={bounds}, client={list.ClientRectangle}");
            PostMessage(list.Handle, 0x201, (IntPtr)(1 | modifier), Coordinates(point));
            PostMessage(list.Handle, 0x202, (IntPtr)modifier, Coordinates(point));
            await Task.Delay(180);
        }
        bool Busy() => (bool)typeof(FileBrowser).GetField("operating", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(browser)!;
        bool Key(Keys key)
        {
            var message = Message.Create(browser.Handle, 0x100, (IntPtr)(key & Keys.KeyCode), IntPtr.Zero);
            return (bool)typeof(FileBrowser).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(browser, [message, key])!;
        }
    }

    private static async Task Until(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed.TotalSeconds > 15) throw new TimeoutException("Browser operation did not finish.");
            await Task.Delay(40);
        }
    }
    private static IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>()
        .SelectMany(control => new[] { control }.Concat(Descendants(control)));
    private static void Assert(bool result, string message)
    {
        if (!result) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
    private static IntPtr Coordinates(Point point) => (IntPtr)((point.Y << 16) | (point.X & 0xffff));
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
