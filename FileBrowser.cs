using System.Globalization;
using System.Runtime.InteropServices;

namespace Viewcut;

internal sealed record BrowserEntry(string Path, string Name, bool Directory, long Size, DateTime Modified)
{
    public bool Video => !Directory && System.IO.Path.GetExtension(Path).ToLowerInvariant() is ".mp4" or ".mov";
}

internal sealed class FileBrowser : UserControl
{
    private readonly TextBox address = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None };
    private readonly TextBox search = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, PlaceholderText = "搜索当前文件夹" };
    private readonly ListView entries = new() { Dock = DockStyle.Fill, VirtualMode = true, OwnerDraw = true,
        BorderStyle = BorderStyle.None, MultiSelect = false, HideSelection = false, ShowItemToolTips = true };
    private readonly TreeView places = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
        ShowLines = false, ShowRootLines = false, FullRowSelect = true, HideSelection = false };
    private readonly Label info = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly FluentButton back;
    private readonly FluentButton forward;
    private readonly FluentButton large;
    private readonly FluentButton details;
    private readonly ToolTip tips = new();
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
    private string? selectedPath;

    public string? CurrentFolder { get; private set; }
    public bool LargeIcons => entries.View == View.LargeIcon;
    public bool EditingPath => address.Focused || search.Focused;
    public string[] VideoFiles => all.Where(entry => entry.Video).Select(entry => entry.Path).ToArray();
    public event Action<string>? VideoActivated;
    public event Action? PreferencesChanged;

    public FileBrowser()
    {
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        BackColor = EditorTheme.Surface;
        ForeColor = EditorTheme.Text;
        Font = new Font("Segoe UI Variable Text", 9);
        address.BackColor = search.BackColor = EditorTheme.Raised;
        address.ForeColor = search.ForeColor = EditorTheme.Text;
        address.AccessibleName = "文件夹地址";
        search.AccessibleName = "搜索当前文件夹";
        entries.AccessibleName = "文件浏览器";
        entries.BackColor = places.BackColor = EditorTheme.Surface;
        entries.ForeColor = places.ForeColor = EditorTheme.Text;
        entries.Columns.Add("名称", 210);
        entries.Columns.Add("修改日期", 125);
        entries.Columns.Add("大小", 80);
        entries.LargeImageList = tileSize;
        entries.TileSize = new Size(148, 130);
        entries.View = View.LargeIcon;
        entries.FullRowSelect = true;
        places.ItemHeight = 32;
        places.Indent = 14;
        places.DrawMode = TreeViewDrawMode.OwnerDrawText;

        back = Icon("\uE72B", "后退", () => BrowseHistory(-1));
        forward = Icon("\uE72A", "前进", () => BrowseHistory(1));
        var up = Icon("\uE74A", "向上一级", Up);
        var refresh = Icon("\uE72C", "刷新", () => _ = NavigateAsync(CurrentFolder));
        var navigation = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        navigation.Controls.AddRange([back, forward, up, refresh]);
        var pathBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        pathBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
        pathBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        pathBar.Controls.Add(navigation, 0, 0);
        pathBar.Controls.Add(InputContainer(address), 1, 0);

        large = Icon("\uE80A", "大图标", () => SetView(true));
        details = Icon("\uE8FD", "列表", () => SetView(false));
        var viewBar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        viewBar.Controls.AddRange([
            new Label { Text = "查看", AutoSize = true, ForeColor = EditorTheme.Muted, Margin = new Padding(3, 10, 8, 0) },
            large, details
        ]);
        var commandBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        commandBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        commandBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        commandBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        commandBar.Controls.Add(viewBar, 0, 0);
        commandBar.Controls.Add(InputContainer(search), 1, 0);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.Controls.Add(places, 0, 0);
        body.Controls.Add(entries, 1, 0);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(6) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 41));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 41));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.Controls.Add(pathBar, 0, 0);
        layout.Controls.Add(commandBar, 0, 1);
        layout.Controls.Add(body, 0, 2);
        layout.Controls.Add(info, 0, 3);
        Controls.Add(layout);
        info.ForeColor = EditorTheme.Muted;

        AddPlace("主文件夹", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        AddPlace("桌面", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        AddPlace("视频", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
        var computer = places.Nodes.Add("此电脑");
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
            if (entries.SelectedIndices.Count != 1) return;
            selectedPath = visible[entries.SelectedIndices[0]].Path;
            info.Text = $"{visible.Length} 个项目　|　{visible[entries.SelectedIndices[0]].Name}";
        };
        entries.DrawItem += DrawEntry;
        entries.DrawSubItem += DrawDetails;
        entries.DrawColumnHeader += (_, e) =>
        {
            using var fill = new SolidBrush(EditorTheme.Background);
            e.Graphics.FillRectangle(fill, e.Bounds);
            DrawText(e.Graphics, e.Header?.Text ?? "", e.Bounds, EditorTheme.Muted);
        };
        entries.HandleCreated += (_, _) => SetWindowTheme(entries.Handle, "DarkMode_Explorer", null);
        places.HandleCreated += (_, _) => SetWindowTheme(places.Handle, "DarkMode_Explorer", null);
        entries.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Back) { Up(); e.Handled = true; }
        };
        address.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            _ = NavigateAsync(Environment.ExpandEnvironmentVariables(address.Text.Trim().Trim('"')));
        };
        search.TextChanged += (_, _) => Filter();
        SetView(true, notify: false);
    }

    public async Task NavigateAsync(string? path, string? select = null, bool addHistory = true)
    {
        if (string.IsNullOrWhiteSpace(path) || IsDisposed) return;
        var ticket = ++request;
        info.Text = "正在读取文件夹…";
        try
        {
            path = Path.GetFullPath(path);
            var target = path;
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
            selectedPath = select;
            ClearThumbnails();
            if (search.TextLength > 0) search.Clear();
            else Filter();
            back.Enabled = historyIndex > 0;
            forward.Enabled = historyIndex < history.Count - 1;
            PreferencesChanged?.Invoke();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (ticket != request || IsDisposed) return;
            address.Text = CurrentFolder ?? path;
            info.Text = $"无法打开文件夹　{ex.Message}";
        }
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
        entries.VirtualListSize = 0;
        visible = all.Where(entry => entry.Name.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        entries.VirtualListSize = visible.Length;
        SelectPath(selectedPath);
        info.Text = $"{visible.Length} 个项目";
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

    public void SetView(bool largeIcons, bool notify = true)
    {
        entries.View = largeIcons ? View.LargeIcon : View.Details;
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
        else info.Text = "请选择 MP4 或 MOV 视频。";
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
        var rect = new Rectangle(e.Bounds.X, e.Bounds.Y, places.Width - e.Bounds.X, e.Bounds.Height);
        using var fill = new SolidBrush(e.Node.IsSelected ? EditorTheme.Border : EditorTheme.Surface);
        e.Graphics.FillRectangle(fill, rect);
        using var iconFont = new Font("Segoe Fluent Icons", 11);
        TextRenderer.DrawText(e.Graphics, e.Node.Parent is not null ? "\uEDA2" : "\uE8B7", iconFont,
            new Rectangle(rect.Left + 2, rect.Top, 23, rect.Height), e.Node.Parent is not null ? EditorTheme.Muted : Color.FromArgb(255, 209, 93),
            TextFormatFlags.VerticalCenter);
        DrawText(e.Graphics, e.Node.Text, new Rectangle(rect.Left + 25, rect.Top, rect.Width - 25, rect.Height), EditorTheme.Text);
    }

    private void DrawEntry(object? sender, DrawListViewItemEventArgs e)
    {
        if (!LargeIcons) return;
        var entry = visible[e.ItemIndex];
        var selected = e.Item?.Selected == true;
        using var background = new SolidBrush(selected ? EditorTheme.Border : EditorTheme.Surface);
        var bounds = Rectangle.Inflate(e.Bounds, -3, -2);
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
        }
        else
        {
            using var iconFont = new Font("Segoe Fluent Icons", 38);
            TextRenderer.DrawText(e.Graphics, entry.Directory ? "\uE8B7" : entry.Video ? "\uE714" : "\uE8A5", iconFont,
                imageBounds, entry.Directory ? Color.FromArgb(255, 209, 93) : EditorTheme.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (entry.Video) RequestThumbnail(entry);
        }
        var caption = new Rectangle(bounds.X + 3, imageBounds.Bottom + 3, bounds.Width - 6,
            Math.Max(1, bounds.Bottom - imageBounds.Bottom - 3));
        TextRenderer.DrawText(e.Graphics, entry.Name, Font, caption, EditorTheme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (selected && entries.Focused) e.DrawFocusRectangle();
    }

    private void DrawDetails(object? sender, DrawListViewSubItemEventArgs e)
    {
        using var fill = new SolidBrush(e.Item?.Selected == true ? EditorTheme.Border : EditorTheme.Surface);
        e.Graphics.FillRectangle(fill, e.Bounds);
        var bounds = e.Bounds;
        if (e.ColumnIndex == 0)
        {
            var entry = visible[e.ItemIndex];
            using var iconFont = new Font("Segoe Fluent Icons", 11);
            TextRenderer.DrawText(e.Graphics, entry.Directory ? "\uE8B7" : entry.Video ? "\uE714" : "\uE8A5",
                iconFont, new Rectangle(bounds.X + 4, bounds.Y, 24, bounds.Height),
                entry.Directory ? Color.FromArgb(255, 209, 93) : EditorTheme.Muted, TextFormatFlags.VerticalCenter);
            bounds.X += 28;
            bounds.Width -= 28;
        }
        DrawText(e.Graphics, e.SubItem?.Text ?? "", bounds, EditorTheme.Text);
    }

    private void DrawText(Graphics g, string text, Rectangle bounds, Color color) =>
        TextRenderer.DrawText(g, text, Font, Rectangle.Inflate(bounds, -4, 0), color,
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
        var button = new FluentButton { Glyph = glyph, AutoSize = false, Width = 30, Height = 30,
            AccessibleName = label, Margin = new Padding(0, 3, 4, 3) };
        tips.SetToolTip(button, label);
        button.Click += (_, _) => action();
        return button;
    }

    private static Panel InputContainer(TextBox input)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = EditorTheme.Raised,
            Padding = new Padding(8, 8, 6, 4), Margin = new Padding(2, 3, 2, 5) };
        panel.Controls.Add(input);
        return panel;
    }

    private static string SizeText(long bytes) => bytes >= 1_048_576
        ? (bytes / 1_048_576d).ToString("N1", CultureInfo.CurrentCulture) + " MB"
        : Math.Ceiling(bytes / 1024d).ToString(CultureInfo.CurrentCulture) + " KB";

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            request++;
            ClearThumbnails();
            tips.Dispose();
            tileSize.Dispose();
        }
        base.Dispose(disposing);
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string theme, string? subIdList);
}
