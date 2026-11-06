namespace Viewcut;

internal static class EditorTheme
{
    public static readonly Color Background = Color.FromArgb(32, 32, 32);
    public static readonly Color Surface = Color.FromArgb(25, 25, 25);
    public static readonly Color Raised = Color.FromArgb(45, 45, 45);
    public static readonly Color Border = Color.FromArgb(64, 64, 64);
    public static readonly Color Text = Color.FromArgb(242, 242, 242);
    public static readonly Color Muted = Color.FromArgb(171, 171, 171);
    public static readonly Color Accent = Color.FromArgb(153, 202, 255);
    public static readonly Color Marker = Color.FromArgb(251, 192, 111);

    public static ContextMenuStrip ContextMenu() => new()
    {
        BackColor = Raised,
        ForeColor = Text,
        Renderer = new ToolStripProfessionalRenderer(new MenuColors()),
        Font = new Font("Segoe UI Variable Text", 9.5f),
        ShowImageMargin = false
    };

    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Raised;
        public override Color MenuItemSelected => Border;
        public override Color MenuItemBorder => Border;
        public override Color MenuBorder => Border;
    }
}
