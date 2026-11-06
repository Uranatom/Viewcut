using System.Runtime.InteropServices;

namespace Viewcut;

internal enum FileAction { Copy, Move, Recycle, Delete, Rename, NewFolder }

// Use the same Shell operations as Explorer, including collision, progress and elevation dialogs.
internal static class ShellFiles
{
    public static Task<bool> ExecuteAsync(IntPtr owner, FileAction action, string[] paths,
        string? destination = null, string? name = null, bool quiet = false)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            IFileOperation? operation = null;
            var items = new List<IShellItem>();
            try
            {
                operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(
                    new Guid("3ad05575-8857-4850-9277-11b85bdb8e09"))!)!;
                operation.SetOwnerWindow(owner);
                uint flags = 0x40 | 0x200 | 0x4000 | 0x40000; // Undo, mkdir, warn before unrecyclable delete, undo record.
                if (action == FileAction.Recycle) flags |= 0x80000;
                if (action == FileAction.Delete) flags &= ~(0x40u | 0x40000u);
                if (quiet) flags |= 0x4 | 0x10 | 0x400;
                operation.SetOperationFlags(flags);
                IShellItem Item(string path)
                {
                    var id = typeof(IShellItem).GUID;
                    Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, IntPtr.Zero, ref id, out var item));
                    items.Add(item);
                    return item;
                }
                var target = destination is null ? null : Item(destination);
                if (action == FileAction.NewFolder)
                    operation.NewItem(target!, 0x10, name!, null, IntPtr.Zero);
                else for (var index = 0; index < paths.Length; index++)
                {
                    var path = paths[index];
                    var item = Item(path);
                    switch (action)
                    {
                        case FileAction.Copy: operation.CopyItem(item, target!, null, IntPtr.Zero); break;
                        case FileAction.Move: operation.MoveItem(item, target!, null, IntPtr.Zero); break;
                        case FileAction.Recycle or FileAction.Delete: operation.DeleteItem(item, IntPtr.Zero); break;
                        case FileAction.Rename:
                            var renamed = paths.Length == 1 ? name! : $"{name} ({index + 1}){(Directory.Exists(path) ? "" : Path.GetExtension(path))}";
                            operation.RenameItem(item, renamed, IntPtr.Zero); break;
                    }
                }
                operation.PerformOperations();
                operation.GetAnyOperationsAborted(out var aborted);
                completion.SetResult(!aborted);
            }
            catch (Exception ex) { completion.SetException(ex); }
            finally
            {
                foreach (var item in items) Marshal.ReleaseComObject(item);
                if (operation is not null) Marshal.ReleaseComObject(operation);
            }
        }) { IsBackground = true, Name = "Viewcut Shell file operation" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem
    {
        void BindToHandler(IntPtr context, ref Guid handler, ref Guid id, [MarshalAs(UnmanagedType.Interface)] out object result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint kind, out IntPtr name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem item, uint hint, out int order);
    }

    [ComImport, Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        void Advise(IntPtr sink, out uint cookie);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        void SetProgressDialog(IntPtr dialog);
        void SetProperties(IntPtr properties);
        void SetOwnerWindow(IntPtr owner);
        void ApplyPropertiesToItem(IShellItem item);
        void ApplyPropertiesToItems([MarshalAs(UnmanagedType.IUnknown)] object items);
        void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
        void RenameItems([MarshalAs(UnmanagedType.IUnknown)] object items, [MarshalAs(UnmanagedType.LPWStr)] string name);
        void MoveItem(IShellItem item, IShellItem folder, [MarshalAs(UnmanagedType.LPWStr)] string? name, IntPtr sink);
        void MoveItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem folder);
        void CopyItem(IShellItem item, IShellItem folder, [MarshalAs(UnmanagedType.LPWStr)] string? name, IntPtr sink);
        void CopyItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem folder);
        void DeleteItem(IShellItem item, IntPtr sink);
        void DeleteItems([MarshalAs(UnmanagedType.IUnknown)] object items);
        void NewItem(IShellItem folder, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name,
            [MarshalAs(UnmanagedType.LPWStr)] string? template, IntPtr sink);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr context, ref Guid id, out IShellItem item);
}
