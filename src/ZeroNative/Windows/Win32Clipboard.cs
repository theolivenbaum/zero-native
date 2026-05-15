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
    private const uint CF_HDROP = 15;
    private const uint CF_DIB = 8;
    private const uint GMEM_MOVEABLE = 0x0002;
    // BITMAPINFOHEADER + BITMAPFILEHEADER overhead used to wrap CF_DIB payloads
    // into a self-contained .bmp byte array (callers usually want a complete
    // image file, not a raw DIB).
    private const int BITMAPINFOHEADER_SIZE = 40;

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

    [StructLayout(LayoutKind.Sequential)]
    private struct DROPFILES
    {
        public uint pFiles;
        public int pt_x;
        public int pt_y;
        [MarshalAs(UnmanagedType.Bool)] public bool fNC;
        [MarshalAs(UnmanagedType.Bool)] public bool fWide;
    }

    public static IReadOnlyList<string> ReadFiles()
    {
        if (!OpenClipboard(IntPtr.Zero)) return Array.Empty<string>();
        try
        {
            var handle = GetClipboardData(CF_HDROP);
            if (handle == IntPtr.Zero) return Array.Empty<string>();
            var ptr = GlobalLock(handle);
            if (ptr == IntPtr.Zero) return Array.Empty<string>();
            try
            {
                var header = Marshal.PtrToStructure<DROPFILES>(ptr);
                var cursor = ptr + (int)header.pFiles;
                var paths = new List<string>();
                while (true)
                {
                    var s = header.fWide ? Marshal.PtrToStringUni(cursor) : Marshal.PtrToStringAnsi(cursor);
                    if (string.IsNullOrEmpty(s)) break;
                    paths.Add(s);
                    var step = (s.Length + 1) * (header.fWide ? sizeof(char) : 1);
                    cursor += step;
                }
                return paths;
            }
            finally { GlobalUnlock(handle); }
        }
        finally { CloseClipboard(); }
    }

    public static void WriteFiles(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!OpenClipboard(IntPtr.Zero)) return;
        try
        {
            EmptyClipboard();
            if (paths.Count == 0) return;

            // DROPFILES header + wide-string list terminated by a double-null.
            var headerSize = Marshal.SizeOf<DROPFILES>();
            var charCount = 0;
            foreach (var p in paths) charCount += (p?.Length ?? 0) + 1;
            charCount += 1; // final terminator
            var total = (nuint)(headerSize + charCount * sizeof(char));
            var handle = GlobalAlloc(GMEM_MOVEABLE, total);
            if (handle == IntPtr.Zero) return;
            var ptr = GlobalLock(handle);
            if (ptr == IntPtr.Zero) return;
            try
            {
                var header = new DROPFILES { pFiles = (uint)headerSize, fWide = true };
                Marshal.StructureToPtr(header, ptr, fDeleteOld: false);
                var cursor = ptr + headerSize;
                foreach (var p in paths)
                {
                    if (string.IsNullOrEmpty(p)) continue;
                    var bytes = (p.Length + 1) * sizeof(char);
                    Marshal.Copy(p.ToCharArray(), 0, cursor, p.Length);
                    Marshal.WriteInt16(cursor, p.Length * sizeof(char), 0);
                    cursor += bytes;
                }
                Marshal.WriteInt16(cursor, 0);
            }
            finally { GlobalUnlock(handle); }
            SetClipboardData(CF_HDROP, handle);
        }
        finally { CloseClipboard(); }
    }

    public static byte[] ReadImage()
    {
        if (!OpenClipboard(IntPtr.Zero)) return Array.Empty<byte>();
        try
        {
            var handle = GetClipboardData(CF_DIB);
            if (handle == IntPtr.Zero) return Array.Empty<byte>();
            var size = (int)GlobalSize(handle);
            var ptr = GlobalLock(handle);
            if (ptr == IntPtr.Zero || size <= 0) return Array.Empty<byte>();
            try
            {
                // Wrap the DIB in a BMP file header so the bytes are a complete .bmp.
                var bmp = new byte[14 + size];
                bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
                var fileSize = bmp.Length;
                var pixelOffset = 14 + ReadDibPixelOffset(ptr);
                Buffer.BlockCopy(BitConverter.GetBytes(fileSize), 0, bmp, 2, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(pixelOffset), 0, bmp, 10, 4);
                Marshal.Copy(ptr, bmp, 14, size);
                return bmp;
            }
            finally { GlobalUnlock(handle); }
        }
        finally { CloseClipboard(); }
    }

    private static int ReadDibPixelOffset(IntPtr dibPtr)
    {
        // Pixel offset = headerSize + (palette colors * 4). Palette only present
        // for <=8bpp DIBs. Read biSize, biBitCount, biClrUsed.
        var biSize = Marshal.ReadInt32(dibPtr, 0);
        var biBitCount = Marshal.ReadInt16(dibPtr, 14);
        var biClrUsed = Marshal.ReadInt32(dibPtr, 32);
        var palette = biBitCount <= 8
            ? (biClrUsed > 0 ? biClrUsed : 1 << biBitCount) * 4
            : 0;
        return biSize + palette;
    }

    public static void WriteImage(ReadOnlySpan<byte> bytes)
    {
        if (!OpenClipboard(IntPtr.Zero)) return;
        try
        {
            EmptyClipboard();
            if (bytes.IsEmpty) return;

            // Accept either a .bmp file (with the 14-byte BITMAPFILEHEADER) or a
            // raw DIB. CF_DIB wants the raw DIB only — strip the BFH if present.
            int dibOffset = 0, dibLength = bytes.Length;
            if (bytes.Length > 14 && bytes[0] == 'B' && bytes[1] == 'M')
            {
                dibOffset = 14;
                dibLength = bytes.Length - 14;
            }
            else if (bytes.Length < BITMAPINFOHEADER_SIZE)
            {
                return;
            }

            var handle = GlobalAlloc(GMEM_MOVEABLE, (nuint)dibLength);
            if (handle == IntPtr.Zero) return;
            var ptr = GlobalLock(handle);
            if (ptr == IntPtr.Zero) return;
            try
            {
                unsafe
                {
                    fixed (byte* src = bytes)
                    {
                        Buffer.MemoryCopy(src + dibOffset, (void*)ptr, dibLength, dibLength);
                    }
                }
            }
            finally { GlobalUnlock(handle); }
            SetClipboardData(CF_DIB, handle);
        }
        finally { CloseClipboard(); }
    }
}
#endif
