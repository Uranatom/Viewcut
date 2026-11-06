namespace Viewcut;

internal sealed class RenameClipDialog : Form
{
    private readonly TextBox input = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle,
        BackColor = EditorTheme.Raised, ForeColor = EditorTheme.Text };
    public string? ClipName { get; private set; }

    public RenameClipDialog(string? current, Func<string?, string?> validate, string title = "重命名片段",
        string label = "片段名称", string hint = "名称将用于导出文件，扩展名自动保留。留空恢复默认名称。")
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI Variable Text", 9.5f);
        Text = L.T(title);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = EditorTheme.Background;
        ForeColor = EditorTheme.Text;
        ClientSize = new Size(450, 185);
        input.Text = current ?? "";
        input.AccessibleName = L.T(label);
        var save = new FluentButton { Text = L.T("确定"), Emphasized = true };
        var cancel = new FluentButton { Text = L.T("取消"), DialogResult = DialogResult.Cancel };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true, Margin = Padding.Empty };
        actions.Controls.AddRange([cancel, save]);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = L.T(label), AutoSize = true, Margin = new Padding(0, 0, 0, 8) }, 0, 0);
        layout.Controls.Add(input, 0, 1);
        layout.Controls.Add(new Label { Text = L.T(hint),
            Dock = DockStyle.Fill, AutoSize = true, ForeColor = EditorTheme.Muted, Margin = new Padding(0, 8, 0, 8) }, 0, 2);
        layout.Controls.Add(actions, 0, 3);
        Controls.Add(layout);
        AcceptButton = save;
        CancelButton = cancel;
        save.Click += (_, _) =>
        {
            var name = string.IsNullOrWhiteSpace(input.Text) ? null : input.Text.Trim();
            if (validate(name) is { } error)
            {
                MessageBox.Show(this, error, L.T("名称无法使用"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                input.Focus();
                input.SelectAll();
                return;
            }
            ClipName = name;
            DialogResult = DialogResult.OK;
        };
        Shown += (_, _) => { input.Focus(); input.SelectAll(); };
        EditorTheme.ApplyTree(this);
        HandleCreated += (_, _) => EditorTheme.ApplyWindow(this);
        ResumeLayout(performLayout: true);
    }
}
