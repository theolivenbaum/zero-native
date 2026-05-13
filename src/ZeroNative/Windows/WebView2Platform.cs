#if ZERO_NATIVE_HAS_WEBVIEW2
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using ZeroNative.Platform;

namespace ZeroNative.Windows;

/// <summary>
/// Windows host using a top-level HWND + WebView2 core (no WinForms/WPF dependency).
/// Uses the CoreWebView2 controller against a bare Win32 window.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
internal sealed class WebView2Platform : IPlatform, IPlatformServices
{
    public string Name => "windows";
    public Surface Surface { get; }
    public AppInfo AppInfo { get; }
    public IPlatformServices Services => this;

    private IntPtr _hwnd;
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _webView;
    private Action<PlatformEvent>? _handler;

    public WebView2Platform(AppInfo appInfo, Surface surface)
    {
        AppInfo = appInfo;
        Surface = surface;
    }

    public void Run(Action<PlatformEvent> handler)
    {
        _handler = handler;

        var window = AppInfo.ResolvedStartupWindow(0);
        _hwnd = Win32.CreateTopLevelWindow(
            window.ResolvedTitle(AppInfo.AppName),
            (int)window.DefaultFrame.Width,
            (int)window.DefaultFrame.Height);

        handler(new PlatformEvent.AppStart());
        handler(new PlatformEvent.SurfaceResized(Surface));
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

        InitializeWebView2Async(_hwnd).GetAwaiter().GetResult();
        Win32.RunMessageLoop(_hwnd, OnResize);

        handler(new PlatformEvent.AppShutdown());
    }

    private async Task InitializeWebView2Async(IntPtr hwnd)
    {
        var dataFolder = Path.Combine(Path.GetTempPath(), "ZeroNative", AppInfo.BundleId);
        Directory.CreateDirectory(dataFolder);

        var env = await CoreWebView2Environment.CreateAsync(null, dataFolder).ConfigureAwait(false);
        _controller = await env.CreateCoreWebView2ControllerAsync(hwnd).ConfigureAwait(false);
        _webView = _controller.CoreWebView2;
        var (w, h) = Win32.GetClientSize(hwnd);
        _controller.Bounds = new System.Drawing.Rectangle(0, 0, w, h);

        _webView.WebMessageReceived += (_, e) =>
        {
            try
            {
                var msg = e.TryGetWebMessageAsString() ?? e.WebMessageAsJson;
                _handler?.Invoke(new PlatformEvent.BridgeReceived(new BridgeMessage(msg, "zero://inline", 1)));
            }
            catch { /* ignore malformed */ }
        };
    }

    private void OnResize(int width, int height)
    {
        if (_controller is null) return;
        _controller.Bounds = new System.Drawing.Rectangle(0, 0, width, height);
        _handler?.Invoke(new PlatformEvent.SurfaceResized(Surface with
        {
            Size = new ZeroNative.Primitives.SizeF(width, height),
        }));
    }

    void IPlatformServices.LoadWindowWebView(ulong windowId, WebViewSource source)
    {
        if (_webView is null) return;
        switch (source.Kind)
        {
            case WebViewSourceKind.Html:
                _webView.NavigateToString(source.Body);
                break;
            case WebViewSourceKind.Url:
                _webView.Navigate(source.Body);
                break;
            case WebViewSourceKind.Assets:
                var opt = source.AssetOptions;
                if (opt is null) return;
                var entry = Path.GetFullPath(Path.Combine(opt.RootPath, opt.Entry));
                _webView.Navigate(new Uri(entry).AbsoluteUri);
                break;
        }
    }

    void IPlatformServices.CompleteWindowBridge(ulong windowId, string response)
    {
        if (_webView is null) return;
        var js = $"window.__zero_native_bridge_response && window.__zero_native_bridge_response({response});";
        _webView.ExecuteScriptAsync(js);
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
    void IPlatformServices.CloseWindow(ulong windowId) => Win32.CloseWindow(_hwnd);
    void IPlatformServices.EmitWindowEvent(ulong windowId, string eventName, string detailJson)
    {
        if (_webView is null) return;
        var js = $"window.dispatchEvent(new CustomEvent({System.Text.Json.JsonSerializer.Serialize(eventName)}, {{ detail: {detailJson} }}));";
        _webView.ExecuteScriptAsync(js);
    }
}
#endif
