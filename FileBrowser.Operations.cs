using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Viewcut;

internal sealed partial class FileBrowser
{
    private readonly ContextMenuStrip commandMenu = EditorTheme.ContextMenu();
    private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 350 };
    private FileSystemWatcher? watcher;
    private bool updatingEntries;
    private bool navigating;
    private bool operating;
    private int sortColumn;
    private bool sortDescending;
    private string? dropFolder;
    private HashSet<string> cutPaths = new(StringComparer.OrdinalIgnoreCase);
    public bool FileOperationInProgress => operating;
    private BrowserEntry[] SelectedEntries => entries.SelectedIndices.Cast<int>()
        .Where(index => index >= 0 && index < visible.Length).Select(index => visible[index]).ToArray();

    private void InitializeOperations()
    {
        entries.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            if (entries.GetItemAt(e.X, e.Y) is null) ShowCommands(entries.PointToScreen(e.Location));
            else ShowFileMenu(entries.PointToScreen(e.Location));
        };
        entries.AfterLabelEdit += (_, e) =>
        {
            e.CancelEdit = true;
            if (e.Label is null || e.Item < 0 || e.Item >= visible.Length) return;
            if (string.Equals(e.Label, visible[e.Item].Name, StringComparison.Ordinal)) return;
            var path = visible[e.Item].Path;
            if (ValidateFileName(e.Label) is { } error) { ShowFileError(error); return; }
            _ = RunFileOperationAsync(FileAction.Rename, [path], name: e.Label);
        };
        entries.ColumnClick += (_, e) =>
        {
            sortDescending = sortColumn == e.Column && !sortDescending;
            sortColumn = e.Column;
            ApplySort();
        };
        entries.ItemDrag += (_, _) =>
        {
            var paths = SelectedEntries.Select(entry => entry.Path).ToArray();
            if (paths.Length == 0 || operating) return;
            entries.DoDragDrop(FileData(paths, false), DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            ScheduleRefresh();
        };
        entries.DragEnter += (_, e) => UpdateDrop(e);
        entries.DragOver += (_, e) => UpdateDrop(e);
        entries.DragLeave += (_, _) => dropFolder = null;
        entries.DragDrop += async (_, e) =>
        {
            UpdateDrop(e);
            if (e.Effect == DragDropEffects.None || e.Data?.GetData(DataFormats.FileDrop) is not string[] paths) return;
            var destination = dropFolder;
            dropFolder = null;
            await RunFileOperationAsync(e.Effect == DragDropEffects.Move ? FileAction.Move : FileAction.Copy, paths, destination);
        };
        refreshTimer.Tick += async (_, _) =>
        {
            refreshTimer.Stop();
            if (!operating && !entries.LabelEditInProgress()) await NavigateAsync(CurrentFolder, addHistory: false);
            else refreshTimer.Start();
        };
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (entries.LabelEditInProgress()) return base.ProcessCmdKey(ref message, keyData);
        if (keyData == (Keys.Control | Keys.L)) { address.Focus(); address.SelectAll(); return true; }
        if (keyData == (Keys.Control | Keys.F)) { search.Focus(); search.SelectAll(); return true; }
        if (EditingPath) return base.ProcessCmdKey(ref message, keyData);
        if (keyData == Keys.F5) { ScheduleRefresh(); return true; }
        if (keyData == (Keys.Alt | Keys.Left)) { BrowseHistory(-1); return true; }
        if (keyData == (Keys.Alt | Keys.Right)) { BrowseHistory(1); return true; }
        if (keyData is Keys.Back || keyData == (Keys.Alt | Keys.Up)) { Up(); return true; }
        if (!entries.ContainsFocus) return base.ProcessCmdKey(ref message, keyData);
        switch (keyData)
        {
            case Keys.Control | Keys.A: SelectAllEntries(); return true;
            case Keys.Control | Keys.C: CopySelection(false); return true;
            case Keys.Control | Keys.X: CopySelection(true); return true;
            case Keys.Control | Keys.V: _ = PasteAsync(); return true;
            case Keys.Control | Keys.Shift | Keys.C: CopyPaths(); return true;
            case Keys.Delete: _ = DeleteFilesAsync(false); return true;
            case Keys.Shift | Keys.Delete: _ = DeleteFilesAsync(true); return true;
            case Keys.F2: RenameSelection(); return true;
            case Keys.Control | Keys.Shift | Keys.N: _ = NewFolderAsync(); return true;
            case Keys.Alt | Keys.Enter: ShowProperties(); return true;
            case Keys.Apps:
            case Keys.Shift | Keys.F10:
                ShowFileMenu(entries.PointToScreen(entries.FocusedItem?.Bounds.Location ?? new Point(8, 8))); return true;
        }
        return base.ProcessCmdKey(ref message, keyData);
    }

    private void ShowFileMenu(Point screen)
    {
        var selection = SelectedEntries;
        var paths = selection.Select(entry => entry.Path).ToArray();
        if (paths.Length == 0) { ShowCommands(screen); return; }
        if (operating) return;
        try
        {
            using var menu = new ShellMenu(paths, FindForm()!.Handle);
            menu.Show(this, screen, ActivateSelection, command =>
            {
                switch (command)
                {
                    case "copy": CopySelection(false); break;
                    case "cut": CopySelection(true); break;
                    case "delete": _ = DeleteFilesAsync((ModifierKeys & Keys.Shift) != 0); break;
                    case "rename": RenameSelection(); break;
                }
            }, reviewable: selection.Length == 1 && (selection[0].Directory || selection[0].Video));
            ScheduleRefresh();
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException) { ShowFileError(ex.Message); }
    }

    private void ShowCommands(Point? screen = null)
    {
        commandMenu.Items.Clear();
        void Add(string text, Action action, bool enabled = true)
        {
            var item = commandMenu.Items.Add(text, null, (_, _) => action());
            item.Enabled = enabled && !operating;
        }
        var any = SelectedEntries.Length > 0;
        Add("剪切    Ctrl+X", () => CopySelection(true), any);
        Add("复制    Ctrl+C", () => CopySelection(false), any);
        Add("粘贴    Ctrl+V", () => _ = PasteAsync(), HasClipboardFiles());
        Add("复制为路径    Ctrl+Shift+C", CopyPaths, any);
        commandMenu.Items.Add(new ToolStripSeparator());
        Add("重命名    F2", RenameSelection, any);
        Add("删除    Delete", () => _ = DeleteFilesAsync(false), any);
        Add("新建文件夹    Ctrl+Shift+N", () => _ = NewFolderAsync(), CurrentFolder is not null);
        Add("属性    Alt+Enter", ShowProperties, any);
        commandMenu.Items.Add(new ToolStripSeparator());
        Add("全选    Ctrl+A", SelectAllEntries, visible.Length > 0);
        Add("反向选择", () =>
        {
            entries.BeginUpdate();
            updatingEntries = true;
            for (var index = 0; index < visible.Length; index++) entries.Items[index].Selected = !entries.Items[index].Selected;
            updatingEntries = false;
            entries.EndUpdate();
            info.Text = $"{visible.Length} 个项目　|　已选择 {entries.SelectedIndices.Count} 项";
        });
        var sort = new ToolStripMenuItem("排序");
        foreach (var column in new[] { 0, 1, 2 })
        {
            var item = new ToolStripMenuItem(entries.Columns[column].Text) { Checked = sortColumn == column };
            item.Click += (_, _) => { sortColumn = column; ApplySort(); };
            sort.DropDownItems.Add(item);
        }
        var descending = new ToolStripMenuItem("降序") { Checked = sortDescending };
        descending.Click += (_, _) => { sortDescending = !sortDescending; ApplySort(); };
        sort.DropDownItems.Add(descending);
        commandMenu.Items.Add(sort);
        Add("刷新    F5", ScheduleRefresh);
        Add("在 Windows 资源管理器中打开", () => { if (CurrentFolder is { } folder) OpenExternal(folder); });
        commandMenu.Show(screen ?? entries.PointToScreen(new Point(8, 8)));
    }

    private void SelectAllEntries()
    {
        entries.BeginUpdate();
        // LVM_SETITEMSTATE with -1 selects all in one native call, including virtual lists.
        var item = new ListItemState { StateMask = 2, State = 2 };
        SendMessage(entries.Handle, 0x102B, (IntPtr)(-1), ref item);
        entries.EndUpdate();
        entries.Focus();
    }

    private static DataObject FileData(string[] paths, bool cut)
    {
        var data = new DataObject();
        var files = new StringCollection();
        files.AddRange(paths);
        data.SetFileDropList(files);
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 1)));
        return data;
    }

    private void CopySelection(bool cut)
    {
        var paths = SelectedEntries.Select(entry => entry.Path).ToArray();
        if (paths.Length == 0) return;
        try
        {
            Clipboard.SetDataObject(FileData(paths, cut), true, 20, 100);
            cutPaths = cut ? paths.ToHashSet(StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase);
            info.Text = $"已{(cut ? "剪切" : "复制")} {paths.Length} 项，选择目标文件夹后粘贴。";
            entries.Invalidate();
        }
        catch (ExternalException) { info.Text = "剪贴板正被其他程序使用，请稍后重试。"; }
    }

    private void CopyPaths()
    {
        var paths = SelectedEntries.Select(entry => $"\"{entry.Path}\"").ToArray();
        if (paths.Length == 0) return;
        try { Clipboard.SetText(string.Join(Environment.NewLine, paths)); }
        catch (ExternalException ex) { ShowFileError(ex.Message); }
    }

    private static bool HasClipboardFiles()
    {
        try { return Clipboard.ContainsFileDropList(); }
        catch (ExternalException) { return false; }
    }

    private async Task PasteAsync()
    {
        if (CurrentFolder is not { } destination || operating) return;
        try
        {
            var data = Clipboard.GetDataObject();
            if (data?.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
            var move = data.GetData("Preferred DropEffect") is MemoryStream effect && effect.ToArray() is { Length: > 0 } bytes && (bytes[0] & 2) != 0;
            if (await RunFileOperationAsync(move ? FileAction.Move : FileAction.Copy, paths, destination) && move)
            {
                var current = Clipboard.GetFileDropList().Cast<string>();
                if (current.SequenceEqual(paths, StringComparer.OrdinalIgnoreCase)) Clipboard.Clear();
                cutPaths.Clear();
            }
        }
        catch (Exception ex) when (ex is ExternalException or IOException) { ShowFileError(ex.Message); }
    }

    private Task<bool> DeleteFilesAsync(bool permanently)
    {
        var paths = SelectedEntries.Select(entry => entry.Path).ToArray();
        if (paths.Length == 0 || operating) return Task.FromResult(false);
        if (permanently && MessageBox.Show(this, $"永久删除选中的 {paths.Length} 项？此操作不会移入回收站。",
            "永久删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return Task.FromResult(false);
        return RunFileOperationAsync(permanently ? FileAction.Delete : FileAction.Recycle, paths);
    }

    private void RenameSelection()
    {
        if (operating) return;
        var selected = SelectedEntries;
        entries.Focus();
        if (selected.Length == 1) entries.Items[entries.SelectedIndices[0]].BeginEdit();
        else if (selected.Length > 1)
        {
            using var dialog = new RenameClipDialog(Path.GetFileNameWithoutExtension(selected[0].Name), ValidateFileName,
                "批量重命名", "名称", "文件将依次命名为“名称 (1)”“名称 (2)”等，文件扩展名保留。");
            if (dialog.ShowDialog(this) == DialogResult.OK)
                _ = RunFileOperationAsync(FileAction.Rename, selected.Select(entry => entry.Path).ToArray(), name: dialog.ClipName);
        }
    }

    private async Task NewFolderAsync()
    {
        if (CurrentFolder is not { } folder || operating) return;
        var name = "新建文件夹";
        for (var number = 2; Directory.Exists(Path.Combine(folder, name)) || File.Exists(Path.Combine(folder, name)); number++)
            name = $"新建文件夹 ({number})";
        if (await RunFileOperationAsync(FileAction.NewFolder, [], folder, name) && CurrentFolder == folder)
        {
            SelectPath(Path.Combine(folder, name));
            RenameSelection();
        }
    }

    private static string? ValidateFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.EndsWith('.') || name.EndsWith(' ')) return "请输入有效的文件或文件夹名称。";
        var stem = name.Split('.')[0];
        if (new[] { "CON", "PRN", "AUX", "NUL" }.Contains(stem, StringComparer.OrdinalIgnoreCase)
            || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '1' and <= '9'))
            return "该名称是 Windows 保留名称，请使用其他名称。";
        return null;
    }

    private void ShowProperties()
    {
        var paths = SelectedEntries.Select(entry => entry.Path).ToArray();
        if (paths.Length == 0) return;
        try { using var menu = new ShellMenu(paths, FindForm()!.Handle); menu.InvokeVerb(FindForm()!.Handle, "properties"); }
        catch (COMException ex) { ShowFileError(ex.Message); }
    }

    private async Task<bool> RunFileOperationAsync(FileAction action, string[] paths, string? destination = null, string? name = null)
    {
        if (operating) return false;
        operating = true;
        info.Text = "正在处理文件…";
        try { return await ShellFiles.ExecuteAsync(FindForm()!.Handle, action, paths, destination, name); }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException)
        { if (!IsDisposed) ShowFileError(ex.Message); return false; }
        finally
        {
            try { if (!IsDisposed) await NavigateAsync(CurrentFolder, addHistory: false); }
            finally { operating = false; }
        }
    }

    private void UpdateDrop(DragEventArgs e)
    {
        e.Effect = DragDropEffects.None;
        dropFolder = null;
        if (operating || CurrentFolder is null || e.Data?.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        var hit = entries.GetItemAt(entries.PointToClient(new Point(e.X, e.Y)).X, entries.PointToClient(new Point(e.X, e.Y)).Y);
        dropFolder = hit is not null && visible[hit.Index].Directory ? visible[hit.Index].Path : CurrentFolder;
        if (paths.Any(path => string.Equals(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar),
            dropFolder.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            || dropFolder.StartsWith(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))) return;
        var move = (e.KeyState & 4) != 0 || (e.KeyState & 8) == 0 && paths.All(path =>
            string.Equals(Path.GetPathRoot(path), Path.GetPathRoot(dropFolder), StringComparison.OrdinalIgnoreCase));
        e.Effect = move ? DragDropEffects.Move : DragDropEffects.Copy;
        e.Effect &= e.AllowedEffect;
    }

    private void RestoreSelection(string[] paths)
    {
        var selected = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < visible.Length; index++)
            if (selected.Contains(visible[index].Path)) entries.SelectedIndices.Add(index);
    }

    private BrowserEntry[] SortEntries(IEnumerable<BrowserEntry> items)
    {
        var comparer = Comparer<BrowserEntry>.Create((a, b) =>
        {
            if (a.Directory != b.Directory) return a.Directory ? -1 : 1;
            var order = sortColumn switch
            {
                1 => a.Modified.CompareTo(b.Modified),
                2 => a.Size.CompareTo(b.Size),
                _ => StrCmpLogicalW(a.Name, b.Name)
            };
            if (order == 0) order = StrCmpLogicalW(a.Name, b.Name);
            return sortDescending ? -order : order;
        });
        return items.Order(comparer).ToArray();
    }

    private void ApplySort()
    {
        var selected = SelectedEntries.Select(entry => entry.Path).ToArray();
        Filter();
        updatingEntries = true;
        entries.SelectedIndices.Clear();
        RestoreSelection(selected);
        updatingEntries = false;
    }

    private void WatchFolder(string folder)
    {
        if (watcher?.Path == folder) return;
        watcher?.Dispose();
        watcher = null;
        try
        {
            watcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                | NotifyFilters.Size | NotifyFilters.LastWrite, SynchronizingObject = this };
            watcher.Changed += (_, _) => ScheduleRefresh();
            watcher.Created += (_, _) => ScheduleRefresh();
            watcher.Deleted += (_, _) => ScheduleRefresh();
            watcher.Renamed += (_, _) => ScheduleRefresh();
            watcher.Error += (_, _) => ScheduleRefresh();
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { info.Text = "自动刷新不可用，可按 F5 刷新。"; }
    }

    private void ScheduleRefresh()
    {
        if (IsDisposed || navigating) return;
        refreshTimer.Stop();
        refreshTimer.Start();
    }

    private void ShowFileError(string error) => MessageBox.Show(this, error, "文件操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private void OpenExternal(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException) { ShowFileError(ex.Message); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ListItemState { public uint Mask; public int Item, SubItem; public uint State, StateMask; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr index, ref ListItemState item);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string first, string second);
}

internal static class ListEditing
{
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    public static bool LabelEditInProgress(this ListView list) => list.IsHandleCreated
        && SendMessage(list.Handle, 0x1018, IntPtr.Zero, IntPtr.Zero) != IntPtr.Zero;
}
