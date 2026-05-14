#if ZERO_NATIVE_HAS_WEBVIEW2
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ZeroNative.Platform;

namespace ZeroNative.Windows;

/// <summary>
/// Minimal Win32 tray icon via <c>Shell_NotifyIconW</c>, paired with a popup
/// menu created from <see cref="TrayOptions.Items"/>. Tray menu clicks raise
/// <c>WM_COMMAND</c> against the parent window which the caller maps to
/// <see cref="PlatformEvent.TrayAction"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class Win32Tray : IDisposable
{
    private const string User32 = "user32.dll";
    private const string Shell32 = "shell32.dll";

    private const uint NIM_ADD = 0;
    private const uint NIM_MODIFY = 1;
    private const uint NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 0x01;
    private const uint NIF_ICON = 0x02;
    private const uint NIF_TIP = 0x04;
    private const uint WM_USER = 0x0400;
    private const uint WM_TRAYICON = WM_USER + 1;
    private const uint MF_STRING = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;
    private const uint MF_GRAYED = 0x00000001;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_LBUTTONUP = 0x0202;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport(Shell32, CharSet = CharSet.Unicode)]
    private static extern int Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport(User32, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport(User32)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport(User32, CharSet = CharSet.Unicode)]
    private static extern int AppendMenuW(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string? lpNewItem);

    [DllImport(User32)]
    private static extern int DestroyMenu(IntPtr hMenu);

    [DllImport(User32)]
    private static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport(User32)]
    private static extern int SetForegroundWindow(IntPtr hWnd);

    [DllImport(User32)]
    private static extern int GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x10;
    private const uint LR_DEFAULTSIZE = 0x40;

    private NOTIFYICONDATA _nid;
    private IntPtr _menu;
    private IReadOnlyList<TrayMenuItem> _items = Array.Empty<TrayMenuItem>();
    private bool _installed;
    private readonly IntPtr _ownerHwnd;
    private readonly Action<uint> _onAction;

    public Win32Tray(IntPtr ownerHwnd, Action<uint> onAction)
    {
        _ownerHwnd = ownerHwnd;
        _onAction = onAction;
    }

    public void Install(TrayOptions options)
    {
        Remove();
        var hIcon = LoadIconOrDefault(options.IconPath);
        _nid = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _ownerHwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = hIcon,
            szTip = options.Tooltip ?? string.Empty,
        };
        Shell_NotifyIconW(NIM_ADD, ref _nid);
        _installed = true;
        UpdateMenu(options.Items);
    }

    public void UpdateMenu(IReadOnlyList<TrayMenuItem> items)
    {
        if (_menu != IntPtr.Zero) DestroyMenu(_menu);
        _menu = CreatePopupMenu();
        _items = items;
        foreach (var item in items)
        {
            if (item.Separator)
            {
                AppendMenuW(_menu, MF_SEPARATOR, IntPtr.Zero, null);
                continue;
            }
            var flags = MF_STRING | (item.Enabled ? 0u : MF_GRAYED);
            var cmdId = item.Id == 0 ? (uint)items.Count : item.Id;
            AppendMenuW(_menu, flags, (IntPtr)cmdId, item.Label);
        }
    }

    /// <summary>
    /// Invoked from the host window proc on <c>WM_TRAYICON</c>. Returns true if
    /// the message produced a click that the platform should forward as
    /// <see cref="PlatformEvent.TrayAction"/>.
    /// </summary>
    public void HandleTrayMessage(uint lParam)
    {
        // LOWORD of lParam carries the notification (mouse event) code.
        var notification = lParam & 0xFFFF;
        if (notification != WM_RBUTTONUP && notification != WM_LBUTTONUP) return;
        if (_menu == IntPtr.Zero) return;
        GetCursorPos(out var pt);
        SetForegroundWindow(_ownerHwnd);
        var cmd = TrackPopupMenu(_menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, 0, _ownerHwnd, IntPtr.Zero);
        if (cmd > 0) _onAction((uint)cmd);
    }

    public void Remove()
    {
        if (_installed)
        {
            Shell_NotifyIconW(NIM_DELETE, ref _nid);
            _installed = false;
        }
        if (_menu != IntPtr.Zero) { DestroyMenu(_menu); _menu = IntPtr.Zero; }
    }

    private static IntPtr LoadIconOrDefault(string? path)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            var icon = LoadImageW(IntPtr.Zero, path, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
            if (icon != IntPtr.Zero) return icon;
        }
        // Fallback: system "application" icon (IDI_APPLICATION).
        return LoadImageW(IntPtr.Zero, "#32512", IMAGE_ICON, 0, 0, LR_DEFAULTSIZE);
    }

    public void Dispose() => Remove();
}
#endif
