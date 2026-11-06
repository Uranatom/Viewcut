using System.Runtime.InteropServices;

namespace Viewcut;

internal static class ShellImages
{
    // Cache-only retrieval avoids decoding the whole SD card while browsing.
    public static Bitmap? CachedThumbnail(string path)
    {
        var id = typeof(IShellItemImageFactory).GUID;
        if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref id, out var factory) < 0) return null;
        IntPtr bitmap = IntPtr.Zero;
        try
        {
            const uint SIIGBF_THUMBNAILONLY = 0x08;
            const uint SIIGBF_INCACHEONLY = 0x10;
            if (factory.GetImage(new NativeSize(160, 100), SIIGBF_THUMBNAILONLY | SIIGBF_INCACHEONLY, out bitmap) < 0)
                return null;
            return Image.FromHbitmap(bitmap);
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException) { return null; }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            Marshal.ReleaseComObject(factory);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeSize(int Width, int Height);

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(NativeSize size, uint flags, out IntPtr bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr context,
        ref Guid id, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);
}
