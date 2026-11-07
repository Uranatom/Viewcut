namespace Viewcut;

internal sealed class PreferencesDialog : Form
{
    private readonly Dictionary<string, RadioButton> languages = [];
    private readonly Dictionary<string, RadioButton> themes = [];
    private bool updating;

    public PreferencesDialog(Func<string, bool> setLanguage, Func<string, bool> setTheme)
    {
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font(UiFonts.Text, 9.5f);
        Text = L.T("偏好设置");
        AccessibleName = "Viewcut Preferences";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = EditorTheme.Background;
        ForeColor = EditorTheme.Text;
        ClientSize = new Size(480, 320);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true,
            ColumnCount = 1, RowCount = 6, Padding = new Padding(24) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 5; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var languageRow = Options(languages, [("zh-CN", "简体中文"), ("en-US", "English")], setLanguage);
        var themeRow = Options(themes, [("dark", L.T("深色")), ("light", L.T("浅色")), ("system", L.T("跟随系统"))], setTheme);
        layout.Controls.Add(Caption(L.T("语言")), 0, 0);
        layout.Controls.Add(languageRow, 0, 1);
        layout.Controls.Add(Caption(L.T("界面主题")), 0, 2);
        layout.Controls.Add(themeRow, 0, 3);
        layout.Controls.Add(new Label { Text = L.T("立即应用并自动保存。"), AutoSize = true,
            ForeColor = EditorTheme.Muted, Margin = new Padding(0, 6, 0, 16) }, 0, 4);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var close = new FluentButton { Text = L.T("关闭"), DialogResult = DialogResult.OK, Emphasized = true };
        footer.Controls.Add(close);
        layout.Controls.Add(footer, 0, 5);
        Controls.Add(layout);
        AcceptButton = CancelButton = close;
        L.Changed += ApplyPreferences;
        EditorTheme.Changed += ApplyPreferences;
        HandleCreated += (_, _) => EditorTheme.ApplyWindow(this);
        ApplyPreferences();

        FlowLayoutPanel Options(Dictionary<string, RadioButton> controls, (string Code, string Label)[] choices, Func<string, bool> change)
        {
            var row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true,
                Margin = new Padding(0, 4, 0, 18) };
            foreach (var (code, label) in choices)
            {
                var radio = new RadioButton { Text = label, AutoSize = true, Margin = new Padding(0, 5, 22, 5),
                    AccessibleName = code, UseVisualStyleBackColor = false };
                radio.CheckedChanged += (_, _) =>
                {
                    if (updating || !radio.Checked) return;
                    change(code);
                    ApplyPreferences();
                };
                controls.Add(code, radio);
                row.Controls.Add(radio);
            }
            return row;
        }
    }

    private static Label Caption(string text) => new() { Text = text, AutoSize = true,
        Margin = Padding.Empty, ForeColor = EditorTheme.Text };

    private void ApplyPreferences()
    {
        updating = true;
        SuspendLayout();
        L.ApplyTree(this);
        EditorTheme.ApplyTree(this);
        foreach (var (code, radio) in languages) radio.Checked = code == L.Language;
        foreach (var (code, radio) in themes) radio.Checked = code == EditorTheme.Mode;
        ResumeLayout(true);
        updating = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { L.Changed -= ApplyPreferences; EditorTheme.Changed -= ApplyPreferences; }
        base.Dispose(disposing);
    }
}
