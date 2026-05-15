using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ZeroNative.Assets;
using ZeroNative.Bridge;
using ZeroNative.Platform;
using ZeroNative.Security;

namespace ZeroNative.Linux;

/// <summary>
/// Linux host using GTK + WebKit via P/Invoke. The runtime picks
/// GTK3 + WebKit2GTK 4.0/4.1 on legacy systems and GTK4 + WebKitGTK 6.0
/// when the modern stack is the only thing installed; the pairing is
/// resolved once at startup (see <see cref="WebKit.Probe"/> +
/// <see cref="Gtk.PairWithWebKit"/>) and every Gtk/WebKit call dispatches
/// through the detected ABI thereafter.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class WebKitGtkPlatform : IPlatform, IPlatformServices
{
    public string Name => "linux";
    public Surface Surface { get; }
    public AppInfo AppInfo { get; }
    public IPlatformServices Services => this;

    private IntPtr _window;
    private IntPtr _webView;
    private Action<PlatformEvent>? _handler;
    private SecurityPolicy _policy = new();
    private AssetServer? _assetServer;
    private string? _assetScheme;
    private readonly HashSet<string> _registeredSchemes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ulong, IntPtr> _windows = new();
    private readonly Dictionary<IntPtr, ulong> _windowIds = new();
    private ulong _primaryWindowId = 1;
    private AyatanaIndicator? _tray;

    // Named delegates: Marshal.GetFunctionPointerForDelegate rejects generic Action/Func.
    //
    // The destroy hook returns int even though GTK3's "destroy" signal is void —
    // GTK4 reuses the same slot for "close-request" (gboolean: FALSE = allow close).
    // Always returning 0 makes both ABIs happy.
    private delegate int DestroyCallback(IntPtr instance, IntPtr userData);
    private delegate void ScriptMessageCallback(IntPtr ucm, IntPtr jsResult, IntPtr userData);
    private delegate void SchemeRequestCallback(IntPtr request, IntPtr userData);
    private delegate int SignalEventCallback(IntPtr widget, IntPtr eventPtr, IntPtr userData);
    private delegate int DecidePolicyCallback(IntPtr webview, IntPtr decision, int decisionType, IntPtr userData);
    // GObject "notify::<prop>" callback: void(GObject* object, GParamSpec* pspec, gpointer user_data).
    private delegate void NotifyCallback(IntPtr instance, IntPtr pspec, IntPtr userData);

    private static readonly DestroyCallback _destroyCallback = OnDestroyStatic;
    // Keep the delegates alive in fields so the GC doesn't reclaim them while GTK holds the function pointer.
    private readonly ScriptMessageCallback _scriptMessageCallback;
    private readonly SchemeRequestCallback _schemeRequestCallback;
    private readonly SignalEventCallback _configureCallback;
    private readonly SignalEventCallback _focusInCallback;
    private readonly NotifyCallback _notifyGeometryCallback;
    private readonly NotifyCallback _notifyIsActiveCallback;
    private readonly DecidePolicyCallback _decidePolicyCallback;
    private static WebKitGtkPlatform? _activeInstance;

    public WebKitGtkPlatform(AppInfo appInfo, Surface surface)
    {
        AppInfo = appInfo;
        Surface = surface;
        _scriptMessageCallback = OnScriptMessage;
        _schemeRequestCallback = OnSchemeRequest;
        _configureCallback = OnConfigureEvent;
        _focusInCallback = OnFocusInEvent;
        _notifyGeometryCallback = OnNotifyGeometry;
        _notifyIsActiveCallback = OnNotifyIsActive;
        _decidePolicyCallback = OnDecidePolicy;
    }

    public void Run(Action<PlatformEvent> handler)
    {
        _handler = handler;
        InitializeGtk();

        handler(new PlatformEvent.AppStart());
        handler(new PlatformEvent.SurfaceResized(Surface));

        var window = AppInfo.ResolvedStartupWindow(0);
        handler(new PlatformEvent.WindowFrameChanged(new WindowState
        {
            Id = window.Id,
            Label = window.Label,
            Title = window.ResolvedTitle(AppInfo.AppName),
            Frame = window.DefaultFrame,
            ScaleFactor = Surface.ScaleFactor,
            Open = true,
            Focused = true,
        }));

        // Enter main loop
        Gtk.Main();

        handler(new PlatformEvent.AppShutdown());
    }

    private void InitializeGtk()
    {
        // Probe the WebKit ABI before touching GTK so we can ask GTK to load
        // the matching major version. WebKitGTK 6.0 is GTK4-bound; 4.x is GTK3.
        var webkitAbi = WebKit.Probe();
        Gtk.PairWithWebKit(webkitAbi);

        var argc = 0;
        Gtk.Init(ref argc, IntPtr.Zero);

        var window = AppInfo.ResolvedStartupWindow(0);
        _primaryWindowId = window.Id;
        _window = Gtk.WindowNew(Gtk.GtkWindowToplevel);
        Gtk.WindowSetTitle(_window, window.ResolvedTitle(AppInfo.AppName));
        Gtk.WindowSetDefaultSize(_window, (int)window.DefaultFrame.Width, (int)window.DefaultFrame.Height);
        Gtk.WindowSetResizable(_window, window.Resizable);
        if (window.DefaultFrame.X != 0 || window.DefaultFrame.Y != 0)
            Gtk.WindowMove(_window, (int)window.DefaultFrame.X, (int)window.DefaultFrame.Y);

        _webView = WebKit.WebViewNew();
        Gtk.WindowSetChild(_window, _webView);
        _windows[window.Id] = _window;
        _windowIds[_window] = window.Id;

        // Navigation policy enforcement (decide-policy signal). The handler decides
        // inline vs. block vs. open-externally based on the configured SecurityPolicy.
        var policyCb = Marshal.GetFunctionPointerForDelegate(_decidePolicyCallback);
        Gtk.SignalConnectData(_webView, "decide-policy", policyCb, IntPtr.Zero, IntPtr.Zero, 0);

        // Wire up the bridge: inject the JS shim and register the script-message handler.
        var ucm = WebKit.GetUserContentManager(_webView);
        if (ucm != IntPtr.Zero)
        {
            WebKit.AddUserScript(ucm, BridgeJavascript.Build(BridgeJavascript.Channel.WebKitMessageHandler));
            WebKit.RegisterScriptMessageHandler(ucm, BridgeJavascript.HandlerName);
            var scriptCb = Marshal.GetFunctionPointerForDelegate(_scriptMessageCallback);
            Gtk.SignalConnectData(ucm, $"script-message-received::{BridgeJavascript.HandlerName}", scriptCb, IntPtr.Zero, IntPtr.Zero, 0);
            _activeInstance = this;
        }

        Gtk.WidgetShowAll(_window);

        // Wire up "user closed the window" → quit the main loop. The signal name
        // differs between GTK3 (the widget-level "destroy") and GTK4 ("close-request"
        // on the GtkWindow). Connecting both is harmless on either ABI — only the
        // matching one fires; g_signal_connect_data tolerates the mismatch.
        var destroyCb = Marshal.GetFunctionPointerForDelegate(_destroyCallback);
        Gtk.SignalConnectData(_window,
            Gtk.IsGtk4 ? "close-request" : "destroy",
            destroyCb, IntPtr.Zero, IntPtr.Zero, 0);

        if (!Gtk.IsGtk4)
        {
            // GTK3 emits configure-event / focus-in-event directly on the window.
            var configureCb = Marshal.GetFunctionPointerForDelegate(_configureCallback);
            Gtk.SignalConnectData(_window, "configure-event", configureCb, IntPtr.Zero, IntPtr.Zero, 0);
            var focusInCb = Marshal.GetFunctionPointerForDelegate(_focusInCallback);
            Gtk.SignalConnectData(_window, "focus-in-event", focusInCb, IntPtr.Zero, IntPtr.Zero, 0);
        }
        else
        {
            // GTK4 removed configure-event / focus-in-event; geometry comes from
            // notify::default-width/height (fires on user resize) and focus from
            // notify::is-active. Programmatic position isn't exposed, so OnNotifyGeometry
            // reports (0,0) for X/Y — the WM owns placement on GTK4.
            var notifyGeomCb = Marshal.GetFunctionPointerForDelegate(_notifyGeometryCallback);
            Gtk.SignalConnectData(_window, "notify::default-width", notifyGeomCb, IntPtr.Zero, IntPtr.Zero, 0);
            Gtk.SignalConnectData(_window, "notify::default-height", notifyGeomCb, IntPtr.Zero, IntPtr.Zero, 0);
            var notifyActiveCb = Marshal.GetFunctionPointerForDelegate(_notifyIsActiveCallback);
            Gtk.SignalConnectData(_window, "notify::is-active", notifyActiveCb, IntPtr.Zero, IntPtr.Zero, 0);
        }
    }

    /// <summary>
    /// GTK4 size-change signal. Fires when the user resizes the toplevel or the WM
    /// rewrites the default size. Emits the same WindowFrameChanged / SurfaceResized
    /// events as the GTK3 configure-event handler. Position is reported as (0,0)
    /// because GTK4 no longer exposes absolute screen coordinates.
    /// </summary>
    private void OnNotifyGeometry(IntPtr instance, IntPtr pspec, IntPtr userData)
    {
        OnConfigureEvent(instance, IntPtr.Zero, IntPtr.Zero);
    }

    private void OnNotifyIsActive(IntPtr instance, IntPtr pspec, IntPtr userData)
    {
        // notify::is-active fires on both gain and loss; check the new value and
        // only emit on gain to match the GTK3 focus-in-event semantics.
        if (!Gtk.Gtk4WindowIsActive(instance)) return;
        OnFocusInEvent(instance, IntPtr.Zero, IntPtr.Zero);
    }

    private int OnConfigureEvent(IntPtr widget, IntPtr eventPtr, IntPtr userData)
    {
        if (!_windowIds.TryGetValue(widget, out var id)) return 0;
        try
        {
            Gtk.WindowGetPosition(widget, out var x, out var y);
            Gtk.WindowGetSize(widget, out var width, out var height);
            var scale = Gtk.WidgetGetScaleFactor(widget);
            var frame = new ZeroNative.Primitives.RectF(x, y, width, height);
            _handler?.Invoke(new PlatformEvent.WindowFrameChanged(new WindowState
            {
                Id = id,
                Label = id == _primaryWindowId ? "main" : $"window-{id}",
                Title = AppInfo.AppName,
                Frame = frame,
                ScaleFactor = scale > 0 ? scale : Surface.ScaleFactor,
                Open = true,
                Focused = id == _primaryWindowId,
            }));
            if (id == _primaryWindowId)
            {
                _handler?.Invoke(new PlatformEvent.SurfaceResized(Surface with
                {
                    Size = new ZeroNative.Primitives.SizeF(width, height),
                    ScaleFactor = scale > 0 ? scale : Surface.ScaleFactor,
                }));
            }
        }
        catch
        {
            // Best effort — don't bring down the loop on a signal callback.
        }
        return 0; // continue propagation
    }

    private int OnFocusInEvent(IntPtr widget, IntPtr eventPtr, IntPtr userData)
    {
        if (!_windowIds.TryGetValue(widget, out var id)) return 0;
        try { _handler?.Invoke(new PlatformEvent.WindowFocused(id)); }
        catch { /* swallow */ }
        return 0;
    }

    private int OnDecidePolicy(IntPtr webview, IntPtr decision, int decisionType, IntPtr userData)
    {
        // We only care about navigation and new-window decisions. Let WebKit handle
        // the response policy default (decisionType == 2).
        if (decisionType != WebKit.PolicyDecisionTypeNavigationAction
            && decisionType != WebKit.PolicyDecisionTypeNewWindowAction)
            return 0;

        try
        {
            var uriPtr = WebKit.GetNavigationRequestUri(decision);
            var uri = uriPtr != IntPtr.Zero ? Marshal.PtrToStringUTF8(uriPtr) ?? "" : "";
            if (IsHostInitiatedUri(uri))
            {
                WebKit.UseDecision(decision);
                return 1;
            }

            var verdict = _policy.DecideNavigation(uri);
            switch (verdict)
            {
                case NavigationDecision.AllowInline:
                    WebKit.UseDecision(decision);
                    return 1;
                case NavigationDecision.Block:
                    WebKit.IgnoreDecision(decision);
                    return 1;
                case NavigationDecision.OpenExternally:
                    WebKit.IgnoreDecision(decision);
                    TryOpenExternally(uri);
                    return 1;
            }
        }
        catch
        {
            // Best-effort — let WebKit make its own decision if anything breaks here.
        }
        return 0;
    }

    private bool IsHostInitiatedUri(string uri)
    {
        if (string.IsNullOrEmpty(uri)) return true;
        if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return true;
        if (uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return true;
        if (uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return true;
        if (_assetScheme is { Length: > 0 } && uri.StartsWith(_assetScheme + "://", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static void TryOpenExternally(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch { /* best effort */ }
    }

    private static int OnDestroyStatic(IntPtr widget, IntPtr data)
    {
        Gtk.MainQuit();
        return 0; // GTK4 close-request: FALSE means "allow the window to close".
    }

    private void OnScriptMessage(IntPtr ucm, IntPtr jsResult, IntPtr userData)
    {
        try
        {
            var payload = WebKit.ReadJavascriptResultString(jsResult);
            if (string.IsNullOrEmpty(payload)) return;
            _handler?.Invoke(new PlatformEvent.BridgeReceived(new BridgeMessage(payload, "zero://inline", 1)));
        }
        catch
        {
            // Swallow — we never want a malformed JS payload to crash the loop.
        }
    }

    private void OnSchemeRequest(IntPtr request, IntPtr userData)
    {
        if (_assetServer is null) return;
        try
        {
            var uri = WebKit.GetUriSchemeRequestUri(request) ?? "";
            var resolved = _assetServer.Resolve(uri) ?? new AssetResponse(ReadOnlyMemory<byte>.Empty, "text/plain; charset=utf-8", 404);
            var body = resolved.Body.ToArray();
            var stream = WebKit.CreateMemoryInputStream(body);
            WebKit.FinishUriSchemeRequest(request, stream, body.LongLength, ContentTypeOnly(resolved.ContentType));
        }
        catch
        {
            // Best effort: swallow to avoid crashing the loop.
        }
    }

    private static string ContentTypeOnly(string contentType)
    {
        var idx = contentType.IndexOf(';');
        return idx < 0 ? contentType : contentType[..idx].Trim();
    }

    void IPlatformServices.LoadWindowWebView(ulong windowId, WebViewSource source)
    {
        if (_webView == IntPtr.Zero) return;
        switch (source.Kind)
        {
            case WebViewSourceKind.Html:
                WebKit.LoadHtml(_webView, source.Body, "zero://inline/");
                break;
            case WebViewSourceKind.Url:
                WebKit.LoadUri(_webView, source.Body);
                break;
            case WebViewSourceKind.Assets:
                var opt = source.AssetOptions;
                if (opt is null) return;
                ConfigureAssetSource(opt);
                var origin = (_assetScheme ?? "zero").TrimEnd('/');
                var host = ExtractHost(opt.Origin);
                WebKit.LoadUri(_webView, $"{origin}://{host}/{opt.Entry.TrimStart('/')}");
                break;
        }
    }

    private void ConfigureAssetSource(WebViewAssetSource opt)
    {
        _assetServer = new AssetServer(opt.RootPath, opt.Entry, opt.SpaFallback);
        var scheme = ExtractScheme(opt.Origin);
        _assetScheme = scheme;

        if (_registeredSchemes.Add(scheme))
        {
            var ctx = WebKit.GetWebContext(_webView);
            if (ctx != IntPtr.Zero)
            {
                var cb = Marshal.GetFunctionPointerForDelegate(_schemeRequestCallback);
                WebKit.RegisterUriScheme(ctx, scheme, cb, IntPtr.Zero);
            }
        }
    }

    private static string ExtractScheme(string origin)
    {
        var idx = origin.IndexOf("://", StringComparison.Ordinal);
        return idx < 0 ? origin : origin[..idx];
    }

    private static string ExtractHost(string origin)
    {
        var idx = origin.IndexOf("://", StringComparison.Ordinal);
        if (idx < 0) return "app";
        var rest = origin[(idx + 3)..];
        var slash = rest.IndexOf('/');
        return slash < 0 ? rest : rest[..slash];
    }

    void IPlatformServices.CompleteWindowBridge(ulong windowId, string response)
    {
        if (_webView == IntPtr.Zero) return;
        var js = $"window.__zero_native_bridge_response && window.__zero_native_bridge_response({response});";
        WebKit.RunJavaScript(_webView, js);
    }

    WindowInfo IPlatformServices.CreateWindow(WindowOptions options)
    {
        // The primary GTK window is created during InitializeGtk; secondary windows
        // currently share the existing one (multi-window WebKitGTK support is queued).
        if (!_windows.ContainsKey(options.Id) && _window != IntPtr.Zero && options.Id != _primaryWindowId)
        {
            // Defer real multi-window creation until the GTK4 path lands; surface a synthetic
            // descriptor so the runtime's window list stays consistent.
            _windows[options.Id] = _window;
        }

        return new WindowInfo
        {
            Id = options.Id,
            Label = options.Label,
            Title = options.ResolvedTitle(AppInfo.AppName),
            Frame = options.DefaultFrame,
            ScaleFactor = Surface.ScaleFactor,
            Open = true,
            Focused = false,
        };
    }

    void IPlatformServices.FocusWindow(ulong windowId)
    {
        if (_windows.TryGetValue(windowId, out var hwnd) && hwnd != IntPtr.Zero)
            Gtk.WindowPresent(hwnd);
    }

    void IPlatformServices.CloseWindow(ulong windowId)
    {
        if (_windows.TryGetValue(windowId, out var hwnd) && hwnd != IntPtr.Zero && hwnd != _window)
        {
            Gtk.WidgetDestroy(hwnd);
            _windowIds.Remove(hwnd);
            _windows.Remove(windowId);
            return;
        }
        // Closing the primary window quits the loop, matching the AppShutdown path.
        Gtk.MainQuit();
    }

    void IPlatformServices.SetWindowFrame(ulong windowId, Primitives.RectF frame)
    {
        var hwnd = _windows.TryGetValue(windowId, out var w) ? w
                 : windowId == _primaryWindowId ? _window
                 : IntPtr.Zero;
        if (hwnd == IntPtr.Zero) throw new WindowNotFoundException();
        Gtk.WindowResize(hwnd, (int)frame.Width, (int)frame.Height);
        Gtk.WindowMove(hwnd, (int)frame.X, (int)frame.Y);
    }

    void IPlatformServices.EmitWindowEvent(ulong windowId, string eventName, string detailJson)
    {
        if (_webView == IntPtr.Zero) return;
        var safeName = System.Text.Json.JsonSerializer.Serialize(eventName);
        var js = $"window.dispatchEvent(new CustomEvent({safeName}, {{ detail: {detailJson} }}));";
        WebKit.RunJavaScript(_webView, js);
    }

    OpenDialogResult IPlatformServices.ShowOpenDialog(OpenDialogOptions options)
        => Gtk.IsGtk4 ? Gtk4Dialogs.ShowOpen(_window, options) : GtkDialogs.ShowOpen(_window, options);

    string? IPlatformServices.ShowSaveDialog(SaveDialogOptions options)
        => Gtk.IsGtk4 ? Gtk4Dialogs.ShowSave(_window, options) : GtkDialogs.ShowSave(_window, options);

    MessageDialogResult IPlatformServices.ShowMessageDialog(MessageDialogOptions options)
        => Gtk.IsGtk4 ? Gtk4Dialogs.ShowMessage(_window, options) : GtkDialogs.ShowMessage(_window, options);

    void IPlatformServices.CreateTray(TrayOptions options)
    {
        _tray ??= new AyatanaIndicator(id => _handler?.Invoke(new PlatformEvent.TrayAction(id)));
        if (!_tray.TryInstall(options, AppInfo.BundleId))
        {
            // Library not present (or symbol missing): degrade silently.
            _tray.Dispose();
            _tray = null;
            throw new UnsupportedServiceException("Tray requires libayatana-appindicator3-1 on Linux");
        }
    }

    void IPlatformServices.UpdateTrayMenu(IReadOnlyList<TrayMenuItem> items)
        => _tray?.UpdateMenu(items);

    void IPlatformServices.RemoveTray()
    {
        _tray?.Dispose();
        _tray = null;
    }

    void IPlatformServices.ConfigureSecurityPolicy(SecurityPolicy policy) => _policy = policy;

    string IPlatformServices.ReadClipboard()
    {
        if (Gtk.IsGtk4) return Gtk4ClipboardRead();
        var atom = Gtk.AtomIntern("CLIPBOARD", false);
        var clipboard = Gtk.ClipboardGet(atom);
        if (clipboard == IntPtr.Zero) return "";
        var ptr = Gtk.ClipboardWaitForText(clipboard);
        if (ptr == IntPtr.Zero) return "";
        try { return Marshal.PtrToStringUTF8(ptr) ?? ""; }
        finally { Gtk.GFree(ptr); }
    }

    void IPlatformServices.WriteClipboard(string text)
    {
        if (Gtk.IsGtk4)
        {
            var clip4 = Gtk4DefaultClipboard();
            if (clip4 == IntPtr.Zero) throw new UnsupportedServiceException("Clipboard requires a default GdkDisplay");
            Gtk.Gdk4ClipboardSetText(clip4, text);
            return;
        }
        var atom = Gtk.AtomIntern("CLIPBOARD", false);
        var clipboard = Gtk.ClipboardGet(atom);
        if (clipboard == IntPtr.Zero) return;
        Gtk.ClipboardSetText(clipboard, text, -1);
        Gtk.ClipboardStore(clipboard);
    }

    /// <summary>
    /// Resolves the default GTK4 clipboard via the (folded-in) GDK display getter.
    /// </summary>
    private static IntPtr Gtk4DefaultClipboard()
    {
        var display = Gtk.Gdk4DisplayGetDefault();
        return display == IntPtr.Zero ? IntPtr.Zero : Gtk.Gdk4DisplayGetClipboard(display);
    }

    // GTK4 GdkClipboard.read_text is async-only. We drive a nested main-context
    // iteration loop until the GAsyncReadyCallback fires, then call _finish to pull
    // the string out. The callback writes the result into a GCHandle-pinned record.
    private sealed class Gtk4ClipReadState
    {
        public bool Done;
        public string? Text;
    }

    private delegate void GAsyncReadyCallback(IntPtr source, IntPtr result, IntPtr userData);
    private static readonly GAsyncReadyCallback _gtk4ClipReadDone = OnGtk4ClipReadDone;

    private static string Gtk4ClipboardRead()
    {
        var clipboard = Gtk4DefaultClipboard();
        if (clipboard == IntPtr.Zero) return "";

        var state = new Gtk4ClipReadState();
        var handle = GCHandle.Alloc(state);
        try
        {
            var cb = Marshal.GetFunctionPointerForDelegate(_gtk4ClipReadDone);
            Gtk.Gdk4ClipboardReadTextAsync(clipboard, IntPtr.Zero, cb, GCHandle.ToIntPtr(handle));
            // Pump the default main context until the callback flips Done.
            while (!state.Done) Gtk.MainContextIteration(IntPtr.Zero, true);
            return state.Text ?? "";
        }
        finally
        {
            handle.Free();
        }
    }

    private static void OnGtk4ClipReadDone(IntPtr source, IntPtr result, IntPtr userData)
    {
        Gtk4ClipReadState? state = null;
        try
        {
            var handle = GCHandle.FromIntPtr(userData);
            state = handle.Target as Gtk4ClipReadState;
            if (state is null) return;
            var ptr = Gtk.Gdk4ClipboardReadTextFinish(source, result, IntPtr.Zero);
            if (ptr != IntPtr.Zero)
            {
                state.Text = Marshal.PtrToStringUTF8(ptr);
                Gtk.GFree(ptr);
            }
        }
        catch
        {
            // Best-effort — the caller's loop only checks the Done flag.
        }
        finally
        {
            if (state is not null) state.Done = true;
        }
    }

    byte[] IPlatformServices.ReadClipboardImage()
    {
        if (Gtk.IsGtk4) throw new UnsupportedServiceException("Image clipboard access on GTK4 requires the GdkClipboard ContentProvider binding (not yet ported)");
        var atom = Gtk.AtomIntern("CLIPBOARD", false);
        var clipboard = Gtk.ClipboardGet(atom);
        if (clipboard == IntPtr.Zero) return Array.Empty<byte>();
        var pixbuf = Gtk.ClipboardWaitForImage(clipboard);
        if (pixbuf == IntPtr.Zero) return Array.Empty<byte>();
        try
        {
            if (!Gtk.GdkPixbufSaveToBuffer(pixbuf, out var buf, out var size, "png", IntPtr.Zero, IntPtr.Zero) || buf == IntPtr.Zero)
                return Array.Empty<byte>();
            try
            {
                var bytes = new byte[(int)size];
                Marshal.Copy(buf, bytes, 0, bytes.Length);
                return bytes;
            }
            finally { Gtk.GFree(buf); }
        }
        finally { Gtk.GObjectUnref(pixbuf); }
    }

    void IPlatformServices.WriteClipboardImage(ReadOnlySpan<byte> bytes)
    {
        if (Gtk.IsGtk4) throw new UnsupportedServiceException("Image clipboard access on GTK4 requires the GdkClipboard ContentProvider binding (not yet ported)");
        if (bytes.IsEmpty) return;
        var loader = Gtk.GdkPixbufLoaderNew();
        if (loader == IntPtr.Zero) throw new UnsupportedServiceException("gdk_pixbuf_loader_new failed");

        IntPtr buf = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            unsafe
            {
                fixed (byte* src = bytes)
                    Buffer.MemoryCopy(src, (void*)buf, bytes.Length, bytes.Length);
            }
            if (!Gtk.GdkPixbufLoaderWrite(loader, buf, (UIntPtr)(uint)bytes.Length, IntPtr.Zero))
                throw new UnsupportedServiceException("gdk_pixbuf_loader_write failed (unrecognized image format?)");
            if (!Gtk.GdkPixbufLoaderClose(loader, IntPtr.Zero))
                throw new UnsupportedServiceException("gdk_pixbuf_loader_close failed");

            var pixbuf = Gtk.GdkPixbufLoaderGetPixbuf(loader);
            if (pixbuf == IntPtr.Zero) throw new UnsupportedServiceException("decoded pixbuf was null");

            var atom = Gtk.AtomIntern("CLIPBOARD", false);
            var clipboard = Gtk.ClipboardGet(atom);
            if (clipboard == IntPtr.Zero) return;
            Gtk.ClipboardSetImage(clipboard, pixbuf);
            Gtk.ClipboardStore(clipboard);
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
            Gtk.GObjectUnref(loader);
        }
    }

    IReadOnlyList<string> IPlatformServices.ReadClipboardFiles()
    {
        if (Gtk.IsGtk4) throw new UnsupportedServiceException("Clipboard access on GTK4 requires the GdkClipboard binding (not yet ported)");
        var atom = Gtk.AtomIntern("CLIPBOARD", false);
        var clipboard = Gtk.ClipboardGet(atom);
        if (clipboard == IntPtr.Zero) return Array.Empty<string>();
        var arr = Gtk.ClipboardWaitForUris(clipboard);
        if (arr == IntPtr.Zero) return Array.Empty<string>();
        try
        {
            var paths = new List<string>();
            unsafe
            {
                var p = (IntPtr*)arr;
                while (*p != IntPtr.Zero)
                {
                    var uri = Marshal.PtrToStringUTF8(*p) ?? "";
                    if (uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                    {
                        try { paths.Add(Uri.UnescapeDataString(new Uri(uri).LocalPath)); }
                        catch { paths.Add(uri); }
                    }
                    else if (uri.Length > 0)
                    {
                        paths.Add(uri);
                    }
                    p++;
                }
            }
            return paths;
        }
        finally { Gtk.StringArrayFree(arr); }
    }
}
