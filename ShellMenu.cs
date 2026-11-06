using System.Runtime.InteropServices;
using System.Text;

namespace Viewcut;

// Hosts Explorer's real context menu, including installed Shell extensions and submenus.
internal sealed class ShellMenu : IDisposable
{
    private readonly IContextMenu context;
    private readonly IntPtr menu;
    private readonly uint commandCount;
    public string[] Verbs => Enumerable.Range(0, (int)commandCount).Select(index => CanonicalVerb((uint)index)).ToArray();

    public ShellMenu(string[] paths, IntPtr owner)
    {
        var pidls = new List<IntPtr>();
        IShellItemArray? array = null;
        try
        {
            foreach (var path in paths)
            {
                Marshal.ThrowExceptionForHR(SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _));
                pidls.Add(pidl);
            }
            Marshal.ThrowExceptionForHR(SHCreateShellItemArrayFromIDLists((uint)pidls.Count, pidls.ToArray(), out array));
            var id = typeof(IContextMenu).GUID;
            var handler = new Guid("3981e225-f559-11d3-8e3a-00c04f6837d5");
            array.BindToHandler(IntPtr.Zero, ref handler, ref id, out var result);
            context = (IContextMenu)result;
            menu = CreatePopupMenu();
            var flags = 0x14u | ((Control.ModifierKeys & Keys.Shift) != 0 ? 0x100u : 0);
            var count = context.QueryContextMenu(menu, 0, 1, 0x6fff, flags);
            Marshal.ThrowExceptionForHR(count);
            commandCount = (uint)(count & 0xffff);
        }
        catch
        {
            if (menu != IntPtr.Zero) DestroyMenu(menu);
            if (context is not null) Marshal.ReleaseComObject(context);
            throw;
        }
        finally
        {
            foreach (var pidl in pidls) Marshal.FreeCoTaskMem(pidl);
            if (array is not null) Marshal.ReleaseComObject(array);
        }
    }

    public void Show(Control owner, Point screen, Action openInViewcut, Action<string> command, bool reviewable)
    {
        if (reviewable)
        {
            InsertMenu(menu, 0, 0x400, 0x7000, L.T("在 Viewcut 中打开"));
            InsertMenu(menu, 1, 0xC00, 0, null);
        }
        using var hook = new MenuHook(owner.FindForm()!.Handle, context);
        var selected = TrackPopupMenuEx(menu, 0x100 | 0x2, screen.X, screen.Y, hook.Handle, IntPtr.Zero);
        if (selected == 0x7000) openInViewcut();
        else if (selected > 0)
        {
            var verb = CanonicalVerb(selected - 1);
            if (verb is "cut" or "copy" or "delete" or "rename") command(verb);
            else Invoke(owner.FindForm()!.Handle, (IntPtr)(selected - 1));
        }
    }

    public string CanonicalVerb(uint offset)
    {
        var buffer = new StringBuilder(512);
        return context.GetCommandString((UIntPtr)offset, 4, IntPtr.Zero, buffer, buffer.Capacity) >= 0
            ? buffer.ToString().ToLowerInvariant() : "";
    }

    public void InvokeVerb(IntPtr owner, string verb)
    {
        var pointer = Marshal.StringToCoTaskMemAnsi(verb);
        try { Invoke(owner, pointer); }
        finally { Marshal.FreeCoTaskMem(pointer); }
    }

    private void Invoke(IntPtr owner, IntPtr verb)
    {
        var invocation = new Invocation { Size = Marshal.SizeOf<Invocation>(), Window = owner, Verb = verb, Show = 1 };
        Marshal.ThrowExceptionForHR(context.InvokeCommand(ref invocation));
    }

    public void Dispose()
    {
        DestroyMenu(menu);
        Marshal.ReleaseComObject(context);
    }

    private sealed class MenuHook : NativeWindow, IDisposable
    {
        private readonly IContextMenu3? third;
        private readonly IContextMenu2? second;
        public MenuHook(IntPtr owner, IContextMenu menu)
        {
            third = menu as IContextMenu3;
            second = menu as IContextMenu2;
            AssignHandle(owner);
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg is 0x117 or 0x2b or 0x2c or 0x120)
            {
                if (third is not null && third.HandleMenuMsg2((uint)message.Msg, message.WParam, message.LParam, out var result) == 0)
                { message.Result = result; return; }
                if (second is not null && second.HandleMenuMsg((uint)message.Msg, message.WParam, message.LParam) == 0) return;
            }
            base.WndProc(ref message);
        }
        public void Dispose() => ReleaseHandle();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Invocation
    {
        public int Size;
        public uint Mask;
        public IntPtr Window, Verb, Parameters, Directory;
        public int Show;
        public uint HotKey;
        public IntPtr Icon;
    }

    [ComImport, Guid("b63ea76d-1f85-456f-a19c-48159efa858b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray
    {
        void BindToHandler(IntPtr context, ref Guid handler, ref Guid id, [MarshalAs(UnmanagedType.Interface)] out object result);
    }

    [ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref Invocation invocation);
        [PreserveSig] int GetCommandString(UIntPtr command, uint type, IntPtr reserved,
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count);
    }

    [ComImport, Guid("000214f4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref Invocation invocation);
        [PreserveSig] int GetCommandString(UIntPtr command, uint type, IntPtr reserved, IntPtr text, int count);
        [PreserveSig] int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
    }

    [ComImport, Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref Invocation invocation);
        [PreserveSig] int GetCommandString(UIntPtr command, uint type, IntPtr reserved, IntPtr text, int count);
        [PreserveSig] int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int HandleMenuMsg2(uint message, IntPtr wParam, IntPtr lParam, out IntPtr result);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr context, out IntPtr pidl, uint mask, out uint attributes);
    [DllImport("shell32.dll")]
    private static extern int SHCreateShellItemArrayFromIDLists(uint count, [In] IntPtr[] pidls, out IShellItemArray array);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool InsertMenu(IntPtr menu, uint position, uint flags, uint id, string? text);
    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr options);
}
