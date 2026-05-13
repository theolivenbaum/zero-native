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

    public static IntPtr GetUserContentManager(IntPtr webview)
    {
        try { return WebView_UCM41(webview); }
        catch (DllNotFoundException) { return WebView_UCM40(webview); }
    }

    public static void AddUserScript(IntPtr ucm, string sourceJs)
    {
        var script = NewUserScript(sourceJs);
        try { UCM_AddScript41(ucm, script); }
        catch (DllNotFoundException) { UCM_AddScript40(ucm, script); }
    }

    public static bool RegisterScriptMessageHandler(IntPtr ucm, string name)
    {
        try { return UCM_RegisterHandler41(ucm, name, IntPtr.Zero); }
        catch (DllNotFoundException) { return UCM_RegisterHandler40(ucm, name); }
    }

    private static IntPtr NewUserScript(string source)
    {
        try { return UserScript_New41(source, 0 /* AllFrames */, 0 /* AtDocumentStart */, IntPtr.Zero, IntPtr.Zero); }
        catch (DllNotFoundException) { return UserScript_New40(source, 0, 0, IntPtr.Zero, IntPtr.Zero); }
    }

    [LibraryImport(WebKit41, EntryPoint = "webkit_web_view_get_user_content_manager")]
    private static partial IntPtr WebView_UCM41(IntPtr webview);

    [LibraryImport(WebKit40, EntryPoint = "webkit_web_view_get_user_content_manager")]
    private static partial IntPtr WebView_UCM40(IntPtr webview);

    [LibraryImport(WebKit41, EntryPoint = "webkit_user_script_new", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr UserScript_New41(string source, int injectedFrames, int injectionTime, IntPtr allowList, IntPtr blockList);

    [LibraryImport(WebKit40, EntryPoint = "webkit_user_script_new", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr UserScript_New40(string source, int injectedFrames, int injectionTime, IntPtr allowList, IntPtr blockList);

    [LibraryImport(WebKit41, EntryPoint = "webkit_user_content_manager_add_script")]
    private static partial void UCM_AddScript41(IntPtr ucm, IntPtr script);

    [LibraryImport(WebKit40, EntryPoint = "webkit_user_content_manager_add_script")]
    private static partial void UCM_AddScript40(IntPtr ucm, IntPtr script);

    [LibraryImport(WebKit41, EntryPoint = "webkit_user_content_manager_register_script_message_handler", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool UCM_RegisterHandler41(IntPtr ucm, string name, IntPtr worldName);

    [LibraryImport(WebKit40, EntryPoint = "webkit_user_content_manager_register_script_message_handler", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool UCM_RegisterHandler40(IntPtr ucm, string name);

    /// <summary>
    /// Reads a string from a <c>WebKitJavascriptResult</c> using the JSCValue API.
    /// </summary>
    public static string? ReadJavascriptResultString(IntPtr jsResult)
    {
        try { return ReadJsResult41(jsResult); }
        catch (DllNotFoundException) { return ReadJsResult40(jsResult); }
    }

    [LibraryImport(WebKit41, EntryPoint = "webkit_javascript_result_get_js_value")]
    private static partial IntPtr JsResultGetValue41(IntPtr jsResult);

    [LibraryImport(WebKit40, EntryPoint = "webkit_javascript_result_get_js_value")]
    private static partial IntPtr JsResultGetValue40(IntPtr jsResult);

    [LibraryImport("libjavascriptcoregtk-4.1.so.0", EntryPoint = "jsc_value_to_string")]
    private static partial IntPtr JscValueToString41(IntPtr value);

    [LibraryImport("libjavascriptcoregtk-4.0.so.18", EntryPoint = "jsc_value_to_string")]
    private static partial IntPtr JscValueToString40(IntPtr value);

    private static string? ReadJsResult41(IntPtr jsResult)
    {
        var value = JsResultGetValue41(jsResult);
        if (value == IntPtr.Zero) return null;
        var strPtr = JscValueToString41(value);
        return Marshal.PtrToStringUTF8(strPtr);
    }

    private static string? ReadJsResult40(IntPtr jsResult)
    {
        var value = JsResultGetValue40(jsResult);
        if (value == IntPtr.Zero) return null;
        var strPtr = JscValueToString40(value);
        return Marshal.PtrToStringUTF8(strPtr);
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
