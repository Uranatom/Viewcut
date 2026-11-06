using System.Globalization;
using System.Runtime.InteropServices;

namespace Viewcut;

internal sealed partial class Player : IDisposable
{
    private const int Flag = 3;
    private const int Double = 5;
    private const ulong LoadRequest = 10;
    private const ulong SeekRequest = 11;
    private IntPtr handle;
    private bool loading;
    private bool started;
    private string? nextPath;

    public bool Ready { get; private set; }
    public bool Paused { get; private set; }
    public bool Seeking { get; private set; }
    public double Position { get; private set; }
    public double Duration { get; private set; }
    public string? Error { get; private set; }

    public Player(IntPtr host)
    {
        handle = Create();
        if (handle == IntPtr.Zero) throw new InvalidOperationException("无法创建播放引擎。");
        try
        {
            Set("wid", unchecked((uint)host.ToInt64()).ToString(CultureInfo.InvariantCulture));
            Set("config", "no");
            Set("load-scripts", "no");
            Set("osc", "no");
            Set("input-default-bindings", "no");
            Set("input-vo-keyboard", "no");
            Set("input-cursor", "no");
            Set("vo", "gpu");
            Set("gpu-context", "d3d11");
            Set("hwdec", "auto-safe");
            Set("keep-open", "yes");
            Check(Initialize(handle));
            Check(Observe(handle, 1, "time-pos", Double));
            Check(Observe(handle, 2, "duration", Double));
            Check(Observe(handle, 3, "pause", Flag));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Open(string path)
    {
        Ready = false;
        Seeking = false;
        Error = null;
        Position = Duration = 0;
        if (loading)
        {
            nextPath = path;
            return;
        }
        loading = true;
        started = false;
        Command(0, "set", "pause", "no");
        Command(LoadRequest, "loadfile", path, "replace");
    }

    public void TogglePause()
    {
        if (Ready) Command(0, "cycle", "pause");
    }

    public void Seek(double seconds, bool exact)
    {
        if (!Ready) return;
        Seeking = true;
        Command(SeekRequest, "seek",
            Math.Clamp(seconds, 0, Duration).ToString("F6", CultureInfo.InvariantCulture),
            exact ? "absolute+exact" : "absolute+keyframes");
    }

    public void Poll()
    {
        while (true)
        {
            var e = Marshal.PtrToStructure<NativeEvent>(WaitEvent(handle, 0));
            switch (e.Id)
            {
                case 0:
                    return;
                case 5 when e.Error < 0 && e.Reply == LoadRequest:
                    FinishLoad(ErrorText(e.Error));
                    break;
                case 5 when e.Error < 0 && e.Reply == SeekRequest:
                    Seeking = false;
                    break;
                case 6:
                    started = true;
                    break;
                case 7 when started && Marshal.ReadInt32(e.Data) == 4:
                    FinishLoad(ErrorText(Marshal.ReadInt32(e.Data, 4)));
                    break;
                case 8:
                    FinishLoad(null);
                    break;
                case 21:
                    Seeking = false;
                    break;
                case 22:
                    var property = Marshal.PtrToStructure<NativeProperty>(e.Data);
                    if (property.Format == Double && started)
                    {
                        var value = Marshal.PtrToStructure<double>(property.Data);
                        if (e.Reply == 1) Position = value;
                        if (e.Reply == 2) Duration = value;
                    }
                    if (property.Format == Flag && e.Reply == 3)
                        Paused = Marshal.ReadInt32(property.Data) != 0;
                    break;
            }
        }
    }

    private void FinishLoad(string? error)
    {
        loading = false;
        if (nextPath is { } next)
        {
            nextPath = null;
            Open(next);
            return;
        }
        Error = error;
        Ready = error is null;
    }

    private void Set(string name, string value) => Check(SetOption(handle, name, value));

    private void Command(ulong id, params string[] arguments)
    {
        var pointers = new IntPtr[arguments.Length + 1];
        try
        {
            for (var i = 0; i < arguments.Length; i++)
                pointers[i] = Marshal.StringToCoTaskMemUTF8(arguments[i]);
            Check(CommandAsync(handle, id, pointers));
        }
        finally
        {
            foreach (var pointer in pointers) Marshal.FreeCoTaskMem(pointer);
        }
    }

    private static void Check(int status)
    {
        if (status < 0) throw new InvalidOperationException($"播放引擎：{ErrorText(status)}");
    }

    private static string ErrorText(int code) =>
        Marshal.PtrToStringUTF8(ErrorString(code)) ?? "未知播放错误";

    public void Dispose()
    {
        if (handle == IntPtr.Zero) return;
        Destroy(handle);
        handle = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeEvent
    {
        public int Id;
        public int Error;
        public ulong Reply;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeProperty
    {
        public IntPtr Name;
        public int Format;
        public IntPtr Data;
    }

    private const string Library = "libmpv-2.dll";
    [LibraryImport(Library, EntryPoint = "mpv_create")]
    private static partial IntPtr Create();
    [LibraryImport(Library, EntryPoint = "mpv_initialize")]
    private static partial int Initialize(IntPtr handle);
    [LibraryImport(Library, EntryPoint = "mpv_terminate_destroy")]
    private static partial void Destroy(IntPtr handle);
    [LibraryImport(Library, EntryPoint = "mpv_set_option_string", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int SetOption(IntPtr handle, string name, string value);
    [LibraryImport(Library, EntryPoint = "mpv_observe_property", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Observe(IntPtr handle, ulong id, string name, int format);
    [LibraryImport(Library, EntryPoint = "mpv_command_async")]
    private static partial int CommandAsync(IntPtr handle, ulong id, [In] IntPtr[] arguments);
    [LibraryImport(Library, EntryPoint = "mpv_wait_event")]
    private static partial IntPtr WaitEvent(IntPtr handle, double seconds);
    [LibraryImport(Library, EntryPoint = "mpv_error_string")]
    private static partial IntPtr ErrorString(int error);
}
