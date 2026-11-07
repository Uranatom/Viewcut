using Microsoft.Win32;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Viewcut;

internal static class EditorTheme
{
    private static readonly Color[] Dark = [Color.FromArgb(32, 32, 32), Color.FromArgb(25, 25, 25),
        Color.FromArgb(45, 45, 45), Color.FromArgb(64, 64, 64), Color.FromArgb(242, 242, 242),
        Color.FromArgb(171, 171, 171), Color.FromArgb(153, 202, 255), Color.FromArgb(251, 192, 111)];
    private static readonly Color[] Light = [Color.FromArgb(243, 243, 243), Color.White,
        Color.FromArgb(250, 250, 250), Color.FromArgb(209, 209, 209), Color.FromArgb(26, 26, 26),
        Color.FromArgb(93, 93, 93), Color.FromArgb(0, 95, 184), Color.FromArgb(156, 87, 0)];
    private static readonly Color[] DarkClips = [Dark[6], Color.FromArgb(148, 188, 246),
        Color.FromArgb(221, 169, 240), Color.FromArgb(241, 190, 128)];
    private static readonly Color[] LightClips = [Light[6], Color.FromArgb(89, 63, 155),
        Color.FromArgb(145, 43, 115), Color.FromArgb(131, 81, 0)];
    private static readonly ConditionalWeakTable<Control, ColorRoles> Roles = new();
    public static string Mode { get; private set; } = "dark";
    public static bool IsLight { get; private set; }
    public static event Action? Changed;
    private static Color[] Palette => IsLight ? Light : Dark;
    public static Color Background => Palette[0];
    public static Color Surface => Palette[1];
    public static Color Raised => Palette[2];
    public static Color Border => Palette[3];
    public static Color Text => Palette[4];
    public static Color Muted => Palette[5];
    public static Color Accent => Palette[6];
    public static Color Marker => Palette[7];
    public static Color Hover => IsLight ? Color.FromArgb(229, 229, 229) : Color.FromArgb(57, 57, 57);
    public static Color Playing => IsLight ? Color.FromArgb(229, 241, 251) : Color.FromArgb(35, 51, 65);
    public static Color Folder => IsLight ? Color.FromArgb(152, 107, 0) : Color.FromArgb(255, 209, 93);
    public static Color OnAccent => IsLight ? Color.White : Dark[1];
    public static Color VideoText => Dark[4];
    public static Color VideoMuted => Dark[5];
    public static Color[] ClipColors => IsLight ? LightClips : DarkClips;

    public static void SetMode(string? mode)
    {
        var normalized = mode is "light" or "system" ? mode : "dark";
        var light = normalized == "light" || normalized == "system" && SystemIsLight();
        var changed = Mode != normalized || IsLight != light;
        Mode = normalized;
        IsLight = light;
        if (changed) Changed?.Invoke();
    }

    private static bool SystemIsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return true; }
    }

    public static void ApplyTree(Control root)
    {
        var roles = Roles.GetValue(root, control => new ColorRoles(Role(control.BackColor), Role(control.ForeColor)));
        if (roles.Back >= 0) root.BackColor = Palette[roles.Back];
        if (roles.Fore >= 0) root.ForeColor = Palette[roles.Fore];
        if (root is ListView or TreeView && root.IsHandleCreated)
            SetWindowTheme(root.Handle, IsLight ? "Explorer" : "DarkMode_Explorer", null);
        if (root is Form form && form.IsHandleCreated) ApplyWindow(form);
        if (root.ContextMenuStrip is { } menu) ApplyMenu(menu);
        foreach (Control child in root.Controls) ApplyTree(child);
        root.Invalidate();
    }

    public static void ApplyWindow(Form form)
    {
        var dark = IsLight ? 0 : 1;
        // 20 is supported by current Windows 10/11; older DWM revisions used 19.
        if (DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int)) != 0)
            DwmSetWindowAttribute(form.Handle, 19, ref dark, sizeof(int));
    }

    private static int Role(Color color)
    {
        for (var index = 0; index < Dark.Length; index++)
            if (color.ToArgb() == Dark[index].ToArgb() || color.ToArgb() == Light[index].ToArgb()) return index;
        return -1;
    }
    private sealed record ColorRoles(int Back, int Fore);

    public static ContextMenuStrip ContextMenu()
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Raised, ForeColor = Text,
            Renderer = new ToolStripProfessionalRenderer(new MenuColors()),
            Font = new Font(UiFonts.Text, 9.5f), ShowImageMargin = false
        };
        void ThemeChanged() => ApplyMenu(menu);
        void LanguageChanged() => L.ApplyMenu(menu.Items);
        Changed += ThemeChanged;
        L.Changed += LanguageChanged;
        menu.Opening += (_, _) => { ThemeChanged(); LanguageChanged(); };
        menu.Disposed += (_, _) => { Changed -= ThemeChanged; L.Changed -= LanguageChanged; };
        return menu;
    }

    private static void ApplyMenu(ContextMenuStrip menu)
    {
        menu.BackColor = Raised;
        menu.ForeColor = Text;
        void StyleItems(ToolStripItemCollection items)
        {
            foreach (ToolStripItem item in items)
            {
                item.ForeColor = Text;
                if (item is not ToolStripDropDownItem dropdown || !dropdown.HasDropDownItems) continue;
                dropdown.DropDown.BackColor = Raised;
                dropdown.DropDown.ForeColor = Text;
                dropdown.DropDown.Renderer = menu.Renderer;
                StyleItems(dropdown.DropDownItems);
            }
        }
        StyleItems(menu.Items);
        menu.Invalidate(true);
    }

    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Raised;
        public override Color MenuItemSelected => Border;
        public override Color MenuItemBorder => Border;
        public override Color MenuBorder => Border;
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string theme, string? subIdList);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
