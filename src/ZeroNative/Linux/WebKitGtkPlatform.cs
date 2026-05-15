using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ZeroNative.Assets;
using ZeroNative.Bridge;
using ZeroNative.Platform;
using ZeroNative.Security;

namespace ZeroNative.Linux;

/// <summary>
/// Linux host using GTK3 + WebKit2GTK via P/Invoke.
/// Requires libgtk-3 and libwebkit2gtk-4.1 (or 4.0) at runtime.
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
    private delegate void SignalVoidCallback(IntPtr instance, IntPtr userData);
    private delegate void ScriptMessageCallback(IntPtr ucm, IntPtr jsResult, IntPtr userData);
    private delegate void SchemeRequestCallback(IntPtr request, IntPtr userData);
    private delegate int SignalEventCallback(IntPtr widget, IntPtr eventPtr, IntPtr userData);
    private delegate int DecidePolicyCallback(IntPtr webview, IntPtr decision, int decisionType, IntPtr userData);

    private static readonly SignalVoidCallback _destroyCallback = OnDestroyStatic;
    // Keep the delegates alive in fields so the GC doesn't reclaim them while GTK holds the function pointer.
    private readonly ScriptMessageCallback _scriptMessageCallback;
    private readonly SchemeRequestCallback _schemeRequestCallback;
    private readonly SignalEventCallback _configureCallback;
    private readonly SignalEventCallback _focusInCallback;
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
        Gtk.ContainerAdd(_window, _webView);
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

        // Connect destroy → main_quit. Keep the delegate alive via the static field.
        var destroyCb = Marshal.GetFunctionPointerForDelegate(_destroyCallback);
        Gtk.SignalConnectData(_window, "destroy", destroyCb, IntPtr.Zero, IntPtr.Zero, 0);

        // Forward configure-event (moves+resizes) and focus-in-event to the runtime.
        var configureCb = Marshal.GetFunctionPointerForDelegate(_configureCallback);
        Gtk.SignalConnectData(_window, "configure-event", configureCb, IntPtr.Zero, IntPtr.Zero, 0);
        var focusInCb = Marshal.GetFunctionPointerForDelegate(_focusInCallback);
        Gtk.SignalConnectData(_window, "focus-in-event", focusInCb, IntPtr.Zero, IntPtr.Zero, 0);
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

    private static void OnDestroyStatic(IntPtr widget, IntPtr data) => Gtk.MainQuit();

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

    void IPlatformServices.EmitWindowEvent(ulong windowId, string eventName, string detailJson)
    {
        if (_webView == IntPtr.Zero) return;
        var safeName = System.Text.Json.JsonSerializer.Serialize(eventName);
        var js = $"window.dispatchEvent(new CustomEvent({safeName}, {{ detail: {detailJson} }}));";
        WebKit.RunJavaScript(_webView, js);
    }

    OpenDialogResult IPlatformServices.ShowOpenDialog(OpenDialogOptions options)
        => GtkDialogs.ShowOpen(_window, options);

    string? IPlatformServices.ShowSaveDialog(SaveDialogOptions options)
        => GtkDialogs.ShowSave(_window, options);

    MessageDialogResult IPlatformServices.ShowMessageDialog(MessageDialogOptions options)
        => GtkDialogs.ShowMessage(_window, options);

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
        var atom = Gtk.AtomIntern("CLIPBOARD", false);
        var clipboard = Gtk.ClipboardGet(atom);
        if (clipboard == IntPtr.Zero) return;
        Gtk.ClipboardSetText(clipboard, text, -1);
        Gtk.ClipboardStore(clipboard);
    }

    IReadOnlyList<string> IPlatformServices.ReadClipboardFiles()
    {
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
