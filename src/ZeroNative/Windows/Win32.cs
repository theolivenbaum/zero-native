#if ZERO_NATIVE_HAS_WEBVIEW2
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ZeroNative.Windows;

/// <summary>
/// Minimal Win32 host: register a window class, create top-level windows,
/// run a GetMessage/DispatchMessage loop.
/// Supports multiple windows, DPI awareness, and forwards size / move / focus
/// events back to a shared listener.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class Win32
{
    private const string User32 = "user32.dll";
    private const string Kernel32 = "kernel32.dll";
    private const string Shcore = "Shcore.dll";

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public IntPtr lpszMenuName;
        public IntPtr lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX, ptY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private const uint WM_DESTROY = 0x0002;
    private const uint WM_SIZE = 0x0005;
    private const uint WM_MOVE = 0x0003;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_ACTIVATE = 0x0006;
    private const uint WM_DPICHANGED = 0x02E0;
    public const uint WM_TRAYICON = 0x0400 + 1;
    private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
    private const int CW_USEDEFAULT = unchecked((int)0x80000000);
    private const int SW_NORMAL = 1;
    private const int SW_HIDE = 0;

    [LibraryImport(Kernel32, EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr GetModuleHandleW(string? name);

    [DllImport(User32, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEX wcex);

    [DllImport(User32, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport(User32)]
    private static extern int ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport(User32)]
    private static extern int UpdateWindow(IntPtr hWnd);

    [DllImport(User32)]
    private static extern int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint min, uint max);

    [DllImport(User32)]
    private static extern int TranslateMessage(ref MSG lpMsg);

    [DllImport(User32)]
    private static extern IntPtr DispatchMessageW(ref MSG lpMsg);

    [DllImport(User32)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport(User32)]
    private static extern void PostQuitMessage(int nExitCode);

    [DllImport(User32)]
    private static extern int DestroyWindow(IntPtr hWnd);

    [DllImport(User32)]
    private static extern int GetClientRect(IntPtr hWnd, out RECT rect);

    [DllImport(User32)]
    private static extern int GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport(User32)]
    private static extern int SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport(User32)]
    private static extern int SetForegroundWindow(IntPtr hWnd);

    [DllImport(User32, CharSet = CharSet.Unicode)]
    private static extern int SetWindowTextW(IntPtr hWnd, string text);

    [DllImport(User32)]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport(Shcore, PreserveSig = true)]
    private static extern int SetProcessDpiAwareness(int value);

    [DllImport(User32)]
    private static extern int SetProcessDpiAwarenessContext(IntPtr context);

    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

    public sealed record WindowCallbacks(
        Action<int, int>? OnResize = null,
        Action<int, int>? OnMove = null,
        Action<bool>? OnActivate = null,
        Action<uint>? OnDpiChanged = null,
        Action? OnClose = null,
        Action<uint>? OnTrayMessage = null);

    private static readonly Dictionary<IntPtr, WindowCallbacks> _callbacks = new();
    private static IntPtr _wndProcPtr;
    private static bool _classRegistered;
    private static readonly string ClassName = "ZeroNativeWindow";
    private static readonly Func<IntPtr, uint, IntPtr, IntPtr, IntPtr> _wndProcDelegate = WndProc;
    private static IntPtr _primaryHwnd;
    private static int _openWindows;
    private static bool _dpiInitialized;

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (_callbacks.TryGetValue(hWnd, out var cb))
        {
            switch (msg)
            {
                case WM_SIZE:
                {
                    var w = (int)(lParam.ToInt64() & 0xFFFF);
                    var h = (int)((lParam.ToInt64() >> 16) & 0xFFFF);
                    cb.OnResize?.Invoke(w, h);
                    break;
                }
                case WM_MOVE:
                {
                    // The packed coordinates are signed shorts; sign-extend manually.
                    var lp = lParam.ToInt64();
                    var x = (short)(lp & 0xFFFF);
                    var y = (short)((lp >> 16) & 0xFFFF);
                    cb.OnMove?.Invoke(x, y);
                    break;
                }
                case WM_ACTIVATE:
                {
                    var activated = (wParam.ToInt64() & 0xFFFF) != 0;
                    cb.OnActivate?.Invoke(activated);
                    break;
                }
                case WM_DPICHANGED:
                {
                    var dpi = (uint)(wParam.ToInt64() & 0xFFFF);
                    cb.OnDpiChanged?.Invoke(dpi);
                    break;
                }
                case WM_CLOSE:
                    cb.OnClose?.Invoke();
                    DestroyWindow(hWnd);
                    return IntPtr.Zero;
            }

            if (msg == WM_TRAYICON && cb.OnTrayMessage is not null)
            {
                cb.OnTrayMessage((uint)(lParam.ToInt64() & 0xFFFFFFFF));
                return IntPtr.Zero;
            }
        }

        if (msg == WM_DESTROY)
        {
            if (_callbacks.Remove(hWnd) && hWnd != IntPtr.Zero)
                _openWindows--;
            if (hWnd == _primaryHwnd || _openWindows <= 0)
                PostQuitMessage(0);
            return IntPtr.Zero;
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private static void EnsureDpiAwareness()
    {
        if (_dpiInitialized) return;
        try { SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); }
        catch
        {
            try { SetProcessDpiAwareness(2 /* PROCESS_PER_MONITOR_DPI_AWARE */); }
            catch { /* best-effort: older Windows lacks Shcore */ }
        }
        _dpiInitialized = true;
    }

    private static void EnsureClassRegistered()
    {
        if (_classRegistered) return;
        _wndProcPtr = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
        var wcex = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            style = 0x0020 | 0x0002 | 0x0001,
            lpfnWndProc = _wndProcPtr,
            hInstance = GetModuleHandleW(null),
            lpszClassName = Marshal.StringToHGlobalUni(ClassName),
            hbrBackground = (IntPtr)6, // COLOR_WINDOW+1
        };
        RegisterClassExW(ref wcex);
        _classRegistered = true;
    }

    public static IntPtr CreateTopLevelWindow(string title, int width, int height, int x = CW_USEDEFAULT, int y = CW_USEDEFAULT, bool primary = true)
    {
        EnsureDpiAwareness();
        EnsureClassRegistered();
        var hwnd = CreateWindowExW(
            0,
            ClassName,
            title,
            WS_OVERLAPPEDWINDOW,
            x, y, width, height,
            IntPtr.Zero, IntPtr.Zero,
            GetModuleHandleW(null), IntPtr.Zero);
        ShowWindow(hwnd, SW_NORMAL);
        UpdateWindow(hwnd);
        if (primary && _primaryHwnd == IntPtr.Zero) _primaryHwnd = hwnd;
        _openWindows++;
        return hwnd;
    }

    public static void RegisterCallbacks(IntPtr hwnd, WindowCallbacks callbacks)
    {
        _callbacks[hwnd] = callbacks;
    }

    public static void RunMessageLoop()
    {
        while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
    }

    public static (int width, int height) GetClientSize(IntPtr hwnd)
    {
        GetClientRect(hwnd, out var r);
        return (r.Right - r.Left, r.Bottom - r.Top);
    }

    public static (int x, int y, int width, int height) GetWindowFrame(IntPtr hwnd)
    {
        GetWindowRect(hwnd, out var r);
        return (r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    public static float GetWindowScaleFactor(IntPtr hwnd)
    {
        try
        {
            var dpi = GetDpiForWindow(hwnd);
            return dpi == 0 ? 1f : dpi / 96f;
        }
        catch { return 1f; }
    }

    public static void CloseWindow(IntPtr hwnd) => DestroyWindow(hwnd);

    public static void FocusWindow(IntPtr hwnd) => SetForegroundWindow(hwnd);

    public static void SetWindowFrame(IntPtr hwnd, int x, int y, int width, int height)
        => SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, SWP_NOZORDER | SWP_NOACTIVATE);

    public static void SetTitle(IntPtr hwnd, string title) => SetWindowTextW(hwnd, title);
}
#endif
