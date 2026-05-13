#if ZERO_NATIVE_HAS_WEBVIEW2
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ZeroNative.Windows;

/// <summary>
/// Thin wrappers around the Win32 clipboard API for text-only read/write.
/// The clipboard is locked while it's held open, so each call opens it briefly
/// and closes again before returning.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class Win32Clipboard
{
    private const string User32 = "user32.dll";
    private const string Kernel32 = "kernel32.dll";
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(IntPtr hWndNewOwner);

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport(User32)]
    private static partial IntPtr GetClipboardData(uint uFormat);

    [LibraryImport(User32)]
    private static partial IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [LibraryImport(Kernel32)]
    private static partial IntPtr GlobalAlloc(uint uFlags, nuint dwBytes);

    [LibraryImport(Kernel32)]
    private static partial IntPtr GlobalLock(IntPtr hMem);

    [LibraryImport(Kernel32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(IntPtr hMem);

    [LibraryImport(Kernel32)]
    private static partial nuint GlobalSize(IntPtr hMem);

    public static string ReadText()
    {
        if (!OpenClipboard(IntPtr.Zero)) return "";
        try
        {
            var handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == IntPtr.Zero) return "";
            var ptr = GlobalLock(handle);
            if (ptr == IntPtr.Zero) return "";
            try
            {
                return Marshal.PtrToStringUni(ptr) ?? "";
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static void WriteText(string text)
    {
        if (!OpenClipboard(IntPtr.Zero)) return;
        try
        {
            EmptyClipboard();
            // GlobalAlloc + memcpy + SetClipboardData transfers ownership of the
            // memory to the clipboard; we must not free it here on success.
            var bytes = (nuint)((text.Length + 1) * sizeof(char));
            var handle = GlobalAlloc(GMEM_MOVEABLE, bytes);
            if (handle == IntPtr.Zero) return;
            var ptr = GlobalLock(handle);
            if (ptr == IntPtr.Zero) return;
            try
            {
                Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
                Marshal.WriteInt16(ptr, text.Length * sizeof(char), 0);
            }
            finally
            {
                GlobalUnlock(handle);
            }
            SetClipboardData(CF_UNICODETEXT, handle);
        }
        finally
        {
            CloseClipboard();
        }
    }
}
#endif
