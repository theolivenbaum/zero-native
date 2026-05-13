using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ZeroNative.Platform;

namespace ZeroNative.MacOS;

/// <summary>
/// macOS host using AppKit + WKWebView via the Objective-C runtime.
/// This is a minimal viable implementation - it creates an NSApplication, opens
/// an NSWindow with a WKWebView, and runs the event loop.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class WKWebViewPlatform : IPlatform, IPlatformServices
{
    public string Name => "macos";
    public Surface Surface { get; }
    public AppInfo AppInfo { get; }
    public IPlatformServices Services => this;

    private IntPtr _app;
    private IntPtr _mainWindow;
    private IntPtr _webView;
    private Action<PlatformEvent>? _handler;

    public WKWebViewPlatform(AppInfo appInfo, Surface surface)
    {
        AppInfo = appInfo;
        Surface = surface;
    }

    public void Run(Action<PlatformEvent> handler)
    {
        _handler = handler;
        InitializeAppKit();

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

        // Hand off to AppKit run loop. App-shutdown is dispatched when the runloop returns.
        ObjC.MsgSend(_app, ObjC.Sel("run"));

        handler(new PlatformEvent.AppShutdown());
    }

    private void InitializeAppKit()
    {
        var nsAppClass = ObjC.GetClass("NSApplication");
        _app = ObjC.MsgSend(nsAppClass, ObjC.Sel("sharedApplication"));
        ObjC.MsgSend(_app, ObjC.Sel("setActivationPolicy:"), (IntPtr)0 /* NSApplicationActivationPolicyRegular */);

        var window = AppInfo.ResolvedStartupWindow(0);
        var frame = new ObjC.CGRect
        {
            X = window.DefaultFrame.X,
            Y = window.DefaultFrame.Y,
            Width = window.DefaultFrame.Width,
            Height = window.DefaultFrame.Height,
        };

        var nsWindowClass = ObjC.GetClass("NSWindow");
        var alloc = ObjC.MsgSend(nsWindowClass, ObjC.Sel("alloc"));
        const nuint titledMask = 1 | 2 | 4 | 8; // titled | closable | miniaturizable | resizable
        _mainWindow = ObjC.MsgSend_CGRect_NSUInteger(alloc, ObjC.Sel("initWithContentRect:styleMask:backing:defer:"), frame, titledMask, 2 /* NSBackingStoreBuffered */, false);

        var title = ObjC.NSString(window.ResolvedTitle(AppInfo.AppName));
        ObjC.MsgSend(_mainWindow, ObjC.Sel("setTitle:"), title);

        // Create WKWebView
        var configClass = ObjC.GetClass("WKWebViewConfiguration");
        var config = ObjC.MsgSend(ObjC.MsgSend(configClass, ObjC.Sel("alloc")), ObjC.Sel("init"));

        var wkClass = ObjC.GetClass("WKWebView");
        var wkAlloc = ObjC.MsgSend(wkClass, ObjC.Sel("alloc"));
        _webView = ObjC.MsgSend(wkAlloc, ObjC.Sel("initWithFrame:configuration:"), config);
        // setFrame:
        ObjC.MsgSend_CGRect(_webView, ObjC.Sel("setFrame:"), frame);

        ObjC.MsgSend(_mainWindow, ObjC.Sel("setContentView:"), _webView);
        ObjC.MsgSend(_mainWindow, ObjC.Sel("makeKeyAndOrderFront:"), IntPtr.Zero);
        ObjC.MsgSend(_app, ObjC.Sel("activateIgnoringOtherApps:"), (IntPtr)1);
    }

    void IPlatformServices.LoadWindowWebView(ulong windowId, WebViewSource source)
    {
        if (_webView == IntPtr.Zero) return;
        switch (source.Kind)
        {
            case WebViewSourceKind.Html:
                {
                    var html = ObjC.NSString(source.Body);
                    var nsNull = IntPtr.Zero;
                    ObjC.MsgSend(_webView, ObjC.Sel("loadHTMLString:baseURL:"), html, nsNull);
                    break;
                }
            case WebViewSourceKind.Url:
                {
                    var nsUrlClass = ObjC.GetClass("NSURL");
                    var url = ObjC.MsgSend(nsUrlClass, ObjC.Sel("URLWithString:"), ObjC.NSString(source.Body));
                    var nsUrlRequest = ObjC.GetClass("NSURLRequest");
                    var req = ObjC.MsgSend(nsUrlRequest, ObjC.Sel("requestWithURL:"), url);
                    ObjC.MsgSend(_webView, ObjC.Sel("loadRequest:"), req);
                    break;
                }
            case WebViewSourceKind.Assets:
                {
                    // Map the configured asset directory to a file URL of the entry document.
                    var opt = source.AssetOptions;
                    if (opt is null) return;
                    var entry = Path.GetFullPath(Path.Combine(opt.RootPath, opt.Entry));
                    var fileUrl = "file://" + entry;
                    var nsUrlClass = ObjC.GetClass("NSURL");
                    var url = ObjC.MsgSend(nsUrlClass, ObjC.Sel("URLWithString:"), ObjC.NSString(fileUrl));
                    var nsUrlRequest = ObjC.GetClass("NSURLRequest");
                    var req = ObjC.MsgSend(nsUrlRequest, ObjC.Sel("requestWithURL:"), url);
                    ObjC.MsgSend(_webView, ObjC.Sel("loadRequest:"), req);
                    break;
                }
        }
    }

    void IPlatformServices.CompleteWindowBridge(ulong windowId, string response)
    {
        // Evaluate the response by calling window.__zero_native_bridge_response(...)
        if (_webView == IntPtr.Zero) return;
        var js = $"window.__zero_native_bridge_response && window.__zero_native_bridge_response({response});";
        var nsJs = ObjC.NSString(js);
        ObjC.MsgSend(_webView, ObjC.Sel("evaluateJavaScript:completionHandler:"), nsJs, IntPtr.Zero);
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

    void IPlatformServices.FocusWindow(ulong windowId) { /* TODO: multi-window */ }
    void IPlatformServices.CloseWindow(ulong windowId) { /* TODO: multi-window */ }
    void IPlatformServices.EmitWindowEvent(ulong windowId, string eventName, string detailJson) { /* dispatched via JS */ }
}
