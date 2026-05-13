#if ZERO_NATIVE_HAS_WEBVIEW2
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using ZeroNative.Assets;
using ZeroNative.Bridge;
using ZeroNative.Platform;
using ZeroNative.Security;

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
    private CoreWebView2Environment? _environment;
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _webView;
    private Action<PlatformEvent>? _handler;
    private SecurityPolicy _policy = new();
    private AssetServer? _assetServer;
    private string? _assetOrigin;
    private readonly HashSet<string> _assetFilters = new(StringComparer.OrdinalIgnoreCase);

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

        _environment = await CoreWebView2Environment.CreateAsync(null, dataFolder).ConfigureAwait(false);
        _controller = await _environment.CreateCoreWebView2ControllerAsync(hwnd).ConfigureAwait(false);
        _webView = _controller.CoreWebView2;
        var (w, h) = Win32.GetClientSize(hwnd);
        _controller.Bounds = new System.Drawing.Rectangle(0, 0, w, h);

        // Inject the bridge shim before any document loads.
        await _webView.AddScriptToExecuteOnDocumentCreatedAsync(
            BridgeJavascript.Build(BridgeJavascript.Channel.ChromeWebView)).ConfigureAwait(false);

        _webView.WebMessageReceived += (_, e) =>
        {
            try
            {
                var msg = e.TryGetWebMessageAsString() ?? e.WebMessageAsJson;
                var origin = e.Source ?? "zero://inline";
                _handler?.Invoke(new PlatformEvent.BridgeReceived(new BridgeMessage(msg, origin, 1)));
            }
            catch { /* ignore malformed */ }
        };

        _webView.WebResourceRequested += OnWebResourceRequested;
        _webView.NavigationStarting += OnNavigationStarting;
        _webView.NewWindowRequested += OnNewWindowRequested;
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
                ConfigureAssetSource(opt);
                var origin = _assetOrigin!.TrimEnd('/');
                _webView.Navigate($"{origin}/{opt.Entry.TrimStart('/')}");
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

    OpenDialogResult IPlatformServices.ShowOpenDialog(OpenDialogOptions options) => Win32Dialogs.ShowOpen(_hwnd, options);
    string? IPlatformServices.ShowSaveDialog(SaveDialogOptions options) => Win32Dialogs.ShowSave(_hwnd, options);
    MessageDialogResult IPlatformServices.ShowMessageDialog(MessageDialogOptions options) => Win32Dialogs.ShowMessage(_hwnd, options);

    string IPlatformServices.ReadClipboard() => Win32Clipboard.ReadText();
    void IPlatformServices.WriteClipboard(string text) => Win32Clipboard.WriteText(text);

    void IPlatformServices.ConfigureSecurityPolicy(SecurityPolicy policy) => _policy = policy;

    void IPlatformServices.EmitWindowEvent(ulong windowId, string eventName, string detailJson)
    {
        if (_webView is null) return;
        var js = $"window.dispatchEvent(new CustomEvent({System.Text.Json.JsonSerializer.Serialize(eventName)}, {{ detail: {detailJson} }}));";
        _webView.ExecuteScriptAsync(js);
    }

    private void ConfigureAssetSource(WebViewAssetSource opt)
    {
        if (_webView is null) return;
        _assetServer = new AssetServer(opt.RootPath, opt.Entry, opt.SpaFallback);
        _assetOrigin = opt.Origin;

        var filterUri = opt.Origin.TrimEnd('/') + "/*";
        if (_assetFilters.Add(filterUri))
        {
            _webView.AddWebResourceRequestedFilter(
                filterUri,
                CoreWebView2WebResourceContext.All);
        }
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (_assetServer is null || _environment is null || _assetOrigin is null) return;
        var requestUri = e.Request.Uri;
        if (!requestUri.StartsWith(_assetOrigin, StringComparison.OrdinalIgnoreCase)) return;

        var response = _assetServer.Resolve(requestUri);
        if (response is null) return;

        var stream = new MemoryStream(response.Body.ToArray());
        var headers = $"Content-Type: {response.ContentType}\r\nAccess-Control-Allow-Origin: *";
        var reason = response.StatusCode == 200 ? "OK" : "Not Found";
        e.Response = _environment.CreateWebResourceResponse(stream, response.StatusCode, reason, headers);
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (IsHostInitiatedUri(e.Uri)) return;
        var decision = _policy.DecideNavigation(e.Uri);
        switch (decision)
        {
            case NavigationDecision.AllowInline:
                return;
            case NavigationDecision.Block:
                e.Cancel = true;
                return;
            case NavigationDecision.OpenExternally:
                e.Cancel = true;
                TryOpenExternally(e.Uri);
                return;
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (IsHostInitiatedUri(e.Uri)) return;
        var decision = _policy.DecideNavigation(e.Uri);
        switch (decision)
        {
            case NavigationDecision.AllowInline:
                return;
            case NavigationDecision.Block:
                e.Handled = true;
                return;
            case NavigationDecision.OpenExternally:
                e.Handled = true;
                TryOpenExternally(e.Uri);
                return;
        }
    }

    /// <summary>
    /// Allow URIs that the platform itself loads as the document host (data:, about:blank,
    /// file:, and the configured asset origin). These are not user-initiated navigations
    /// the security policy is intended to gate.
    /// </summary>
    private bool IsHostInitiatedUri(string uri)
    {
        if (string.IsNullOrEmpty(uri)) return true;
        if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return true;
        if (uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return true;
        if (uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return true;
        if (_assetOrigin is { Length: > 0 } && uri.StartsWith(_assetOrigin, StringComparison.OrdinalIgnoreCase)) return true;
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
        catch
        {
            // Best effort - if the shell can't open the URL there's nothing actionable from here.
        }
    }
}
#endif
