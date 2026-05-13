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

    [LibraryImport(Gtk3, EntryPoint = "gdk_atom_intern", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr AtomIntern(string atomName, [MarshalAs(UnmanagedType.U1)] bool onlyIfExists);

    [LibraryImport(Gtk3, EntryPoint = "gtk_clipboard_get")]
    public static partial IntPtr ClipboardGet(IntPtr selection);

    [LibraryImport(Gtk3, EntryPoint = "gtk_clipboard_set_text", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void ClipboardSetText(IntPtr clipboard, string text, int len);

    [LibraryImport(Gtk3, EntryPoint = "gtk_clipboard_wait_for_text")]
    public static partial IntPtr ClipboardWaitForText(IntPtr clipboard);

    [LibraryImport(Gtk3, EntryPoint = "gtk_clipboard_store")]
    public static partial void ClipboardStore(IntPtr clipboard);

    [LibraryImport(Glib, EntryPoint = "g_free")]
    public static partial void GFree(IntPtr ptr);
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

    public static IntPtr GetWebContext(IntPtr webview)
    {
        try { return WebView_GetContext41(webview); }
        catch (DllNotFoundException) { return WebView_GetContext40(webview); }
    }

    public static void RegisterUriScheme(IntPtr context, string scheme, IntPtr callback, IntPtr userData)
    {
        try { Context_RegisterScheme41(context, scheme, callback, userData, IntPtr.Zero); }
        catch (DllNotFoundException) { Context_RegisterScheme40(context, scheme, callback, userData, IntPtr.Zero); }
    }

    public static string? GetUriSchemeRequestUri(IntPtr request)
    {
        try { return Marshal.PtrToStringUTF8(SchemeRequest_GetUri41(request)); }
        catch (DllNotFoundException) { return Marshal.PtrToStringUTF8(SchemeRequest_GetUri40(request)); }
    }

    public static void FinishUriSchemeRequest(IntPtr request, IntPtr stream, long streamLength, string contentType)
    {
        try { SchemeRequest_Finish41(request, stream, streamLength, contentType); }
        catch (DllNotFoundException) { SchemeRequest_Finish40(request, stream, streamLength, contentType); }
    }

    public static IntPtr CreateMemoryInputStream(byte[] data)
    {
        // g_bytes_new copies the data internally, so we can free the unmanaged buffer
        // immediately. The resulting GBytes owns the copy; we ref-transfer it to the
        // input stream and unref our reference.
        IntPtr unmanaged = IntPtr.Zero;
        try
        {
            unmanaged = Marshal.AllocHGlobal(Math.Max(1, data.Length));
            if (data.Length > 0) Marshal.Copy(data, 0, unmanaged, data.Length);
            var bytes = BytesNew(unmanaged, (IntPtr)data.Length);
            var stream = MemoryStream_NewFromBytes(bytes);
            BytesUnref(bytes);
            return stream;
        }
        finally
        {
            if (unmanaged != IntPtr.Zero) Marshal.FreeHGlobal(unmanaged);
        }
    }

    [LibraryImport(WebKit41, EntryPoint = "webkit_web_view_get_context")]
    private static partial IntPtr WebView_GetContext41(IntPtr webview);

    [LibraryImport(WebKit40, EntryPoint = "webkit_web_view_get_context")]
    private static partial IntPtr WebView_GetContext40(IntPtr webview);

    [LibraryImport(WebKit41, EntryPoint = "webkit_web_context_register_uri_scheme", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void Context_RegisterScheme41(IntPtr context, string scheme, IntPtr callback, IntPtr userData, IntPtr destroy);

    [LibraryImport(WebKit40, EntryPoint = "webkit_web_context_register_uri_scheme", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void Context_RegisterScheme40(IntPtr context, string scheme, IntPtr callback, IntPtr userData, IntPtr destroy);

    [LibraryImport(WebKit41, EntryPoint = "webkit_uri_scheme_request_get_uri")]
    private static partial IntPtr SchemeRequest_GetUri41(IntPtr request);

    [LibraryImport(WebKit40, EntryPoint = "webkit_uri_scheme_request_get_uri")]
    private static partial IntPtr SchemeRequest_GetUri40(IntPtr request);

    [LibraryImport(WebKit41, EntryPoint = "webkit_uri_scheme_request_finish", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void SchemeRequest_Finish41(IntPtr request, IntPtr stream, long streamLength, string contentType);

    [LibraryImport(WebKit40, EntryPoint = "webkit_uri_scheme_request_finish", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void SchemeRequest_Finish40(IntPtr request, IntPtr stream, long streamLength, string contentType);

    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_memory_input_stream_new_from_bytes")]
    private static partial IntPtr MemoryStream_NewFromBytes(IntPtr bytes);

    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_bytes_new")]
    private static partial IntPtr BytesNew(IntPtr data, IntPtr size);

    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_bytes_unref")]
    private static partial void BytesUnref(IntPtr bytes);
}
