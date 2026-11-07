using System.Runtime.InteropServices;

namespace Viewcut;

// Paint resized editor panels from their new bounds instead of reusing old pixels.
internal sealed class BufferedLayout : TableLayoutPanel
{
    public BufferedLayout()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    protected override unsafe void WndProc(ref Message message)
    {
        if (message.Msg == 0x0046 && message.LParam != IntPtr.Zero) // WM_WINDOWPOSCHANGING
        {
            // Copying the old client pixels can leave fragments of the previous layout.
            var position = (WindowPosition*)message.LParam;
            if ((position->Flags & 0x0003) != 0x0003) // SWP_NOMOVE | SWP_NOSIZE
                position->Flags |= 0x0100; // SWP_NOCOPYBITS
        }
        base.WndProc(ref message);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPosition
    {
        public IntPtr Window, InsertAfter;
        public int X, Y, Width, Height;
        public uint Flags;
    }
}
