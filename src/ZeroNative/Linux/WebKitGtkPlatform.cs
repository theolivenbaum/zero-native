using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ZeroNative.Bridge;
using ZeroNative.Platform;

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
    private static readonly Action<IntPtr, IntPtr> _destroyCallback = OnDestroyStatic;
    // The script-message handler is delivered as a GObject signal callback.
    // Keep the delegate alive in a field so the GC doesn't reclaim it while GTK holds the function pointer.
    private readonly Action<IntPtr, IntPtr, IntPtr> _scriptMessageCallback;
    private static WebKitGtkPlatform? _activeInstance;

    public WebKitGtkPlatform(AppInfo appInfo, Surface surface)
    {
        AppInfo = appInfo;
        Surface = surface;
        _scriptMessageCallback = OnScriptMessage;
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
                var entry = Path.GetFullPath(Path.Combine(opt.RootPath, opt.Entry));
                WebKit.LoadUri(_webView, "file://" + entry);
                break;
        }
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
}
