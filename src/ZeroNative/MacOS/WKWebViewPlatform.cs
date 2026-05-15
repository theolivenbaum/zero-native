using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ZeroNative.Assets;
using ZeroNative.Bridge;
using ZeroNative.Platform;
using ZeroNative.Primitives;
using ZeroNative.Security;

namespace ZeroNative.MacOS;

/// <summary>
/// macOS host: AppKit + WKWebView via the Objective-C runtime.
/// Implements a single shared NSApplication with one or more NSWindows hosting
/// WKWebViews. Custom Objective-C classes registered at startup back the
/// app/window/script-message/URL-scheme delegate protocols.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class WKWebViewPlatform : IPlatform, IPlatformServices
{
    public string Name => "macos";
    public Surface Surface { get; private set; }
    public AppInfo AppInfo { get; }
    public IPlatformServices Services => this;

    private const string AppDelegateClassName = "ZeroNativeAppDelegate";
    private const string WindowDelegateClassName = "ZeroNativeWindowDelegate";
    private const string ScriptHandlerClassName = "ZeroNativeScriptHandler";
    private const string UrlSchemeHandlerClassName = "ZeroNativeUrlSchemeHandler";

    private IntPtr _app;
    private IntPtr _appDelegate;
    private IntPtr _scriptHandler;
    private IntPtr _urlSchemeHandler;
    private IntPtr _webViewConfig;
    private Action<PlatformEvent>? _handler;
    private SecurityPolicy _policy = new();
    private AssetServer? _assetServer;
    private string? _assetScheme;
    private MacTray? _tray;
    private ulong _primaryWindowId = 1;

    private readonly Dictionary<ulong, WindowEntry> _windows = new();
    private readonly Dictionary<IntPtr, ulong> _windowIdsByDelegate = new();
    private readonly Dictionary<IntPtr, ulong> _windowIdsByNsWindow = new();
    private readonly Dictionary<IntPtr, ulong> _windowIdsByWebView = new();

    // Held delegate fields — Marshal.GetFunctionPointerForDelegate doesn't pin.
    private static AppDelegateMethod? s_appWillTerminate;
    private static AppDelegateBoolMethod? s_appShouldTerminateAfterLastWindowClosed;
    private static WindowDelegateMethod? s_windowDidResize;
    private static WindowDelegateMethod? s_windowDidMove;
    private static WindowDelegateMethod? s_windowDidBecomeKey;
    private static WindowDelegateMethod? s_windowWillClose;
    private static ScriptHandlerMethod? s_scriptDidReceive;
    private static SchemeHandlerMethod? s_startSchemeTask;
    private static SchemeHandlerMethod? s_stopSchemeTask;

    private static IntPtr s_appDelegateClass;
    private static IntPtr s_windowDelegateClass;
    private static IntPtr s_scriptHandlerClass;
    private static IntPtr s_urlSchemeHandlerClass;
    private static WKWebViewPlatform? s_activeInstance;

    private delegate void AppDelegateMethod(IntPtr self, IntPtr sel, IntPtr notification);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte AppDelegateBoolMethod(IntPtr self, IntPtr sel, IntPtr sender);
    private delegate void WindowDelegateMethod(IntPtr self, IntPtr sel, IntPtr notification);
    private delegate void ScriptHandlerMethod(IntPtr self, IntPtr sel, IntPtr userContentController, IntPtr message);
    private delegate void SchemeHandlerMethod(IntPtr self, IntPtr sel, IntPtr webView, IntPtr urlSchemeTask);

    private sealed class WindowEntry
    {
        public IntPtr NsWindow;
        public IntPtr WebView;
        public IntPtr Delegate;
        public string Label = "main";
        public string Title = string.Empty;
        public RectF Frame;
        public bool Open = true;
        public bool Focused;
        public float ScaleFactor = 1f;
    }

    public WKWebViewPlatform(AppInfo appInfo, Surface surface)
    {
        AppInfo = appInfo;
        Surface = surface;
        AppKit.EnsureFrameworksLoaded();
    }

    public void Run(Action<PlatformEvent> handler)
    {
        _handler = handler;
        s_activeInstance = this;

        EnsureObjcClasses();
        InitializeAppKit();

        var window = AppInfo.ResolvedStartupWindow(0);
        _primaryWindowId = window.Id;
        CreatePrimaryWindow(window);

        handler(new PlatformEvent.AppStart());
        handler(new PlatformEvent.SurfaceResized(Surface));
        handler(new PlatformEvent.WindowFrameChanged(BuildWindowState(window.Id)));

        // Hand off to AppKit run loop. App-shutdown is dispatched when the runloop returns.
        ObjC.MsgSend(_app, ObjC.Sel("run"));

        handler(new PlatformEvent.AppShutdown());
        s_activeInstance = null;
    }

    private void InitializeAppKit()
    {
        var nsAppClass = ObjC.GetClass("NSApplication");
        _app = ObjC.MsgSend(nsAppClass, ObjC.Sel("sharedApplication"));
        ObjC.MsgSend(_app, ObjC.Sel("setActivationPolicy:"), (IntPtr)AppKit.NSApplicationActivationPolicyRegular);

        // Install our application delegate so dock-quit / cmd-Q ends the runloop cleanly.
        _appDelegate = NewInstance(s_appDelegateClass);
        if (_appDelegate != IntPtr.Zero)
            ObjC.MsgSend(_app, ObjC.Sel("setDelegate:"), _appDelegate);

        // Pre-build a shared WKWebViewConfiguration so every window inherits the
        // same bridge shim + script-message handler + (optional) URL-scheme.
        _webViewConfig = CreateSharedConfig();
    }

    private IntPtr CreateSharedConfig()
    {
        var configClass = ObjC.GetClass("WKWebViewConfiguration");
        var config = ObjC.MsgSend(ObjC.MsgSend(configClass, ObjC.Sel("alloc")), ObjC.Sel("init"));

        var ucmClass = ObjC.GetClass("WKUserContentController");
        var ucm = ObjC.MsgSend(ObjC.MsgSend(ucmClass, ObjC.Sel("alloc")), ObjC.Sel("init"));

        // Inject the shared JS shim at document start.
        var userScriptClass = ObjC.GetClass("WKUserScript");
        var script = ObjC.MsgSend(userScriptClass, ObjC.Sel("alloc"));
        script = ObjC.MsgSend(
            script,
            ObjC.Sel("initWithSource:injectionTime:forMainFrameOnly:"),
            ObjC.NSString(BridgeJavascript.Build(BridgeJavascript.Channel.WebKitMessageHandler)),
            (IntPtr)AppKit.WKUserScriptInjectionTimeAtDocumentStart,
            IntPtr.Zero /* main frame and child frames */);
        ObjC.MsgSend(ucm, ObjC.Sel("addUserScript:"), script);

        // Register the script-message handler so JS -> .NET delivery works.
        _scriptHandler = NewInstance(s_scriptHandlerClass);
        if (_scriptHandler != IntPtr.Zero)
        {
            ObjC.MsgSend(
                ucm,
                ObjC.Sel("addScriptMessageHandler:name:"),
                _scriptHandler,
                ObjC.NSString(BridgeJavascript.HandlerName));
        }

        ObjC.MsgSend(config, ObjC.Sel("setUserContentController:"), ucm);
        return config;
    }

    private void CreatePrimaryWindow(WindowOptions options)
    {
        var entry = AllocateWindow(options);
        _windows[options.Id] = entry;
        _windowIdsByNsWindow[entry.NsWindow] = options.Id;
        _windowIdsByWebView[entry.WebView] = options.Id;
        if (entry.Delegate != IntPtr.Zero) _windowIdsByDelegate[entry.Delegate] = options.Id;

        ObjC.MsgSend(entry.NsWindow, ObjC.Sel("makeKeyAndOrderFront:"), IntPtr.Zero);
        ObjC.MsgSend(_app, ObjC.Sel("activateIgnoringOtherApps:"), (IntPtr)1);
        entry.Focused = true;
    }

    private WindowEntry AllocateWindow(WindowOptions options)
    {
        var frame = new ObjC.CGRect(
            options.DefaultFrame.X,
            options.DefaultFrame.Y,
            options.DefaultFrame.Width,
            options.DefaultFrame.Height);

        var nsWindowClass = ObjC.GetClass("NSWindow");
        var alloc = ObjC.MsgSend(nsWindowClass, ObjC.Sel("alloc"));
        var mask = AppKit.NSWindowStyleMaskTitled | AppKit.NSWindowStyleMaskClosable | AppKit.NSWindowStyleMaskMiniaturizable;
        if (options.Resizable) mask |= AppKit.NSWindowStyleMaskResizable;

        var nsWindow = ObjC.MsgSend_CGRect_NSUInteger(
            alloc,
            ObjC.Sel("initWithContentRect:styleMask:backing:defer:"),
            frame,
            mask,
            AppKit.NSBackingStoreBuffered,
            false);

        ObjC.MsgSend(nsWindow, ObjC.Sel("setTitle:"), ObjC.NSString(options.ResolvedTitle(AppInfo.AppName)));

        var wkClass = ObjC.GetClass("WKWebView");
        var wkAlloc = ObjC.MsgSend(wkClass, ObjC.Sel("alloc"));
        var webView = ObjC.MsgSend_CGRect_IntPtr(wkAlloc, ObjC.Sel("initWithFrame:configuration:"), frame, _webViewConfig);
        ObjC.MsgSend(webView, ObjC.Sel("setAutoresizingMask:"), (IntPtr)(2 | 16) /* width + height resizable */);

        ObjC.MsgSend(nsWindow, ObjC.Sel("setContentView:"), webView);

        var windowDelegate = NewInstance(s_windowDelegateClass);
        if (windowDelegate != IntPtr.Zero)
            ObjC.MsgSend(nsWindow, ObjC.Sel("setDelegate:"), windowDelegate);

        var scale = (float)ObjC.MsgSendDouble(nsWindow, ObjC.Sel("backingScaleFactor"));
        if (scale <= 0) scale = 1f;

        return new WindowEntry
        {
            NsWindow = nsWindow,
            WebView = webView,
            Delegate = windowDelegate,
            Label = options.Label,
            Title = options.ResolvedTitle(AppInfo.AppName),
            Frame = options.DefaultFrame,
            ScaleFactor = scale,
            Open = true,
        };
    }

    private WindowState BuildWindowState(ulong id)
    {
        if (!_windows.TryGetValue(id, out var entry))
        {
            return new WindowState { Id = id };
        }
        return new WindowState
        {
            Id = id,
            Label = entry.Label,
            Title = entry.Title,
            Frame = entry.Frame,
            ScaleFactor = entry.ScaleFactor,
            Open = entry.Open,
            Focused = entry.Focused,
        };
    }

    // ---- IPlatformServices ----

    void IPlatformServices.LoadWindowWebView(ulong windowId, WebViewSource source)
    {
        if (!_windows.TryGetValue(windowId, out var entry)) return;
        var webView = entry.WebView;
        switch (source.Kind)
        {
            case WebViewSourceKind.Html:
            {
                ObjC.MsgSend(webView, ObjC.Sel("loadHTMLString:baseURL:"), ObjC.NSString(source.Body), IntPtr.Zero);
                break;
            }
            case WebViewSourceKind.Url:
            {
                LoadRemoteUrl(webView, source.Body);
                break;
            }
            case WebViewSourceKind.Assets:
            {
                var opt = source.AssetOptions;
                if (opt is null) return;
                ConfigureAssetSource(opt);
                var origin = (_assetScheme ?? "zero").TrimEnd('/');
                var host = ExtractHost(opt.Origin);
                LoadRemoteUrl(webView, $"{origin}://{host}/{opt.Entry.TrimStart('/')}");
                break;
            }
        }
    }

    private static void LoadRemoteUrl(IntPtr webView, string urlString)
    {
        var nsUrlClass = ObjC.GetClass("NSURL");
        var url = ObjC.MsgSend(nsUrlClass, ObjC.Sel("URLWithString:"), ObjC.NSString(urlString));
        var nsUrlRequest = ObjC.GetClass("NSURLRequest");
        var req = ObjC.MsgSend(nsUrlRequest, ObjC.Sel("requestWithURL:"), url);
        ObjC.MsgSend(webView, ObjC.Sel("loadRequest:"), req);
    }

    void IPlatformServices.CompleteWindowBridge(ulong windowId, string response)
    {
        if (!_windows.TryGetValue(windowId, out var entry)) return;
        var js = $"window.__zero_native_bridge_response && window.__zero_native_bridge_response({response});";
        ObjC.MsgSend(entry.WebView, ObjC.Sel("evaluateJavaScript:completionHandler:"), ObjC.NSString(js), IntPtr.Zero);
    }

    WindowInfo IPlatformServices.CreateWindow(WindowOptions options)
    {
        if (_windows.ContainsKey(options.Id))
            throw new DuplicateWindowException("duplicate window id");

        var entry = AllocateWindow(options);
        _windows[options.Id] = entry;
        _windowIdsByNsWindow[entry.NsWindow] = options.Id;
        _windowIdsByWebView[entry.WebView] = options.Id;
        if (entry.Delegate != IntPtr.Zero) _windowIdsByDelegate[entry.Delegate] = options.Id;

        ObjC.MsgSend(entry.NsWindow, ObjC.Sel("makeKeyAndOrderFront:"), IntPtr.Zero);

        return new WindowInfo
        {
            Id = options.Id,
            Label = options.Label,
            Title = entry.Title,
            Frame = entry.Frame,
            ScaleFactor = entry.ScaleFactor,
            Open = true,
            Focused = false,
        };
    }

    void IPlatformServices.FocusWindow(ulong windowId)
    {
        if (!_windows.TryGetValue(windowId, out var entry)) return;
        ObjC.MsgSend(entry.NsWindow, ObjC.Sel("makeKeyAndOrderFront:"), IntPtr.Zero);
    }

    void IPlatformServices.CloseWindow(ulong windowId)
    {
        if (!_windows.TryGetValue(windowId, out var entry)) return;
        ObjC.MsgSend(entry.NsWindow, ObjC.Sel("close"));
    }

    void IPlatformServices.SetWindowFrame(ulong windowId, RectF frame)
    {
        if (!_windows.TryGetValue(windowId, out var entry))
            throw new WindowNotFoundException();
        var rect = new ObjC.CGRect(frame.X, frame.Y, frame.Width, frame.Height);
        ObjC.MsgSend_CGRect_Bool(entry.NsWindow, ObjC.Sel("setFrame:display:"), rect, true);
    }

    void IPlatformServices.EmitWindowEvent(ulong windowId, string eventName, string detailJson)
    {
        if (!_windows.TryGetValue(windowId, out var entry)) return;
        var safe = System.Text.Json.JsonSerializer.Serialize(eventName);
        var js = $"window.dispatchEvent(new CustomEvent({safe}, {{ detail: {detailJson} }}));";
        ObjC.MsgSend(entry.WebView, ObjC.Sel("evaluateJavaScript:completionHandler:"), ObjC.NSString(js), IntPtr.Zero);
    }

    void IPlatformServices.ConfigureSecurityPolicy(SecurityPolicy policy) => _policy = policy;

    OpenDialogResult IPlatformServices.ShowOpenDialog(OpenDialogOptions options) => MacDialogs.ShowOpen(options);
    string? IPlatformServices.ShowSaveDialog(SaveDialogOptions options) => MacDialogs.ShowSave(options);
    MessageDialogResult IPlatformServices.ShowMessageDialog(MessageDialogOptions options) => MacDialogs.ShowMessage(options);

    string IPlatformServices.ReadClipboard()
    {
        var pasteboardClass = ObjC.GetClass("NSPasteboard");
        var pasteboard = ObjC.MsgSend(pasteboardClass, ObjC.Sel("generalPasteboard"));
        if (pasteboard == IntPtr.Zero) return string.Empty;
        var str = ObjC.MsgSend(pasteboard, ObjC.Sel("stringForType:"), ObjC.NSString("public.utf8-plain-text"));
        return ObjC.ReadNSString(str) ?? string.Empty;
    }

    void IPlatformServices.WriteClipboard(string text)
    {
        var pasteboardClass = ObjC.GetClass("NSPasteboard");
        var pasteboard = ObjC.MsgSend(pasteboardClass, ObjC.Sel("generalPasteboard"));
        if (pasteboard == IntPtr.Zero) return;
        ObjC.MsgSend(pasteboard, ObjC.Sel("clearContents"));
        ObjC.MsgSend(pasteboard, ObjC.Sel("setString:forType:"), ObjC.NSString(text ?? string.Empty), ObjC.NSString("public.utf8-plain-text"));
    }

    IReadOnlyList<string> IPlatformServices.ReadClipboardFiles()
    {
        var pasteboardClass = ObjC.GetClass("NSPasteboard");
        var pasteboard = ObjC.MsgSend(pasteboardClass, ObjC.Sel("generalPasteboard"));
        if (pasteboard == IntPtr.Zero) return Array.Empty<string>();

        // [pb propertyListForType: @"public.file-url"] returns either an NSArray
        // (multiple URLs) or an NSString (single URL). We try both shapes.
        var fileUrlType = ObjC.NSString("public.file-url");
        var arrayProp = ObjC.MsgSend(pasteboard, ObjC.Sel("propertyListForType:"), fileUrlType);
        if (arrayProp == IntPtr.Zero)
        {
            // Fall back to NSURL pasteboard items (10.6+ style).
            var nsUrlClass = ObjC.GetClass("NSURL");
            var classesArr = NSArrayFromClasses(nsUrlClass);
            var dict = ObjC.MsgSend(ObjC.GetClass("NSDictionary"), ObjC.Sel("dictionary"));
            var urls = ObjC.MsgSend(pasteboard, ObjC.Sel("readObjectsForClasses:options:"), classesArr, dict);
            if (urls == IntPtr.Zero) return Array.Empty<string>();
            var count = (long)ObjC.MsgSendNInt(urls, ObjC.Sel("count"));
            var paths = new List<string>(checked((int)count));
            for (var i = 0; i < count; i++)
            {
                var url = ObjC.MsgSend(urls, ObjC.Sel("objectAtIndex:"), (nuint)i);
                var pathPtr = ObjC.MsgSend(url, ObjC.Sel("path"));
                if (ObjC.ReadNSString(pathPtr) is { Length: > 0 } p) paths.Add(p);
            }
            return paths;
        }
        // arrayProp could be a single NSString or an NSArray of NSStrings/NSURLs.
        if (ObjC.MsgSendBool(arrayProp, ObjC.Sel("isKindOfClass:"), ObjC.GetClass("NSArray")))
        {
            var count = (long)ObjC.MsgSendNInt(arrayProp, ObjC.Sel("count"));
            var paths = new List<string>(checked((int)count));
            for (var i = 0; i < count; i++)
            {
                var item = ObjC.MsgSend(arrayProp, ObjC.Sel("objectAtIndex:"), (nuint)i);
                var s = ObjC.ReadNSString(item);
                if (!string.IsNullOrEmpty(s)) paths.Add(NormalizeFileUrl(s));
            }
            return paths;
        }
        var single = ObjC.ReadNSString(arrayProp);
        return string.IsNullOrEmpty(single) ? Array.Empty<string>() : new[] { NormalizeFileUrl(single) };
    }

    void IPlatformServices.WriteClipboardFiles(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var pasteboardClass = ObjC.GetClass("NSPasteboard");
        var pasteboard = ObjC.MsgSend(pasteboardClass, ObjC.Sel("generalPasteboard"));
        if (pasteboard == IntPtr.Zero) return;

        ObjC.MsgSend(pasteboard, ObjC.Sel("clearContents"));
        if (paths.Count == 0) return;

        var nsUrlClass = ObjC.GetClass("NSURL");
        var mutArrClass = ObjC.GetClass("NSMutableArray");
        var arr = ObjC.MsgSend(ObjC.MsgSend(mutArrClass, ObjC.Sel("alloc")), ObjC.Sel("init"));
        foreach (var p in paths)
        {
            if (string.IsNullOrEmpty(p)) continue;
            var nsPath = ObjC.NSString(p);
            var url = ObjC.MsgSend(nsUrlClass, ObjC.Sel("fileURLWithPath:"), nsPath);
            if (url != IntPtr.Zero)
                ObjC.MsgSend(arr, ObjC.Sel("addObject:"), url);
        }
        ObjC.MsgSend(pasteboard, ObjC.Sel("writeObjects:"), arr);
    }

    byte[] IPlatformServices.ReadClipboardImage()
    {
        var pasteboardClass = ObjC.GetClass("NSPasteboard");
        var pasteboard = ObjC.MsgSend(pasteboardClass, ObjC.Sel("generalPasteboard"));
        if (pasteboard == IntPtr.Zero) return Array.Empty<byte>();

        // Prefer PNG, then TIFF for downstream consumers that don't unpack TIFF.
        foreach (var (uti, _) in new[] { ("public.png", "png"), ("public.tiff", "tiff") })
        {
            var data = ObjC.MsgSend(pasteboard, ObjC.Sel("dataForType:"), ObjC.NSString(uti));
            if (data != IntPtr.Zero)
            {
                var bytes = ObjC.ReadNSData(data);
                if (bytes.Length > 0) return bytes;
            }
        }
        return Array.Empty<byte>();
    }

    void IPlatformServices.WriteClipboardImage(ReadOnlySpan<byte> bytes)
    {
        var pasteboardClass = ObjC.GetClass("NSPasteboard");
        var pasteboard = ObjC.MsgSend(pasteboardClass, ObjC.Sel("generalPasteboard"));
        if (pasteboard == IntPtr.Zero) return;
        ObjC.MsgSend(pasteboard, ObjC.Sel("clearContents"));
        if (bytes.IsEmpty) return;

        var data = ObjC.NSData(bytes);

        // The MIME of `bytes` is unknown to us; mark as PNG when the magic matches,
        // otherwise fall back to TIFF (the AppKit default image pasteboard type).
        var isPng = bytes.Length >= 8
            && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
            && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;
        var uti = isPng ? "public.png" : "public.tiff";
        ObjC.MsgSend(pasteboard, ObjC.Sel("setData:forType:"), data, ObjC.NSString(uti));
    }

    private static IntPtr NSArrayFromClasses(params IntPtr[] classes)
    {
        var nsArrayClass = ObjC.GetClass("NSMutableArray");
        var arr = ObjC.MsgSend(ObjC.MsgSend(nsArrayClass, ObjC.Sel("alloc")), ObjC.Sel("init"));
        foreach (var c in classes) ObjC.MsgSend(arr, ObjC.Sel("addObject:"), c);
        return arr;
    }

    private static string NormalizeFileUrl(string urlOrPath)
    {
        if (urlOrPath.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            try { return Uri.UnescapeDataString(new Uri(urlOrPath).LocalPath); }
            catch { return urlOrPath; }
        }
        return urlOrPath;
    }

    void IPlatformServices.CreateTray(TrayOptions options)
    {
        _tray ??= new MacTray(id => _handler?.Invoke(new PlatformEvent.TrayAction(id)));
        _tray.Install(options);
    }

    void IPlatformServices.UpdateTrayMenu(IReadOnlyList<TrayMenuItem> items) => _tray?.UpdateMenu(items);

    void IPlatformServices.RemoveTray()
    {
        _tray?.Dispose();
        _tray = null;
    }

    // ---- Asset scheme handler ----

    private void ConfigureAssetSource(WebViewAssetSource opt)
    {
        var scheme = ExtractScheme(opt.Origin);
        _assetServer = new AssetServer(opt.RootPath, opt.Entry, opt.SpaFallback);
        _assetScheme = scheme;

        // setURLSchemeHandler:forURLScheme: must be called on a config before any
        // WKWebView is created. We make a best-effort to register; failures here
        // fall back to file:// loads.
        if (_urlSchemeHandler == IntPtr.Zero) _urlSchemeHandler = NewInstance(s_urlSchemeHandlerClass);
        if (_urlSchemeHandler != IntPtr.Zero && _webViewConfig != IntPtr.Zero)
        {
            ObjC.MsgSend(_webViewConfig, ObjC.Sel("setURLSchemeHandler:forURLScheme:"), _urlSchemeHandler, ObjC.NSString(scheme));
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

    // ---- ObjC class registration ----

    private static void EnsureObjcClasses()
    {
        if (s_appDelegateClass != IntPtr.Zero) return;

        s_appWillTerminate = OnAppWillTerminate;
        s_appShouldTerminateAfterLastWindowClosed = OnAppShouldTerminateAfterLastWindow;
        s_windowDidResize = OnWindowDidResize;
        s_windowDidMove = OnWindowDidMove;
        s_windowDidBecomeKey = OnWindowDidBecomeKey;
        s_windowWillClose = OnWindowWillClose;
        s_scriptDidReceive = OnScriptMessage;
        s_startSchemeTask = OnStartSchemeTask;
        s_stopSchemeTask = OnStopSchemeTask;

        s_appDelegateClass = new ObjcClassBuilder(AppDelegateClassName, "NSObject")
            .AddProtocol("NSApplicationDelegate")
            .AddMethod("applicationWillTerminate:", s_appWillTerminate, "v@:@")
            .AddMethod("applicationShouldTerminateAfterLastWindowClosed:", s_appShouldTerminateAfterLastWindowClosed, "c@:@")
            .Register();

        s_windowDelegateClass = new ObjcClassBuilder(WindowDelegateClassName, "NSObject")
            .AddProtocol("NSWindowDelegate")
            .AddMethod("windowDidResize:", s_windowDidResize, "v@:@")
            .AddMethod("windowDidMove:", s_windowDidMove, "v@:@")
            .AddMethod("windowDidBecomeKey:", s_windowDidBecomeKey, "v@:@")
            .AddMethod("windowWillClose:", s_windowWillClose, "v@:@")
            .Register();

        s_scriptHandlerClass = new ObjcClassBuilder(ScriptHandlerClassName, "NSObject")
            .AddProtocol("WKScriptMessageHandler")
            .AddMethod("userContentController:didReceiveScriptMessage:", s_scriptDidReceive, "v@:@@")
            .Register();

        s_urlSchemeHandlerClass = new ObjcClassBuilder(UrlSchemeHandlerClassName, "NSObject")
            .AddProtocol("WKURLSchemeHandler")
            .AddMethod("webView:startURLSchemeTask:", s_startSchemeTask, "v@:@@")
            .AddMethod("webView:stopURLSchemeTask:", s_stopSchemeTask, "v@:@@")
            .Register();
    }

    private static IntPtr NewInstance(IntPtr cls)
    {
        if (cls == IntPtr.Zero) return IntPtr.Zero;
        var alloc = ObjC.MsgSend(cls, ObjC.Sel("alloc"));
        return ObjC.MsgSend(alloc, ObjC.Sel("init"));
    }

    // ---- Delegate handlers (static so they're directly addressable) ----

    private static void OnAppWillTerminate(IntPtr self, IntPtr sel, IntPtr notification)
    {
        var instance = s_activeInstance;
        instance?._handler?.Invoke(new PlatformEvent.AppShutdown());
    }

    private static byte OnAppShouldTerminateAfterLastWindow(IntPtr self, IntPtr sel, IntPtr sender)
    {
        // YES: when the last window closes, quit the app so the runloop ends and
        // the runtime can publish AppShutdown.
        return 1;
    }

    private static void OnWindowDidResize(IntPtr self, IntPtr sel, IntPtr notification)
    {
        var instance = s_activeInstance;
        if (instance is null) return;
        if (!instance._windowIdsByDelegate.TryGetValue(self, out var id)) return;
        if (!instance._windows.TryGetValue(id, out var entry)) return;
        UpdateEntryFrame(instance, entry, id);
        instance._handler?.Invoke(new PlatformEvent.WindowFrameChanged(instance.BuildWindowState(id)));
        if (id == instance._primaryWindowId)
        {
            instance.Surface = instance.Surface with
            {
                Size = new SizeF((float)entry.Frame.Width, (float)entry.Frame.Height),
                ScaleFactor = entry.ScaleFactor,
            };
            instance._handler?.Invoke(new PlatformEvent.SurfaceResized(instance.Surface));
        }
    }

    private static void OnWindowDidMove(IntPtr self, IntPtr sel, IntPtr notification)
    {
        var instance = s_activeInstance;
        if (instance is null) return;
        if (!instance._windowIdsByDelegate.TryGetValue(self, out var id)) return;
        if (!instance._windows.TryGetValue(id, out var entry)) return;
        UpdateEntryFrame(instance, entry, id);
        instance._handler?.Invoke(new PlatformEvent.WindowFrameChanged(instance.BuildWindowState(id)));
    }

    private static void UpdateEntryFrame(WKWebViewPlatform instance, WindowEntry entry, ulong id)
    {
        if (entry.NsWindow == IntPtr.Zero) return;
        var frame = ObjC.MsgSend_RetCGRect(entry.NsWindow, ObjC.Sel("frame"));
        var scale = (float)ObjC.MsgSendDouble(entry.NsWindow, ObjC.Sel("backingScaleFactor"));
        entry.Frame = new RectF((float)frame.X, (float)frame.Y, (float)frame.Width, (float)frame.Height);
        if (scale > 0) entry.ScaleFactor = scale;
    }

    private static void OnWindowDidBecomeKey(IntPtr self, IntPtr sel, IntPtr notification)
    {
        var instance = s_activeInstance;
        if (instance is null) return;
        if (!instance._windowIdsByDelegate.TryGetValue(self, out var id)) return;
        foreach (var kv in instance._windows) kv.Value.Focused = kv.Key == id;
        instance._handler?.Invoke(new PlatformEvent.WindowFocused(id));
    }

    private static void OnWindowWillClose(IntPtr self, IntPtr sel, IntPtr notification)
    {
        var instance = s_activeInstance;
        if (instance is null) return;
        if (!instance._windowIdsByDelegate.TryGetValue(self, out var id)) return;
        if (instance._windows.TryGetValue(id, out var entry))
        {
            entry.Open = false;
            entry.Focused = false;
            instance._handler?.Invoke(new PlatformEvent.WindowFrameChanged(instance.BuildWindowState(id)));
            instance._windowIdsByNsWindow.Remove(entry.NsWindow);
            instance._windowIdsByWebView.Remove(entry.WebView);
            instance._windowIdsByDelegate.Remove(self);
            instance._windows.Remove(id);
        }
        if (id == instance._primaryWindowId || instance._windows.Count == 0)
            ObjC.MsgSend(instance._app, ObjC.Sel("terminate:"), IntPtr.Zero);
    }

    private static void OnScriptMessage(IntPtr self, IntPtr sel, IntPtr userContentController, IntPtr message)
    {
        var instance = s_activeInstance;
        if (instance is null) return;
        try
        {
            // WKScriptMessage's body is the JS value. We post strings; coerce
            // to string and forward to the runtime.
            var body = ObjC.MsgSend(message, ObjC.Sel("body"));
            var payload = ObjC.ReadNSString(body);
            if (string.IsNullOrEmpty(payload)) return;

            // Resolve the originating window so multi-window bridge responses route
            // back through the right WKWebView.
            var webView = ObjC.MsgSend(message, ObjC.Sel("webView"));
            ulong windowId = instance._primaryWindowId;
            if (instance._windowIdsByWebView.TryGetValue(webView, out var resolved))
                windowId = resolved;

            var origin = "zero://inline";
            var frameInfo = ObjC.MsgSend(message, ObjC.Sel("frameInfo"));
            if (frameInfo != IntPtr.Zero)
            {
                var request = ObjC.MsgSend(frameInfo, ObjC.Sel("request"));
                var url = ObjC.MsgSend(request, ObjC.Sel("URL"));
                var urlStr = ObjC.ReadNSUrl(url);
                if (!string.IsNullOrEmpty(urlStr)) origin = urlStr;
            }

            instance._handler?.Invoke(new PlatformEvent.BridgeReceived(new BridgeMessage(payload, origin, windowId)));
        }
        catch
        {
            // Swallow — never bring down the AppKit run loop on a malformed payload.
        }
    }

    private static void OnStartSchemeTask(IntPtr self, IntPtr sel, IntPtr webView, IntPtr task)
    {
        var instance = s_activeInstance;
        if (instance is null || instance._assetServer is null) return;

        try
        {
            var request = ObjC.MsgSend(task, ObjC.Sel("request"));
            var url = ObjC.MsgSend(request, ObjC.Sel("URL"));
            var urlStr = ObjC.ReadNSUrl(url) ?? string.Empty;

            var resolved = instance._assetServer.Resolve(urlStr)
                ?? new AssetResponse(ReadOnlyMemory<byte>.Empty, "text/plain; charset=utf-8", 404);
            var body = resolved.Body.ToArray();

            // Build an NSHTTPURLResponse with the correct status and content type.
            var headersClass = ObjC.GetClass("NSMutableDictionary");
            var headers = ObjC.MsgSend(ObjC.MsgSend(headersClass, ObjC.Sel("alloc")), ObjC.Sel("init"));
            ObjC.MsgSend(headers, ObjC.Sel("setObject:forKey:"), ObjC.NSString(resolved.ContentType), ObjC.NSString("Content-Type"));
            ObjC.MsgSend(headers, ObjC.Sel("setObject:forKey:"), ObjC.NSString(body.LongLength.ToString()), ObjC.NSString("Content-Length"));
            ObjC.MsgSend(headers, ObjC.Sel("setObject:forKey:"), ObjC.NSString("*"), ObjC.NSString("Access-Control-Allow-Origin"));

            var responseClass = ObjC.GetClass("NSHTTPURLResponse");
            var responseAlloc = ObjC.MsgSend(responseClass, ObjC.Sel("alloc"));
            var response = ObjC.MsgSend(
                responseAlloc,
                ObjC.Sel("initWithURL:statusCode:HTTPVersion:headerFields:"),
                url,
                (IntPtr)resolved.StatusCode,
                ObjC.NSString("HTTP/1.1"),
                headers);

            ObjC.MsgSend(task, ObjC.Sel("didReceiveResponse:"), response);
            if (body.Length > 0)
            {
                var data = ObjC.NSData(body);
                ObjC.MsgSend(task, ObjC.Sel("didReceiveData:"), data);
            }
            ObjC.MsgSend(task, ObjC.Sel("didFinish"));
        }
        catch
        {
            // Failure path: attempt a didFailWithError: so WebKit doesn't hang.
            try { ObjC.MsgSend(task, ObjC.Sel("didFinish")); } catch { /* swallow */ }
        }
    }

    private static void OnStopSchemeTask(IntPtr self, IntPtr sel, IntPtr webView, IntPtr task)
    {
        // We answer synchronously in OnStartSchemeTask so there's nothing to cancel.
    }
}
