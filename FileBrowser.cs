using System.Globalization;
using System.Runtime.InteropServices;

namespace Viewcut;

internal sealed record BrowserEntry(string Path, string Name, bool Directory, long Size, DateTime Modified)
{
    public bool Video => !Directory && System.IO.Path.GetExtension(Path).ToLowerInvariant() is ".mp4" or ".mov";
}

internal sealed partial class FileBrowser : UserControl
{
    private readonly TextBox address = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None };
    private readonly TextBox search = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, PlaceholderText = L.T("搜索当前文件夹") };
    private readonly ListView entries = new BufferedListView { Dock = DockStyle.Fill, VirtualMode = true, OwnerDraw = true,
        BorderStyle = BorderStyle.None, MultiSelect = true, HideSelection = false, ShowItemToolTips = true,
        LabelEdit = true, AllowDrop = true };
    private readonly TreeView places = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
        ShowLines = false, ShowRootLines = false, FullRowSelect = true, HideSelection = false };
    private readonly Label info = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly FluentButton back;
    private readonly FluentButton forward;
    private readonly FluentButton large;
    private readonly FluentButton details;
    private readonly ImageList tileSize = new() { ImageSize = new Size(132, 84), ColorDepth = ColorDepth.Depth32Bit };
    private readonly List<string> history = [];
    private readonly Dictionary<string, Bitmap?> thumbnails = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<BrowserEntry> thumbnailQueue = new();
    private readonly HashSet<string> requested = new(StringComparer.OrdinalIgnoreCase);
    private BrowserEntry[] all = [];
    private BrowserEntry[] visible = [];
    private int historyIndex = -1;
    private int request;
    private bool thumbnailBusy;
    private bool fittingColumns;
    private string? selectedPath;
    private string? playingPath;

    public string? CurrentFolder { get; private set; }
    public bool LargeIcons => entries.View == View.LargeIcon;
    public bool EditingPath => address.Focused || search.Focused;
    public bool EditingText => EditingPath || entries.LabelEditInProgress();
    public string[] VideoFiles => all.Where(entry => entry.Video).Select(entry => entry.Path).ToArray();
    public event Action<string>? VideoActivated;
    public event Action? PreferencesChanged;

    public FileBrowser()
    {
        SuspendLayout();
        AutoScaleMode = AutoScaleMode.Inherit;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        BackColor = EditorTheme.Surface;
        ForeColor = EditorTheme.Text;
        address.BackColor = search.BackColor = EditorTheme.Raised;
        address.ForeColor = search.ForeColor = EditorTheme.Text;
        address.AccessibleName = L.T("文件夹地址");
        search.AccessibleName = L.T("搜索当前文件夹");
        entries.AccessibleName = L.T("文件浏览器");
        entries.BackColor = places.BackColor = EditorTheme.Surface;
        entries.ForeColor = places.ForeColor = EditorTheme.Text;
        entries.Columns.Add(L.T("名称"), 210);
        entries.Columns.Add(L.T("修改日期"), 125);
        entries.Columns.Add(L.T("大小"), 80);
        entries.LargeImageList = tileSize;
        entries.TileSize = new Size(148, 130);
        entries.View = View.LargeIcon;
        entries.FullRowSelect = true;
        places.ItemHeight = 32;
        places.Indent = 14;
        places.DrawMode = TreeViewDrawMode.OwnerDrawAll;
        places.ShowNodeToolTips = true;

        back = Icon("\uE72B", L.T("后退"), () => BrowseHistory(-1));
        forward = Icon("\uE72A", L.T("前进"), () => BrowseHistory(1));
        var up = Icon("\uE74A", L.T("向上一级"), Up);
        var refresh = Icon("\uE72C", L.T("刷新"), () => _ = NavigateAsync(CurrentFolder));
        var navigation = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
        navigation.Controls.AddRange([back, forward, up, refresh]);
        var pathBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, AutoSize = true };
        pathBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathBar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        pathBar.Controls.Add(navigation, 0, 0);
        pathBar.Controls.Add(InputContainer(address), 1, 0);

        large = Icon("\uE80A", L.T("大图标"), () => SetView(true));
        details = Icon("\uE8FD", L.T("列表"), () => SetView(false));
        var viewBar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
        viewBar.Controls.AddRange([
            new Label { Text = L.T("查看"), AutoSize = true, ForeColor = EditorTheme.Muted, Margin = new Padding(3, 10, 8, 0) },
            large, details, Icon("\uE712", L.T("文件操作"), () => ShowCommands())
        ]);
        var commandBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, AutoSize = true };
        commandBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        commandBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        commandBar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        commandBar.Controls.Add(viewBar, 0, 0);
        commandBar.Controls.Add(InputContainer(search), 1, 0);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.Controls.Add(places, 0, 0);
        body.Controls.Add(entries, 1, 0);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(6) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.Controls.Add(pathBar, 0, 0);
        layout.Controls.Add(commandBar, 0, 1);
        layout.Controls.Add(body, 0, 2);
        layout.Controls.Add(info, 0, 3);
        Controls.Add(layout);
        info.ForeColor = EditorTheme.Muted;

        AddPlace(L.T("主文件夹"), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        AddPlace(L.T("桌面"), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        AddPlace(L.T("视频"), Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
        var computer = places.Nodes.Add(L.T("此电脑"));
        foreach (var drive in DriveInfo.GetDrives()) AddPlace(drive.Name, drive.Name, computer);
        computer.Expand();
        places.AfterSelect += (_, e) =>
        {
            if (e.Node?.Tag is string path) _ = NavigateAsync(path);
        };
        places.DrawNode += DrawPlace;
        entries.RetrieveVirtualItem += (_, e) =>
        {
            var entry = visible[e.ItemIndex];
            e.Item = new ListViewItem([entry.Name, entry.Modified.ToString("yyyy/M/d HH:mm"), entry.Directory ? "" : SizeText(entry.Size)])
                { Tag = entry, ImageIndex = -1, ToolTipText = entry.Path };
        };
        entries.ItemActivate += (_, _) => ActivateSelection();
        entries.SelectedIndexChanged += (_, _) =>
        {
            if (updatingEntries) return;
            var selection = SelectedEntries;
            selectedPath = selection.FirstOrDefault()?.Path;
            info.Text = selection.Length == 0 ? L.F("{0} 个项目", visible.Length)
                : selection.Length == 1 ? L.F("{0} 个项目　|　{1}", visible.Length, selection[0].Name)
                : L.F("{0} 个项目　|　已选择 {1} 项", visible.Length, selection.Length);
            entries.Invalidate();
        };
        entries.DrawItem += DrawEntry;
        entries.DrawSubItem += DrawDetails;
        entries.DrawColumnHeader += (_, e) =>
        {
            using var fill = new SolidBrush(EditorTheme.Background);
            e.Graphics.FillRectangle(fill, e.Bounds);
            DrawText(e.Graphics, e.Header?.Text ?? "", e.Bounds, EditorTheme.Muted);
        };
        entries.HandleCreated += (_, _) =>
        {
            SetWindowTheme(entries.Handle, EditorTheme.IsLight ? "Explorer" : "DarkMode_Explorer", null);
            UpdateMetrics();
        };
        entries.SizeChanged += (_, _) => FitColumns();
        places.HandleCreated += (_, _) => SetWindowTheme(places.Handle, EditorTheme.IsLight ? "Explorer" : "DarkMode_Explorer", null);
        InitializeOperations();
        address.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            _ = NavigateAsync(Environment.ExpandEnvironmentVariables(address.Text.Trim().Trim('"')));
        };
        search.TextChanged += (_, _) => Filter();
        SetView(true, notify: false);
        ResumeLayout(performLayout: true);
    }

    public async Task NavigateAsync(string? path, string? select = null, bool addHistory = true)
    {
        if (string.IsNullOrWhiteSpace(path) || IsDisposed) return;
        refreshTimer.Stop();
        navigating = true;
        var ticket = ++request;
        info.Text = L.T("正在读取文件夹…");
        try
        {
            path = Path.GetFullPath(path);
            var target = path;
            var selection = string.Equals(target, CurrentFolder, StringComparison.OrdinalIgnoreCase) && select is null
                ? SelectedEntries.Select(entry => entry.Path).ToArray() : select is null ? [] : new[] { select };
            var result = await Task.Run(() => ReadFolder(target));
            if (ticket != request || IsDisposed) return;
            CurrentFolder = target;
            if (addHistory && (historyIndex < 0 || !string.Equals(history[historyIndex], target, StringComparison.OrdinalIgnoreCase)))
            {
                history.RemoveRange(historyIndex + 1, history.Count - historyIndex - 1);
                history.Add(target);
                historyIndex = history.Count - 1;
            }
            address.Text = target;
            all = result;
            updatingEntries = true;
            entries.SelectedIndices.Clear();
            selectedPath = select;
            ClearThumbnails();
            if (search.TextLength > 0) search.Clear();
            else Filter();
            RestoreSelection(selection);
            updatingEntries = false;
            WatchFolder(target);
            back.Enabled = historyIndex > 0;
            forward.Enabled = historyIndex < history.Count - 1;
            PreferencesChanged?.Invoke();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (ticket != request || IsDisposed) return;
            address.Text = CurrentFolder ?? path;
            info.Text = L.F("无法打开文件夹　{0}", ex.Message);
        }
        finally { if (ticket == request) navigating = false; }
    }

    private static BrowserEntry[] ReadFolder(string path)
    {
        return new DirectoryInfo(path).EnumerateFileSystemInfos("*", new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
        }).Select(item =>
        {
            var directory = (item.Attributes & FileAttributes.Directory) != 0;
            return new BrowserEntry(item.FullName, item.Name, directory, directory ? 0 : ((FileInfo)item).Length, item.LastWriteTime);
        }).OrderByDescending(entry => entry.Directory).ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private void Filter()
    {
        var wasUpdating = updatingEntries;
        updatingEntries = true;
        entries.VirtualListSize = 0;
        visible = SortEntries(all.Where(entry => entry.Name.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase)));
        entries.VirtualListSize = visible.Length;
        FitColumns();
        SelectPath(selectedPath);
        info.Text = L.F("{0} 个项目", visible.Length);
        updatingEntries = wasUpdating;
        entries.Invalidate();
    }

    public void SelectPath(string? path)
    {
        selectedPath = path;
        entries.SelectedIndices.Clear();
        var index = Array.FindIndex(visible, entry => string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;
        entries.SelectedIndices.Add(index);
        entries.EnsureVisible(index);
    }

    public void SetPlayingFile(string path)
    {
        playingPath = path;
        entries.Invalidate();
    }

    public void ApplyPreferences()
    {
        L.ApplyTree(this);
        EditorTheme.ApplyTree(this);
        void TranslateNodes(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                if (L.Key(node.Text) is { } key) node.Text = L.T(key);
                TranslateNodes(node.Nodes);
            }
        }
        TranslateNodes(places.Nodes);
        var count = entries.SelectedIndices.Count;
        info.Text = operating ? L.T("正在处理文件…") : count > 0
            ? L.F("{0} 个项目　|　已选择 {1} 项", visible.Length, count) : L.F("{0} 个项目", visible.Length);
        FitColumns();
        places.Invalidate();
        entries.Invalidate();
    }

    private static IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>()
        .SelectMany(child => new[] { child }.Concat(Descendants(child)));

    public void SetView(bool largeIcons, bool notify = true)
    {
        entries.View = largeIcons ? View.LargeIcon : View.Details;
        FitColumns();
        large.Emphasized = largeIcons;
        details.Emphasized = !largeIcons;
        large.Invalidate();
        details.Invalidate();
        entries.Invalidate();
        if (notify) PreferencesChanged?.Invoke();
    }

    private void ActivateSelection()
    {
        if (entries.SelectedIndices.Count != 1) return;
        var entry = visible[entries.SelectedIndices[0]];
        if (entry.Directory) _ = NavigateAsync(entry.Path);
        else if (entry.Video) VideoActivated?.Invoke(entry.Path);
        else OpenExternal(entry.Path);
    }

    private void Up()
    {
        if (CurrentFolder is not { } path) return;
        var parent = Directory.GetParent(path);
        if (parent is not null) _ = NavigateAsync(parent.FullName, path);
    }

    private async void BrowseHistory(int offset)
    {
        var index = historyIndex + offset;
        if (index < 0 || index >= history.Count) return;
        var previous = historyIndex;
        var target = history[index];
        var ticket = request + 1;
        historyIndex = index;
        await NavigateAsync(target, addHistory: false);
        if (ticket == request && !string.Equals(CurrentFolder, target, StringComparison.OrdinalIgnoreCase))
        {
            historyIndex = previous;
            back.Enabled = historyIndex > 0;
            forward.Enabled = historyIndex < history.Count - 1;
        }
    }

    private void AddPlace(string name, string path, TreeNode? parent = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var node = new TreeNode(name) { Tag = path, ToolTipText = path };
        (parent?.Nodes ?? places.Nodes).Add(node);
    }

    private void DrawPlace(object? sender, DrawTreeNodeEventArgs e)
    {
        if (e.Node is null) return;
        var rect = new Rectangle(0, e.Bounds.Y, places.ClientSize.Width, e.Bounds.Height);
        using var fill = new SolidBrush(e.Node.IsSelected ? EditorTheme.Border : EditorTheme.Surface);
        e.Graphics.FillRectangle(fill, rect);
        var left = Scale(8) + e.Node.Level * places.Indent;
        var iconBounds = new Rectangle(left, rect.Top, Scale(24), rect.Height);
        using var iconFont = new Font(UiFonts.Icons, Scale(16), GraphicsUnit.Pixel);
        TextRenderer.DrawText(e.Graphics, UiFonts.Glyph(e.Node.Parent is not null ? "\uEDA2" : "\uE8B7"), iconFont,
            iconBounds, e.Node.Parent is not null ? EditorTheme.Muted : EditorTheme.Folder,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        var textLeft = iconBounds.Right + Scale(8);
        DrawText(e.Graphics, e.Node.Text, new Rectangle(textLeft, rect.Top, Math.Max(1, rect.Right - textLeft - Scale(4)), rect.Height), EditorTheme.Text);
    }

    private void DrawEntry(object? sender, DrawListViewItemEventArgs e)
    {
        if (!LargeIcons) return;
        var entry = visible[e.ItemIndex];
        var selected = e.Item?.Selected == true;
        var cut = cutPaths.Contains(entry.Path);
        var playing = string.Equals(entry.Path, playingPath, StringComparison.OrdinalIgnoreCase);
        using var background = new SolidBrush(selected ? EditorTheme.Border : playing ? EditorTheme.Playing : EditorTheme.Surface);
        var bounds = Rectangle.Inflate(e.Bounds, -Scale(3), -Scale(2));
        e.Graphics.FillRectangle(background, bounds);
        var scale = DeviceDpi / 96f;
        var imageBounds = new Rectangle(bounds.X + (int)(8 * scale), bounds.Y + (int)(5 * scale),
            Math.Max(1, bounds.Width - (int)(16 * scale)), (int)(76 * scale));
        if (thumbnails.TryGetValue(entry.Path, out var image) && image is not null)
        {
            var ratio = Math.Min(imageBounds.Width / (float)image.Width, imageBounds.Height / (float)image.Height);
            var size = new Size((int)(image.Width * ratio), (int)(image.Height * ratio));
            e.Graphics.DrawImage(image, new Rectangle(imageBounds.Left + (imageBounds.Width - size.Width) / 2,
                imageBounds.Top + (imageBounds.Height - size.Height) / 2, size.Width, size.Height));
            if (cut)
            {
                using var dim = new SolidBrush(Color.FromArgb(140, EditorTheme.Surface));
                e.Graphics.FillRectangle(dim, imageBounds);
            }
        }
        else
        {
            using var iconFont = new Font(UiFonts.Icons, Scale(50), GraphicsUnit.Pixel);
            TextRenderer.DrawText(e.Graphics, entry.Directory ? "\uE8B7" : entry.Video ? "\uE714" : "\uE8A5", iconFont,
                imageBounds, entry.Directory ? EditorTheme.Folder : EditorTheme.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (entry.Video) RequestThumbnail(entry);
        }
        var caption = new Rectangle(bounds.X + Scale(3), imageBounds.Bottom + Scale(3), bounds.Width - Scale(6),
            Math.Max(1, bounds.Bottom - imageBounds.Bottom - Scale(3)));
        TextRenderer.DrawText(e.Graphics, entry.Name, Font, caption, playing ? EditorTheme.Accent : cut ? EditorTheme.Muted : EditorTheme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (playing)
        {
            using var accent = new Pen(EditorTheme.Accent, Math.Max(1, Scale(2)));
            e.Graphics.DrawRectangle(accent, bounds);
        }
        if (selected && entries.Focused) e.DrawFocusRectangle();
    }

    private void DrawDetails(object? sender, DrawListViewSubItemEventArgs e)
    {
        var entry = visible[e.ItemIndex];
        var playing = string.Equals(entry.Path, playingPath, StringComparison.OrdinalIgnoreCase);
        using var fill = new SolidBrush(e.Item?.Selected == true ? EditorTheme.Border : playing ? EditorTheme.Playing : EditorTheme.Surface);
        e.Graphics.FillRectangle(fill, e.Bounds);
        var bounds = e.Bounds;
        if (e.ColumnIndex == 0)
        {
            using var iconFont = new Font(UiFonts.Icons, Scale(16), GraphicsUnit.Pixel);
            TextRenderer.DrawText(e.Graphics, entry.Directory ? "\uE8B7" : entry.Video ? "\uE714" : "\uE8A5",
                iconFont, new Rectangle(bounds.X + Scale(4), bounds.Y, Scale(24), bounds.Height),
                entry.Directory ? EditorTheme.Folder : EditorTheme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            bounds.X += Scale(34);
            bounds.Width -= Scale(34);
        }
        DrawText(e.Graphics, e.SubItem?.Text ?? "", bounds, playing ? EditorTheme.Accent : cutPaths.Contains(entry.Path) ? EditorTheme.Muted : EditorTheme.Text);
        if (playing && e.ColumnIndex == 0)
        {
            using var accent = new SolidBrush(EditorTheme.Accent);
            e.Graphics.FillRectangle(accent, e.Bounds.X, e.Bounds.Y, Scale(3), e.Bounds.Height);
        }
    }

    private void DrawText(Graphics g, string text, Rectangle bounds, Color color) =>
        TextRenderer.DrawText(g, text, Font, Rectangle.Inflate(bounds, -Scale(5), 0), color,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

    private void RequestThumbnail(BrowserEntry entry)
    {
        if (!requested.Add(entry.Path)) return;
        thumbnailQueue.Enqueue(entry);
        if (!thumbnailBusy) _ = LoadThumbnailsAsync();
    }

    private async Task LoadThumbnailsAsync()
    {
        thumbnailBusy = true;
        try
        {
            while (!IsDisposed && thumbnailQueue.TryDequeue(out var entry))
            {
                var ticket = request;
                var image = await Task.Run(() => ShellImages.CachedThumbnail(entry.Path));
                if (IsDisposed || ticket != request) { image?.Dispose(); continue; }
                if (thumbnails.Count >= 192)
                {
                    var first = thumbnails.First();
                    first.Value?.Dispose();
                    thumbnails.Remove(first.Key);
                }
                thumbnails[entry.Path] = image;
                entries.Invalidate();
            }
        }
        finally { thumbnailBusy = false; }
    }

    private void ClearThumbnails()
    {
        thumbnailQueue.Clear();
        requested.Clear();
        foreach (var image in thumbnails.Values) image?.Dispose();
        thumbnails.Clear();
    }

    private FluentButton Icon(string glyph, string label, Action action)
    {
        var button = new FluentButton { Glyph = glyph, AutoSize = true, Padding = new Padding(7, 7, 7, 7),
            AccessibleName = label, Margin = new Padding(0, 3, 4, 3) };
        button.Click += (_, _) => action();
        return button;
    }

    private static Panel InputContainer(TextBox input)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = EditorTheme.Raised, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 9, 10, 9), Margin = new Padding(2, 3, 2, 5) };
        input.Dock = DockStyle.Top;
        panel.Controls.Add(input);
        return panel;
    }

    private static string SizeText(long bytes) => bytes >= 1_048_576
        ? (bytes / 1_048_576d).ToString("N1", CultureInfo.CurrentCulture) + " MB"
        : Math.Ceiling(bytes / 1024d).ToString(CultureInfo.CurrentCulture) + " KB";

    private void FitColumns()
    {
        if (fittingColumns || entries.View != View.Details || !entries.IsHandleCreated) return;
        fittingColumns = true;
        try
        {
            var available = entries.ClientSize.Width;
            var date = TextRenderer.MeasureText("2026/11/06 16:05", Font).Width + Scale(18);
            var size = TextRenderer.MeasureText("1,603.8 MB", Font).Width + Scale(18);
            if (available <= 0) return;
            var name = Math.Max(available / 3, available - date - size);
            date = Math.Max(0, Math.Min(date, (available - name) * 3 / 5));
            var remaining = available - name - date;
            if (entries.Columns[0].Width != name) entries.Columns[0].Width = name;
            if (entries.Columns[1].Width != date) entries.Columns[1].Width = date;
            if (entries.Columns[2].Width != remaining) entries.Columns[2].Width = remaining;
        }
        finally { fittingColumns = false; }
    }

    private void UpdateMetrics()
    {
        places.ItemHeight = Math.Max(Font.Height + Scale(14), Scale(34));
        places.Indent = Scale(14);
        tileSize.ImageSize = new Size(Scale(132), Scale(84));
        FitColumns();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        UpdateMetrics();
    }

    private int Scale(int value) => (int)Math.Round(value * DeviceDpi / 96d);

    private sealed class BufferedListView : ListView
    {
        public BufferedListView() => DoubleBuffered = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            request++;
            watcher?.Dispose();
            refreshTimer.Dispose();
            commandMenu.Dispose();
            ClearThumbnails();
            tileSize.Dispose();
        }
        base.Dispose(disposing);
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string theme, string? subIdList);
}
