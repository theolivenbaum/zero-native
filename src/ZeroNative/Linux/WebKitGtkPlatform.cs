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

    private static readonly Action<IntPtr, IntPtr> _destroyCallback = OnDestroyStatic;
    // The script-message handler is delivered as a GObject signal callback.
    // Keep the delegate alive in a field so the GC doesn't reclaim it while GTK holds the function pointer.
    private readonly Action<IntPtr, IntPtr, IntPtr> _scriptMessageCallback;
    private readonly Action<IntPtr, IntPtr> _schemeRequestCallback;
    private static WebKitGtkPlatform? _activeInstance;

    public WebKitGtkPlatform(AppInfo appInfo, Surface surface)
    {
        AppInfo = appInfo;
        Surface = surface;
        _scriptMessageCallback = OnScriptMessage;
        _schemeRequestCallback = OnSchemeRequest;
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
        _window = Gtk.WindowNew(Gtk.GtkWindowToplevel);
        Gtk.WindowSetTitle(_window, window.ResolvedTitle(AppInfo.AppName));
        Gtk.WindowSetDefaultSize(_window, (int)window.DefaultFrame.Width, (int)window.DefaultFrame.Height);

        _webView = WebKit.WebViewNew();
        Gtk.ContainerAdd(_window, _webView);

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
        => new()
        {
            Id = options.Id,
            Label = options.Label,
            Title = options.ResolvedTitle(AppInfo.AppName),
            Frame = options.DefaultFrame,
            ScaleFactor = Surface.ScaleFactor,
            Open = true,
            Focused = false,
        };

    void IPlatformServices.FocusWindow(ulong windowId) { }
    void IPlatformServices.CloseWindow(ulong windowId) => Gtk.MainQuit();
    void IPlatformServices.EmitWindowEvent(ulong windowId, string eventName, string detailJson) { }

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
}
