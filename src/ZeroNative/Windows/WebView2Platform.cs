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
    private ulong _primaryWindowId = 1;
    private CoreWebView2Environment? _environment;
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _webView;
    private Action<PlatformEvent>? _handler;
    private SecurityPolicy _policy = new();
    private AssetServer? _assetServer;
    private string? _assetOrigin;
    private float _scaleFactor = 1f;
    private readonly HashSet<string> _assetFilters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ulong, IntPtr> _windowsById = new();
    private readonly Dictionary<IntPtr, ulong> _windowIdsByHwnd = new();
    private readonly Dictionary<ulong, string> _windowLabels = new();
    private readonly Dictionary<ulong, string> _windowTitles = new();
    private Win32Tray? _tray;

    public WebView2Platform(AppInfo appInfo, Surface surface)
    {
        AppInfo = appInfo;
        Surface = surface;
    }

    public void Run(Action<PlatformEvent> handler)
    {
        _handler = handler;

        var window = AppInfo.ResolvedStartupWindow(0);
        _primaryWindowId = window.Id;
        _hwnd = Win32.CreateTopLevelWindow(
            window.ResolvedTitle(AppInfo.AppName),
            (int)window.DefaultFrame.Width,
            (int)window.DefaultFrame.Height,
            (int)window.DefaultFrame.X,
            (int)window.DefaultFrame.Y,
            primary: true);
        _windowsById[window.Id] = _hwnd;
        _windowIdsByHwnd[_hwnd] = window.Id;
        _windowLabels[window.Id] = window.Label;
        _windowTitles[window.Id] = window.ResolvedTitle(AppInfo.AppName);
        _scaleFactor = Win32.GetWindowScaleFactor(_hwnd);

        Win32.RegisterCallbacks(_hwnd, new Win32.WindowCallbacks(
            OnResize: OnResize,
            OnMove: OnMove,
            OnActivate: OnActivate,
            OnDpiChanged: OnDpiChanged,
            OnTrayMessage: lp => _tray?.HandleTrayMessage(lp)));

        handler(new PlatformEvent.AppStart());
        handler(new PlatformEvent.SurfaceResized(Surface with { ScaleFactor = _scaleFactor }));
        handler(new PlatformEvent.WindowFrameChanged(new WindowState
        {
            Id = window.Id,
            Label = window.Label,
            Title = window.ResolvedTitle(AppInfo.AppName),
            Frame = window.DefaultFrame,
            ScaleFactor = _scaleFactor,
            Open = true,
            Focused = true,
        }));

        InitializeWebView2Async(_hwnd).GetAwaiter().GetResult();
        Win32.RunMessageLoop();

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
            ScaleFactor = _scaleFactor,
        }));
        EmitWindowFrame(_hwnd);
    }

    private void OnMove(int x, int y) => EmitWindowFrame(_hwnd);

    private void OnActivate(bool activated)
    {
        if (!_windowIdsByHwnd.TryGetValue(_hwnd, out var id)) return;
        if (activated) _handler?.Invoke(new PlatformEvent.WindowFocused(id));
    }

    private void OnDpiChanged(uint dpi)
    {
        _scaleFactor = dpi == 0 ? 1f : dpi / 96f;
        _handler?.Invoke(new PlatformEvent.SurfaceResized(Surface with
        {
            ScaleFactor = _scaleFactor,
        }));
        EmitWindowFrame(_hwnd);
    }

    private void EmitWindowFrame(IntPtr hwnd)
    {
        if (!_windowIdsByHwnd.TryGetValue(hwnd, out var id)) return;
        var (x, y, w, h) = Win32.GetWindowFrame(hwnd);
        _handler?.Invoke(new PlatformEvent.WindowFrameChanged(new WindowState
        {
            Id = id,
            Label = _windowLabels.TryGetValue(id, out var label) ? label : "main",
            Title = _windowTitles.TryGetValue(id, out var title) ? title : AppInfo.AppName,
            Frame = new ZeroNative.Primitives.RectF(x, y, w, h),
            ScaleFactor = _scaleFactor,
            Open = true,
            Focused = id == _primaryWindowId,
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
    {
        if (_windowsById.ContainsKey(options.Id))
            throw new DuplicateWindowException("duplicate window id");

        var title = options.ResolvedTitle(AppInfo.AppName);
        var hwnd = Win32.CreateTopLevelWindow(
            title,
            (int)options.DefaultFrame.Width,
            (int)options.DefaultFrame.Height,
            (int)options.DefaultFrame.X,
            (int)options.DefaultFrame.Y,
            primary: false);
        _windowsById[options.Id] = hwnd;
        _windowIdsByHwnd[hwnd] = options.Id;
        _windowLabels[options.Id] = options.Label;
        _windowTitles[options.Id] = title;
        var scale = Win32.GetWindowScaleFactor(hwnd);

        Win32.RegisterCallbacks(hwnd, new Win32.WindowCallbacks(
            OnClose: () => _handler?.Invoke(new PlatformEvent.WindowFrameChanged(new WindowState
            {
                Id = options.Id,
                Label = options.Label,
                Title = title,
                Frame = options.DefaultFrame,
                ScaleFactor = scale,
                Open = false,
                Focused = false,
            }))));

        return new WindowInfo
        {
            Id = options.Id,
            Label = options.Label,
            Title = title,
            Frame = options.DefaultFrame,
            ScaleFactor = scale,
            Open = true,
            Focused = false,
        };
    }

    void IPlatformServices.FocusWindow(ulong windowId)
    {
        if (_windowsById.TryGetValue(windowId, out var hwnd))
            Win32.FocusWindow(hwnd);
    }

    void IPlatformServices.CloseWindow(ulong windowId)
    {
        if (_windowsById.TryGetValue(windowId, out var hwnd))
        {
            Win32.CloseWindow(hwnd);
            _windowsById.Remove(windowId);
            _windowIdsByHwnd.Remove(hwnd);
        }
    }

    void IPlatformServices.SetWindowFrame(ulong windowId, Primitives.RectF frame)
    {
        if (!_windowsById.TryGetValue(windowId, out var hwnd))
            throw new WindowNotFoundException();
        Win32.SetWindowFrame(hwnd, (int)frame.X, (int)frame.Y, (int)frame.Width, (int)frame.Height);
    }

    OpenDialogResult IPlatformServices.ShowOpenDialog(OpenDialogOptions options) => Win32ShellDialogs.ShowOpen(_hwnd, options);
    string? IPlatformServices.ShowSaveDialog(SaveDialogOptions options) => Win32ShellDialogs.ShowSave(_hwnd, options);
    MessageDialogResult IPlatformServices.ShowMessageDialog(MessageDialogOptions options) => Win32Dialogs.ShowMessage(_hwnd, options);

    void IPlatformServices.CreateTray(TrayOptions options)
    {
        _tray ??= new Win32Tray(_hwnd, id => _handler?.Invoke(new PlatformEvent.TrayAction(id)));
        _tray.Install(options);
    }

    void IPlatformServices.UpdateTrayMenu(IReadOnlyList<TrayMenuItem> items)
        => _tray?.UpdateMenu(items);

    void IPlatformServices.RemoveTray()
    {
        _tray?.Remove();
        _tray = null;
    }

    string IPlatformServices.ReadClipboard() => Win32Clipboard.ReadText();
    void IPlatformServices.WriteClipboard(string text) => Win32Clipboard.WriteText(text);
    IReadOnlyList<string> IPlatformServices.ReadClipboardFiles() => Win32Clipboard.ReadFiles();
    void IPlatformServices.WriteClipboardFiles(IReadOnlyList<string> paths) => Win32Clipboard.WriteFiles(paths);
    byte[] IPlatformServices.ReadClipboardImage() => Win32Clipboard.ReadImage();
    void IPlatformServices.WriteClipboardImage(ReadOnlySpan<byte> bytes) => Win32Clipboard.WriteImage(bytes);

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
