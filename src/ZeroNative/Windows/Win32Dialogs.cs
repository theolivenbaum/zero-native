#if ZERO_NATIVE_HAS_WEBVIEW2
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using ZeroNative.Platform;

namespace ZeroNative.Windows;

/// <summary>
/// Legacy comdlg32 / user32 file & message dialog implementations. The default
/// path now goes through <see cref="Win32ShellDialogs"/> (modern
/// <c>IFileOpenDialog</c> / <c>IFileSaveDialog</c>); this file is kept as a
/// fallback when COM instantiation fails and still owns the
/// <see cref="ShowMessage"/> wrapper around <c>MessageBoxW</c>.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Win32Dialogs
{
    [Flags]
    private enum OpenFileNameFlags : uint
    {
        OFN_FILEMUSTEXIST = 0x00001000,
        OFN_PATHMUSTEXIST = 0x00000800,
        OFN_HIDEREADONLY = 0x00000004,
        OFN_NOCHANGEDIR = 0x00000008,
        OFN_EXPLORER = 0x00080000,
        OFN_ALLOWMULTISELECT = 0x00000200,
        OFN_OVERWRITEPROMPT = 0x00000002,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string? lpstrFilter;
        public string? lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public string? lpstrFileTitle;
        public int nMaxFileTitle;
        public string? lpstrInitialDir;
        public string? lpstrTitle;
        public uint Flags;
        public ushort nFileOffset;
        public ushort nFileExtension;
        public string? lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string? lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public uint FlagsEx;
    }

    [DllImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileNameW(ref OpenFileName ofn);

    [DllImport("comdlg32.dll", EntryPoint = "GetSaveFileNameW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSaveFileNameW(ref OpenFileName ofn);

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hwnd, string text, string caption, uint type);

    public static OpenDialogResult ShowOpen(IntPtr hwnd, OpenDialogOptions options)
    {
        const int bufferLen = 16 * 1024;
        var buffer = Marshal.AllocHGlobal(bufferLen * sizeof(char));
        try
        {
            // Zero-fill the buffer (Win32 expects a null-terminated string).
            for (var i = 0; i < bufferLen; i++)
                Marshal.WriteInt16(buffer, i * sizeof(char), 0);

            uint flags = (uint)(OpenFileNameFlags.OFN_FILEMUSTEXIST | OpenFileNameFlags.OFN_PATHMUSTEXIST |
                                OpenFileNameFlags.OFN_HIDEREADONLY | OpenFileNameFlags.OFN_NOCHANGEDIR |
                                OpenFileNameFlags.OFN_EXPLORER);
            if (options.AllowMultiple) flags |= (uint)OpenFileNameFlags.OFN_ALLOWMULTISELECT;

            var ofn = new OpenFileName
            {
                lStructSize = Marshal.SizeOf<OpenFileName>(),
                hwndOwner = hwnd,
                lpstrFilter = BuildFilter(options.Filters),
                nMaxCustFilter = 0,
                lpstrFile = buffer,
                nMaxFile = bufferLen,
                lpstrInitialDir = string.IsNullOrEmpty(options.DefaultPath) ? null : options.DefaultPath,
                lpstrTitle = string.IsNullOrEmpty(options.Title) ? null : options.Title,
                Flags = flags,
            };

            if (!GetOpenFileNameW(ref ofn))
                return new OpenDialogResult(Array.Empty<string>());

            return new OpenDialogResult(ReadMultiSelectPaths(buffer, bufferLen, options.AllowMultiple));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static string? ShowSave(IntPtr hwnd, SaveDialogOptions options)
    {
        const int bufferLen = 4096;
        var buffer = Marshal.AllocHGlobal(bufferLen * sizeof(char));
        try
        {
            for (var i = 0; i < bufferLen; i++)
                Marshal.WriteInt16(buffer, i * sizeof(char), 0);

            if (!string.IsNullOrEmpty(options.DefaultName))
            {
                var bytes = Encoding.Unicode.GetBytes(options.DefaultName);
                Marshal.Copy(bytes, 0, buffer, Math.Min(bytes.Length, (bufferLen - 1) * sizeof(char)));
            }

            uint flags = (uint)(OpenFileNameFlags.OFN_PATHMUSTEXIST | OpenFileNameFlags.OFN_OVERWRITEPROMPT |
                                OpenFileNameFlags.OFN_HIDEREADONLY | OpenFileNameFlags.OFN_NOCHANGEDIR |
                                OpenFileNameFlags.OFN_EXPLORER);

            var ofn = new OpenFileName
            {
                lStructSize = Marshal.SizeOf<OpenFileName>(),
                hwndOwner = hwnd,
                lpstrFilter = BuildFilter(options.Filters),
                lpstrFile = buffer,
                nMaxFile = bufferLen,
                lpstrInitialDir = string.IsNullOrEmpty(options.DefaultPath) ? null : options.DefaultPath,
                lpstrTitle = string.IsNullOrEmpty(options.Title) ? null : options.Title,
                Flags = flags,
            };

            if (!GetSaveFileNameW(ref ofn))
                return null;

            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static MessageDialogResult ShowMessage(IntPtr hwnd, MessageDialogOptions options)
    {
        // MB_OK = 0, MB_OKCANCEL = 1, MB_YESNOCANCEL = 3, MB_YESNO = 4
        var hasSecondary = options.SecondaryButton.Length > 0;
        var hasTertiary = options.TertiaryButton.Length > 0;
        uint buttons = hasTertiary ? 3u : hasSecondary ? 4u : 0u;
        uint icon = options.Style switch
        {
            MessageDialogStyle.Warning => 0x30, // MB_ICONWARNING
            MessageDialogStyle.Critical => 0x10, // MB_ICONERROR
            _ => 0x40, // MB_ICONINFORMATION
        };
        var text = string.IsNullOrEmpty(options.InformativeText)
            ? options.Message
            : options.Message + "\n\n" + options.InformativeText;
        var result = MessageBoxW(hwnd, text, options.Title, buttons | icon);
        // MB_YESNO -> IDYES=6 IDNO=7
        // MB_YESNOCANCEL -> IDYES=6 IDNO=7 IDCANCEL=2
        // MB_OK -> IDOK=1
        return result switch
        {
            6 => MessageDialogResult.Primary,    // Yes
            1 => MessageDialogResult.Primary,    // OK
            7 => MessageDialogResult.Secondary,  // No
            2 => MessageDialogResult.Tertiary,   // Cancel
            _ => MessageDialogResult.Primary,
        };
    }

    private static string? BuildFilter(IReadOnlyList<FileFilter> filters)
    {
        if (filters.Count == 0) return null;
        var sb = new StringBuilder();
        foreach (var filter in filters)
        {
            sb.Append(filter.Name).Append('\0');
            sb.Append(string.Join(";", filter.Extensions.Select(e => "*." + e.TrimStart('.'))));
            sb.Append('\0');
        }
        sb.Append('\0');
        return sb.ToString();
    }

    private static IReadOnlyList<string> ReadMultiSelectPaths(IntPtr buffer, int bufferLen, bool allowMultiple)
    {
        // For multi-select, the buffer is a sequence of null-terminated strings:
        //   <directory>\0<file1>\0<file2>\0...\0\0
        // If only one file was selected, it's just <fullpath>\0\0
        if (!allowMultiple)
        {
            var single = Marshal.PtrToStringUni(buffer);
            return string.IsNullOrEmpty(single) ? Array.Empty<string>() : new[] { single };
        }

        var segments = new List<string>();
        var start = 0;
        for (var i = 0; i < bufferLen - 1; i++)
        {
            var ch = (char)Marshal.ReadInt16(buffer, i * sizeof(char));
            if (ch == '\0')
            {
                if (i == start)
                    break; // double null = end
                var s = Marshal.PtrToStringUni(buffer + start * sizeof(char), i - start);
                if (!string.IsNullOrEmpty(s)) segments.Add(s);
                start = i + 1;
            }
        }

        if (segments.Count == 0) return Array.Empty<string>();
        if (segments.Count == 1) return segments;
        // First entry is the directory; remaining are filenames.
        var dir = segments[0];
        return segments.Skip(1).Select(f => Path.Combine(dir, f)).ToList();
    }
}
#endif
