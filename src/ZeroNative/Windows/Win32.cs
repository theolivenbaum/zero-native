#if ZERO_NATIVE_HAS_WEBVIEW2
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ZeroNative.Windows;

/// <summary>
/// Minimal Win32 host: register a window class, create a top-level window,
/// run a GetMessage/DispatchMessage loop.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class Win32
{
    private const string User32 = "user32.dll";
    private const string Kernel32 = "kernel32.dll";

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
    private const uint WM_CLOSE = 0x0010;
    private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
    private const int CW_USEDEFAULT = unchecked((int)0x80000000);

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

    private static readonly Dictionary<IntPtr, Action<int, int>> ResizeCallbacks = new();
    private static IntPtr _wndProcPtr;
    private static bool _classRegistered;
    private static readonly string ClassName = "ZeroNativeWindow";
    private static readonly Func<IntPtr, uint, IntPtr, IntPtr, IntPtr> _wndProcDelegate = WndProc;

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_SIZE:
                if (ResizeCallbacks.TryGetValue(hWnd, out var cb))
                {
                    var w = (int)(lParam.ToInt64() & 0xFFFF);
                    var h = (int)((lParam.ToInt64() >> 16) & 0xFFFF);
                    cb(w, h);
                }
                break;
            case WM_CLOSE:
                DestroyWindow(hWnd);
                return IntPtr.Zero;
            case WM_DESTROY:
                PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
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

    public static IntPtr CreateTopLevelWindow(string title, int width, int height)
    {
        EnsureClassRegistered();
        var hwnd = CreateWindowExW(
            0,
            ClassName,
            title,
            WS_OVERLAPPEDWINDOW,
            CW_USEDEFAULT, CW_USEDEFAULT, width, height,
            IntPtr.Zero, IntPtr.Zero,
            GetModuleHandleW(null), IntPtr.Zero);
        ShowWindow(hwnd, 1 /* SW_NORMAL */);
        UpdateWindow(hwnd);
        return hwnd;
    }

    public static void RunMessageLoop(IntPtr hwnd, Action<int, int> resizeCallback)
    {
        ResizeCallbacks[hwnd] = resizeCallback;
        while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
        ResizeCallbacks.Remove(hwnd);
    }

    public static (int width, int height) GetClientSize(IntPtr hwnd)
    {
        GetClientRect(hwnd, out var r);
        return (r.Right - r.Left, r.Bottom - r.Top);
    }

    public static void CloseWindow(IntPtr hwnd) => DestroyWindow(hwnd);
}
#endif
