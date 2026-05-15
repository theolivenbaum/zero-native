using System.Runtime.InteropServices;
using ZeroNative.Primitives;

namespace ZeroNative.Cef;

/// <summary>
/// Bridges CefBrowserHost.GetWindowHandle to the OS-native window so callers
/// can drive programmatic geometry changes against CEF browsers.
///
/// <para>
/// CEF does not expose a portable "set bounds" API for popup-style hosting:
/// <c>CefWindowInfo.SetAsPopup</c> hands the toplevel to Chromium and the
/// CEF C API only lets us read the embedded browser's native handle back.
/// On each OS we translate that handle into the toplevel frame window and
/// drive geometry through the corresponding native API.
/// </para>
/// </summary>
internal static partial class CefNativeWindow
{
    /// <summary>
    /// Sets the frame of the toplevel that hosts the given CEF browser handle.
    /// Coordinates are interpreted per the platform's window-manager convention
    /// (top-left origin on Windows/Linux; macOS uses bottom-left screen coords
    /// — callers pass top-left frames and we translate here).
    /// </summary>
    public static void SetFrame(IntPtr handle, RectF frame)
    {
        if (handle == IntPtr.Zero) throw new InvalidOperationException("CEF browser has no window handle yet");

        if (OperatingSystem.IsWindows()) WindowsSetFrame(handle, frame);
        else if (OperatingSystem.IsMacOS()) MacSetFrame(handle, frame);
        else if (OperatingSystem.IsLinux()) LinuxSetFrame(handle, frame);
        else throw new PlatformNotSupportedException();
    }

    // --- Windows: HWND -> GetAncestor(GA_ROOT) -> SetWindowPos ---

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void WindowsSetFrame(IntPtr hwnd, RectF frame)
    {
        var root = WinGetAncestor(hwnd, GA_ROOT);
        if (root == IntPtr.Zero) root = hwnd;
        // SWP_NOZORDER | SWP_NOACTIVATE keeps Z-order and focus untouched.
        WinSetWindowPos(root, IntPtr.Zero, (int)frame.X, (int)frame.Y, (int)frame.Width, (int)frame.Height, 0x0004 | 0x0010);
    }

    private const uint GA_ROOT = 2;

    [LibraryImport("user32.dll", EntryPoint = "GetAncestor")]
    private static partial IntPtr WinGetAncestor(IntPtr hwnd, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WinSetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int cx, int cy, uint flags);

    // --- macOS: NSView* -> [view window] -> setFrame:display: ---

    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private static void MacSetFrame(IntPtr nsView, RectF frame)
    {
        var selWindow = MacSel("window");
        var window = MacMsgSend(nsView, selWindow);
        if (window == IntPtr.Zero)
            throw new InvalidOperationException("NSView is not attached to an NSWindow");

        // The runtime passes a top-left frame; macOS NSWindow.setFrame: wants
        // bottom-left screen coordinates. Translate against the primary screen
        // height (NSScreen mainScreen.frame.size.height).
        var screenH = MacScreenHeight();
        var cgFrame = new MacCGRect(frame.X, screenH - frame.Y - frame.Height, frame.Width, frame.Height);

        var selSetFrame = MacSel("setFrame:display:");
        MacMsgSend_Frame(window, selSetFrame, cgFrame, true);
    }

    private static double MacScreenHeight()
    {
        var nsScreen = MacGetClass("NSScreen");
        var selMain = MacSel("mainScreen");
        var screen = MacMsgSend(nsScreen, selMain);
        if (screen == IntPtr.Zero) return 0;
        var selFrame = MacSel("frame");
        var rect = MacMsgSend_RetRect(screen, selFrame);
        return rect.Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MacCGRect
    {
        public double X, Y, Width, Height;
        public MacCGRect(double x, double y, double w, double h) { X = x; Y = y; Width = w; Height = h; }
    }

    private const string ObjCLib = "/usr/lib/libobjc.A.dylib";

    [LibraryImport(ObjCLib, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr MacGetClass(string name);

    [LibraryImport(ObjCLib, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr MacSel(string name);

    [LibraryImport(ObjCLib, EntryPoint = "objc_msgSend")]
    private static partial IntPtr MacMsgSend(IntPtr target, IntPtr sel);

    [LibraryImport(ObjCLib, EntryPoint = "objc_msgSend")]
    private static partial IntPtr MacMsgSend_Frame(IntPtr target, IntPtr sel, MacCGRect frame, [MarshalAs(UnmanagedType.U1)] bool display);

    [LibraryImport(ObjCLib, EntryPoint = "objc_msgSend")]
    private static partial MacCGRect MacMsgSend_RetRect(IntPtr target, IntPtr sel);

    // --- Linux: X11 XID -> walk to toplevel -> XMoveResizeWindow ---

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static void LinuxSetFrame(IntPtr xid, RectF frame)
    {
        var display = X11OpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero)
            throw new InvalidOperationException("Could not open X11 display");

        try
        {
            var toplevel = WalkToToplevel(display, xid);
            X11MoveResizeWindow(display, toplevel, (int)frame.X, (int)frame.Y, (uint)frame.Width, (uint)frame.Height);
            X11Flush(display);
        }
        finally
        {
            X11CloseDisplay(display);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static IntPtr WalkToToplevel(IntPtr display, IntPtr window)
    {
        var current = window;
        // X11 hierarchy: root > toplevel(s) > children. CEF's SetAsPopup creates
        // a toplevel; GetWindowHandle returns the embedded browser window. Walk
        // up until parent == root.
        for (var i = 0; i < 16; i++)
        {
            if (X11QueryTree(display, current, out var root, out var parent, out var children, out _) == 0)
                break;
            if (children != IntPtr.Zero) X11Free(children);
            if (parent == IntPtr.Zero || parent == root) return current;
            current = parent;
        }
        return current;
    }

    private const string X11Lib = "libX11.so.6";

    [LibraryImport(X11Lib, EntryPoint = "XOpenDisplay")]
    private static partial IntPtr X11OpenDisplay(IntPtr name);

    [LibraryImport(X11Lib, EntryPoint = "XCloseDisplay")]
    private static partial int X11CloseDisplay(IntPtr display);

    [LibraryImport(X11Lib, EntryPoint = "XMoveResizeWindow")]
    private static partial int X11MoveResizeWindow(IntPtr display, IntPtr window, int x, int y, uint width, uint height);

    [LibraryImport(X11Lib, EntryPoint = "XQueryTree")]
    private static partial int X11QueryTree(IntPtr display, IntPtr window, out IntPtr rootReturn, out IntPtr parentReturn, out IntPtr childrenReturn, out uint nChildrenReturn);

    [LibraryImport(X11Lib, EntryPoint = "XFree")]
    private static partial int X11Free(IntPtr data);

    [LibraryImport(X11Lib, EntryPoint = "XFlush")]
    private static partial int X11Flush(IntPtr display);
}
