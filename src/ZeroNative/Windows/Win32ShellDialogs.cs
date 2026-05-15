#if ZERO_NATIVE_HAS_WEBVIEW2
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ZeroNative.Platform;

namespace ZeroNative.Windows;

/// <summary>
/// Modern Shell file dialogs via the COM <c>IFileOpenDialog</c> / <c>IFileSaveDialog</c>
/// interfaces introduced in Windows Vista. These produce the modern Explorer-style
/// chrome (places sidebar, custom places, breadcrumb path bar) instead of the
/// legacy <c>GetOpenFileNameW</c> dialog.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Win32ShellDialogs
{
    // CLSIDs / IIDs sourced from <shobjidl.h>.
    private static readonly Guid CLSID_FileOpenDialog = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
    private static readonly Guid CLSID_FileSaveDialog = new("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B");
    private static readonly Guid IID_IFileOpenDialog = new("D57C7288-D4AD-4768-BE02-9D969532D960");
    private static readonly Guid IID_IFileSaveDialog = new("84BCCD23-5FDE-4CDB-AEA4-AF64B83D78AB");
    private static readonly Guid IID_IShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    // FILEOPENDIALOGOPTIONS bits.
    [Flags]
    private enum FOS : uint
    {
        OVERWRITEPROMPT  = 0x00000002,
        STRICTFILETYPES  = 0x00000004,
        NOCHANGEDIR      = 0x00000008,
        PICKFOLDERS      = 0x00000020,
        FORCEFILESYSTEM  = 0x00000040,
        ALLOWMULTISELECT = 0x00000200,
        PATHMUSTEXIST    = 0x00000800,
        FILEMUSTEXIST    = 0x00001000,
        CREATEPROMPT     = 0x00002000,
        NOREADONLYRETURN = 0x00008000,
        DONTADDTORECENT  = 0x02000000,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ComDlgFilterSpec
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string Name;
        [MarshalAs(UnmanagedType.LPWStr)] public string Spec;
    }

    private const int SIGDN_FILESYSPATH = unchecked((int)0x80058000);

    [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        // IModalWindow
        [PreserveSig] int Show(IntPtr hwndOwner);
        // IFileDialog
        void SetFileTypes(uint cFileTypes, [In] ComDlgFilterSpec[] rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, int alignment);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
    }

    [ComImport, Guid("D57C7288-D4AD-4768-BE02-9D969532D960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog : IFileDialog
    {
        // IFileOpenDialog
        new int Show(IntPtr hwndOwner);
        new void SetFileTypes(uint cFileTypes, [In] ComDlgFilterSpec[] rgFilterSpec);
        new void SetFileTypeIndex(uint iFileType);
        new void GetFileTypeIndex(out uint piFileType);
        new void Advise(IntPtr pfde, out uint pdwCookie);
        new void Unadvise(uint dwCookie);
        new void SetOptions(uint fos);
        new void GetOptions(out uint pfos);
        new void SetDefaultFolder(IShellItem psi);
        new void SetFolder(IShellItem psi);
        new void GetFolder(out IShellItem ppsi);
        new void GetCurrentSelection(out IShellItem ppsi);
        new void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        new void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        new void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        new void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        new void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        new void GetResult(out IShellItem ppsi);
        new void AddPlace(IShellItem psi, int alignment);
        new void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        new void Close(int hr);
        new void SetClientGuid(ref Guid guid);
        new void ClearClientData();
        new void SetFilter(IntPtr pFilter);
        void GetResults(out IShellItemArray ppenum);
        void GetSelectedItems(out IShellItemArray ppsai);
    }

    [ComImport, Guid("84BCCD23-5FDE-4CDB-AEA4-AF64B83D78AB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileSaveDialog : IFileDialog
    {
        new int Show(IntPtr hwndOwner);
        new void SetFileTypes(uint cFileTypes, [In] ComDlgFilterSpec[] rgFilterSpec);
        new void SetFileTypeIndex(uint iFileType);
        new void GetFileTypeIndex(out uint piFileType);
        new void Advise(IntPtr pfde, out uint pdwCookie);
        new void Unadvise(uint dwCookie);
        new void SetOptions(uint fos);
        new void GetOptions(out uint pfos);
        new void SetDefaultFolder(IShellItem psi);
        new void SetFolder(IShellItem psi);
        new void GetFolder(out IShellItem ppsi);
        new void GetCurrentSelection(out IShellItem ppsi);
        new void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        new void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        new void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        new void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        new void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        new void GetResult(out IShellItem ppsi);
        new void AddPlace(IShellItem psi, int alignment);
        new void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        new void Close(int hr);
        new void SetClientGuid(ref Guid guid);
        new void ClearClientData();
        new void SetFilter(IntPtr pFilter);
        void SetSaveAsItem(IShellItem psi);
        void SetProperties(IntPtr pStore);
        void SetCollectedProperties(IntPtr pList, [MarshalAs(UnmanagedType.Bool)] bool fAppendDefault);
        void GetProperties(out IntPtr ppStore);
        void ApplyProperties(IShellItem psi, IntPtr pStore, IntPtr hwnd, IntPtr pSink);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        [PreserveSig] int GetDisplayName(int sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetPropertyStore(int flags, ref Guid riid, out IntPtr ppv);
        void GetPropertyDescriptionList(IntPtr keyType, ref Guid riid, out IntPtr ppv);
        void GetAttributes(int attribFlags, uint sfgaoMask, out uint psfgaoAttribs);
        void GetCount(out uint pdwNumItems);
        void GetItemAt(uint dwIndex, out IShellItem ppsi);
        void EnumItems(out IntPtr ppenumShellItems);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rclsid,
        IntPtr pUnkOuter,
        uint dwClsContext,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    private const uint CLSCTX_INPROC_SERVER = 0x1;
    private const uint COINIT_APARTMENTTHREADED = 0x2;
    private const int E_CANCELLED_USER = unchecked((int)0x800704C7);

    [ThreadStatic] private static bool s_comInitialized;

    private static void EnsureCom()
    {
        if (s_comInitialized) return;
        // S_FALSE (1) means "already initialized on this thread" — both are fine.
        _ = CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);
        s_comInitialized = true;
    }

    public static OpenDialogResult ShowOpen(IntPtr hwnd, OpenDialogOptions options)
    {
        EnsureCom();
        var hr = CoCreateInstance(CLSID_FileOpenDialog, IntPtr.Zero, CLSCTX_INPROC_SERVER, IID_IFileOpenDialog, out var raw);
        if (hr != 0 || raw is not IFileOpenDialog dialog)
            return Win32Dialogs.ShowOpen(hwnd, options);

        try
        {
            var fos = FOS.PATHMUSTEXIST | FOS.FORCEFILESYSTEM | FOS.NOCHANGEDIR;
            if (options.AllowDirectories) fos |= FOS.PICKFOLDERS;
            else fos |= FOS.FILEMUSTEXIST;
            if (options.AllowMultiple) fos |= FOS.ALLOWMULTISELECT;
            dialog.SetOptions((uint)fos);

            if (options.Title.Length > 0) dialog.SetTitle(options.Title);
            ApplyFilters(dialog, options.Filters);
            ApplyInitialFolder(dialog, options.DefaultPath);

            var result = dialog.Show(hwnd);
            if (result == E_CANCELLED_USER || result < 0)
                return new OpenDialogResult(Array.Empty<string>());

            if (options.AllowMultiple)
            {
                dialog.GetResults(out var items);
                if (items is null) return new OpenDialogResult(Array.Empty<string>());
                try
                {
                    items.GetCount(out var count);
                    var paths = new List<string>((int)count);
                    for (uint i = 0; i < count; i++)
                    {
                        items.GetItemAt(i, out var item);
                        if (item is null) continue;
                        try
                        {
                            var path = GetItemPath(item);
                            if (!string.IsNullOrEmpty(path)) paths.Add(path!);
                        }
                        finally { Marshal.ReleaseComObject(item); }
                    }
                    return new OpenDialogResult(paths);
                }
                finally { Marshal.ReleaseComObject(items); }
            }
            else
            {
                dialog.GetResult(out var item);
                if (item is null) return new OpenDialogResult(Array.Empty<string>());
                try
                {
                    var path = GetItemPath(item);
                    return string.IsNullOrEmpty(path)
                        ? new OpenDialogResult(Array.Empty<string>())
                        : new OpenDialogResult(new[] { path! });
                }
                finally { Marshal.ReleaseComObject(item); }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }

    public static string? ShowSave(IntPtr hwnd, SaveDialogOptions options)
    {
        EnsureCom();
        var hr = CoCreateInstance(CLSID_FileSaveDialog, IntPtr.Zero, CLSCTX_INPROC_SERVER, IID_IFileSaveDialog, out var raw);
        if (hr != 0 || raw is not IFileSaveDialog dialog)
            return Win32Dialogs.ShowSave(hwnd, options);

        try
        {
            var fos = FOS.PATHMUSTEXIST | FOS.FORCEFILESYSTEM | FOS.NOCHANGEDIR | FOS.OVERWRITEPROMPT;
            dialog.SetOptions((uint)fos);

            if (options.Title.Length > 0) dialog.SetTitle(options.Title);
            if (options.DefaultName.Length > 0) dialog.SetFileName(options.DefaultName);
            ApplyFilters(dialog, options.Filters);
            ApplyInitialFolder(dialog, options.DefaultPath);

            if (options.Filters is { Count: > 0 } && options.Filters[0].Extensions.Count > 0)
                dialog.SetDefaultExtension(options.Filters[0].Extensions[0].TrimStart('.'));

            var result = dialog.Show(hwnd);
            if (result == E_CANCELLED_USER || result < 0)
                return null;

            dialog.GetResult(out var item);
            if (item is null) return null;
            try { return GetItemPath(item); }
            finally { Marshal.ReleaseComObject(item); }
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }

    private static void ApplyFilters(IFileDialog dialog, IReadOnlyList<FileFilter> filters)
    {
        if (filters is null || filters.Count == 0) return;
        var specs = new ComDlgFilterSpec[filters.Count];
        for (var i = 0; i < filters.Count; i++)
        {
            var f = filters[i];
            specs[i] = new ComDlgFilterSpec
            {
                Name = f.Name,
                Spec = string.Join(";", f.Extensions.Select(e => "*." + e.TrimStart('.'))),
            };
        }
        dialog.SetFileTypes((uint)specs.Length, specs);
        dialog.SetFileTypeIndex(1);
    }

    private static void ApplyInitialFolder(IFileDialog dialog, string defaultPath)
    {
        if (string.IsNullOrEmpty(defaultPath)) return;
        try
        {
            SHCreateItemFromParsingName(defaultPath, IntPtr.Zero, IID_IShellItem, out var folder);
            if (folder is null) return;
            try { dialog.SetFolder(folder); }
            finally { Marshal.ReleaseComObject(folder); }
        }
        catch
        {
            // Best-effort: leave default folder alone if the path is bad.
        }
    }

    private static string? GetItemPath(IShellItem item)
    {
        IntPtr namePtr = IntPtr.Zero;
        var hr = item.GetDisplayName(SIGDN_FILESYSPATH, out namePtr);
        if (hr != 0 || namePtr == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUni(namePtr); }
        finally { Marshal.FreeCoTaskMem(namePtr); }
    }
}
#endif
