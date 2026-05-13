using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ZeroNative.Linux;

/// <summary>
/// Minimal P/Invoke bindings for GTK3 and WebKit2GTK.
/// We target gtk-3 / webkit2gtk-4.1 (modern distros) - fall back to 4.0 if 4.1 unavailable.
/// </summary>
[SupportedOSPlatform("linux")]
internal static partial class Gtk
{
    private const string Gtk3 = "libgtk-3.so.0";
    private const string Glib = "libglib-2.0.so.0";
    private const string GObject = "libgobject-2.0.so.0";

    public const int GtkWindowToplevel = 0;
    public const int GdkGravityNorthWest = 1;

    [LibraryImport(Gtk3, EntryPoint = "gtk_init")]
    public static partial void Init(ref int argc, IntPtr argv);

    [LibraryImport(Gtk3, EntryPoint = "gtk_main")]
    public static partial void Main();

    [LibraryImport(Gtk3, EntryPoint = "gtk_main_quit")]
    public static partial void MainQuit();

    [LibraryImport(Gtk3, EntryPoint = "gtk_main_iteration_do")]
    [return: MarshalAs(UnmanagedType.I4)]
    public static partial int MainIterationDo([MarshalAs(UnmanagedType.U1)] bool blocking);

    [LibraryImport(Gtk3, EntryPoint = "gtk_events_pending")]
    [return: MarshalAs(UnmanagedType.I4)]
    public static partial int EventsPending();

    [LibraryImport(Gtk3, EntryPoint = "gtk_window_new")]
    public static partial IntPtr WindowNew(int type);

    [LibraryImport(Gtk3, EntryPoint = "gtk_window_set_title", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void WindowSetTitle(IntPtr window, string title);

    [LibraryImport(Gtk3, EntryPoint = "gtk_window_set_default_size")]
    public static partial void WindowSetDefaultSize(IntPtr window, int width, int height);

    [LibraryImport(Gtk3, EntryPoint = "gtk_widget_show_all")]
    public static partial void WidgetShowAll(IntPtr widget);

    [LibraryImport(Gtk3, EntryPoint = "gtk_widget_destroy")]
    public static partial void WidgetDestroy(IntPtr widget);

    [LibraryImport(Gtk3, EntryPoint = "gtk_container_add")]
    public static partial void ContainerAdd(IntPtr container, IntPtr child);

    [LibraryImport(GObject, EntryPoint = "g_signal_connect_data", StringMarshalling = StringMarshalling.Utf8)]
    public static partial ulong SignalConnectData(
        IntPtr instance, string signalName, IntPtr handler, IntPtr data, IntPtr destroyData, int flags);
}

[SupportedOSPlatform("linux")]
internal static partial class WebKit
{
    private const string WebKit41 = "libwebkit2gtk-4.1.so.0";
    private const string WebKit40 = "libwebkit2gtk-4.0.so.37";

    public static IntPtr WebViewNew()
    {
        try { return WebView_New41(); }
        catch (DllNotFoundException) { return WebView_New40(); }
    }

    public static void LoadHtml(IntPtr webview, string content, string? baseUri)
    {
        try { WebView_LoadHtml41(webview, content, baseUri); }
        catch (DllNotFoundException) { WebView_LoadHtml40(webview, content, baseUri); }
    }

    public static void LoadUri(IntPtr webview, string uri)
    {
        try { WebView_LoadUri41(webview, uri); }
        catch (DllNotFoundException) { WebView_LoadUri40(webview, uri); }
    }

    public static void RunJavaScript(IntPtr webview, string js)
    {
        try { WebView_RunJs41(webview, js, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero); }
        catch (DllNotFoundException) { WebView_RunJs40(webview, js, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero); }
    }

    [LibraryImport(WebKit41, EntryPoint = "webkit_web_view_new")]
    private static partial IntPtr WebView_New41();

    [LibraryImport(WebKit40, EntryPoint = "webkit_web_view_new")]
    private static partial IntPtr WebView_New40();

    [LibraryImport(WebKit41, EntryPoint = "webkit_web_view_load_html", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void WebView_LoadHtml41(IntPtr webview, string content, string? baseUri);

    [LibraryImport(WebKit40, EntryPoint = "webkit_web_view_load_html", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void WebView_LoadHtml40(IntPtr webview, string content, string? baseUri);

    [LibraryImport(WebKit41, EntryPoint = "webkit_web_view_load_uri", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void WebView_LoadUri41(IntPtr webview, string uri);

    [LibraryImport(WebKit40, EntryPoint = "webkit_web_view_load_uri", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void WebView_LoadUri40(IntPtr webview, string uri);

    [LibraryImport(WebKit41, EntryPoint = "webkit_web_view_run_javascript", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void WebView_RunJs41(IntPtr webview, string js, IntPtr cancellable, IntPtr callback, IntPtr user_data);

    [LibraryImport(WebKit40, EntryPoint = "webkit_web_view_run_javascript", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void WebView_RunJs40(IntPtr webview, string js, IntPtr cancellable, IntPtr callback, IntPtr user_data);
}
