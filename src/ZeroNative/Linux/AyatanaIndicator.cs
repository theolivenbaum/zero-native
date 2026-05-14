using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ZeroNative.Platform;

namespace ZeroNative.Linux;

/// <summary>
/// Best-effort tray icon via <c>libayatana-appindicator3-1</c>.
/// Falls back to a no-op when the library isn't installed since GTK no longer
/// has a first-class tray API. Status icons (the legacy GTK API) are deprecated
/// and missing on many distros so we skip them entirely.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed partial class AyatanaIndicator : IDisposable
{
    private const string Lib = "libayatana-appindicator3.so.1";
    private const int CategoryApplicationStatus = 0;
    private const int StatusActive = 1;
    private const int StatusPassive = 0;

    [LibraryImport(Lib, EntryPoint = "app_indicator_new", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr AppIndicatorNew(string id, string iconName, int category);

    [LibraryImport(Lib, EntryPoint = "app_indicator_set_status")]
    private static partial void AppIndicatorSetStatus(IntPtr indicator, int status);

    [LibraryImport(Lib, EntryPoint = "app_indicator_set_menu")]
    private static partial void AppIndicatorSetMenu(IntPtr indicator, IntPtr menu);

    [LibraryImport(Lib, EntryPoint = "app_indicator_set_title", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void AppIndicatorSetTitle(IntPtr indicator, string title);

    [LibraryImport(Lib, EntryPoint = "app_indicator_set_icon_full", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void AppIndicatorSetIcon(IntPtr indicator, string iconName, string description);

    [LibraryImport("libgtk-3.so.0", EntryPoint = "gtk_menu_new")]
    private static partial IntPtr MenuNew();

    [LibraryImport("libgtk-3.so.0", EntryPoint = "gtk_menu_item_new_with_label", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr MenuItemNewWithLabel(string label);

    [LibraryImport("libgtk-3.so.0", EntryPoint = "gtk_separator_menu_item_new")]
    private static partial IntPtr SeparatorMenuItemNew();

    [LibraryImport("libgtk-3.so.0", EntryPoint = "gtk_menu_shell_append")]
    private static partial void MenuShellAppend(IntPtr shell, IntPtr child);

    [LibraryImport("libgtk-3.so.0", EntryPoint = "gtk_widget_show_all")]
    private static partial void WidgetShowAll(IntPtr widget);

    [LibraryImport("libgtk-3.so.0", EntryPoint = "gtk_widget_set_sensitive")]
    private static partial void WidgetSetSensitive(IntPtr widget, [MarshalAs(UnmanagedType.U1)] bool sensitive);

    private delegate void MenuItemActivateCallback(IntPtr menuItem, IntPtr userData);

    private IntPtr _indicator;
    private IntPtr _menu;
    private readonly Action<uint> _onAction;
    private readonly MenuItemActivateCallback[] _itemCallbacks;
    private int _itemCount;

    public AyatanaIndicator(Action<uint> onAction)
    {
        _onAction = onAction;
        _itemCallbacks = new MenuItemActivateCallback[64];
    }

    public bool TryInstall(TrayOptions options, string bundleId)
    {
        try
        {
            var icon = string.IsNullOrEmpty(options.IconPath) ? "application-default-icon" : options.IconPath;
            _indicator = AppIndicatorNew(bundleId, icon, CategoryApplicationStatus);
            if (_indicator == IntPtr.Zero) return false;
            AppIndicatorSetStatus(_indicator, StatusActive);
            if (!string.IsNullOrEmpty(options.Tooltip))
                AppIndicatorSetTitle(_indicator, options.Tooltip);
            UpdateMenu(options.Items);
            return true;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    public void UpdateMenu(IReadOnlyList<TrayMenuItem> items)
    {
        if (_indicator == IntPtr.Zero) return;
        _menu = MenuNew();
        _itemCount = 0;
        foreach (var item in items)
        {
            IntPtr menuItem;
            if (item.Separator)
            {
                menuItem = SeparatorMenuItemNew();
            }
            else
            {
                menuItem = MenuItemNewWithLabel(item.Label ?? "");
                if (!item.Enabled) WidgetSetSensitive(menuItem, false);
                if (_itemCount < _itemCallbacks.Length)
                {
                    var id = item.Id;
                    MenuItemActivateCallback cb = (_, _) => _onAction(id);
                    _itemCallbacks[_itemCount++] = cb;
                    var fp = Marshal.GetFunctionPointerForDelegate(cb);
                    Gtk.SignalConnectData(menuItem, "activate", fp, IntPtr.Zero, IntPtr.Zero, 0);
                }
            }
            MenuShellAppend(_menu, menuItem);
        }
        WidgetShowAll(_menu);
        AppIndicatorSetMenu(_indicator, _menu);
    }

    public void Dispose()
    {
        if (_indicator != IntPtr.Zero)
        {
            try { AppIndicatorSetStatus(_indicator, StatusPassive); }
            catch { /* swallow */ }
        }
        _indicator = IntPtr.Zero;
        _menu = IntPtr.Zero;
    }
}
