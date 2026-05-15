using System.Diagnostics;
using System.Runtime.InteropServices;
using Xilium.CefGlue;
using Xilium.CefGlue.Common;
using ZeroNative.Assets;
using ZeroNative.Bridge;
using ZeroNative.Platform;
using ZeroNative.Primitives;
using ZeroNative.Security;

namespace ZeroNative.Cef;

public sealed class CefPlatformOptions
{
    /// <summary>
    /// Optional path to a CEF distribution. If null, CefGlue.Common's default search
    /// (the application's runtimes folder, populated by the package) is used.
    /// </summary>
    public string? CefDirectory { get; init; }

    /// <summary>Browser cache directory. Defaults to a per-app folder under temp.</summary>
    public string? CachePath { get; init; }

    public bool MultiThreadedMessageLoop { get; init; }
    public bool ExternalMessagePump { get; init; }
    public string? UserAgent { get; init; }

    /// <summary>Extra Chromium command-line switches.</summary>
    public IReadOnlyDictionary<string, string>? CommandLineSwitches { get; init; }

    /// <summary>
    /// Custom scheme used to serve <see cref="WebViewSource.Assets"/>. The matching origin
    /// (<see cref="WebViewAssetSource.Origin"/>) must use this scheme. Defaults to "zero".
    /// </summary>
    public string AssetScheme { get; init; } = "zero";
}

/// <summary>
/// Platform implementation that hosts the application UI inside Chromium via CefGlue.
/// CEF owns the top-level browser window through a popup window info — CefGlue handles
/// the per-OS hosting (Win32 / GTK / Cocoa) automatically.
/// </summary>
public sealed class CefPlatform : IPlatform, IPlatformServices, IDisposable
{
    public string Name => "cef";
    public Surface Surface { get; }
    public AppInfo AppInfo { get; }
    public IPlatformServices Services => this;
    public CefPlatformOptions Options { get; }

    private CefBrowser? _primaryBrowser;
    private Action<PlatformEvent>? _handler;
    private bool _initialized;
    private SecurityPolicy _policy = new();
    private AssetServer? _assetServer;
    private string? _assetOrigin;
    private readonly ZeroSchemeHandlerFactory _schemeFactory;
    private readonly Dictionary<ulong, CefBrowser> _browsers = new();
    private readonly Dictionary<int, ulong> _windowIdByBrowserIdentifier = new();
    private ulong _primaryWindowId = 1;

    public CefPlatform(AppInfo appInfo, Surface surface, CefPlatformOptions options)
    {
        AppInfo = appInfo;
        Surface = surface;
        Options = options;
        _schemeFactory = new ZeroSchemeHandlerFactory(() => _assetServer);
    }

    public void Run(Action<PlatformEvent> handler)
    {
        _handler = handler;
        EnsureInitialized();

        handler(new PlatformEvent.AppStart());
        handler(new PlatformEvent.SurfaceResized(Surface));

        var window = AppInfo.ResolvedStartupWindow(0);
        _primaryWindowId = window.Id;
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

        try
        {
            CefRuntime.RunMessageLoop();
        }
        finally
        {
            try { CefRuntime.Shutdown(); } catch { /* idempotent */ }
        }

        handler(new PlatformEvent.AppShutdown());
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;

        if (!string.IsNullOrEmpty(Options.CefDirectory))
            CefRuntime.Load(Options.CefDirectory);

        var settings = new CefSettings
        {
            NoSandbox = true,
            CachePath = Options.CachePath ?? Path.Combine(Path.GetTempPath(), "ZeroNative.Cef", AppInfo.BundleId),
            MultiThreadedMessageLoop = Options.MultiThreadedMessageLoop,
            ExternalMessagePump = Options.ExternalMessagePump,
            UserAgent = Options.UserAgent ?? $"ZeroNative.Cef/{AppInfo.AppName}",
        };
        Directory.CreateDirectory(settings.CachePath);

        var customSchemes = new[]
        {
            new Xilium.CefGlue.Common.Shared.CustomScheme
            {
                SchemeName = Options.AssetScheme,
                IsStandard = true,
                IsSecure = true,
                IsCorsEnabled = true,
                IsFetchEnabled = true,
                SchemeHandlerFactory = _schemeFactory,
            },
        };
        var switches = (Options.CommandLineSwitches ?? new Dictionary<string, string>())
            .Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value))
            .ToArray();

        CefRuntimeLoader.Initialize(settings, switches, customSchemes);

        _initialized = true;
    }

    void IPlatformServices.LoadWindowWebView(ulong windowId, WebViewSource source)
    {
        EnsureInitialized();
        var url = source.Kind switch
        {
            WebViewSourceKind.Html
                => "data:text/html;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(source.Body)),
            WebViewSourceKind.Url => source.Body,
            WebViewSourceKind.Assets when source.AssetOptions is { } o
                => ConfigureAssetSource(o),
            _ => source.Body,
        };

        if (_browsers.TryGetValue(windowId, out var existing))
        {
            existing.GetMainFrame()?.LoadUrl(url);
            return;
        }

        var window = ResolveWindowById(windowId);
        SpawnBrowser(window, url);
    }

    private WindowOptions ResolveWindowById(ulong windowId)
    {
        for (var i = 0; i < AppInfo.StartupWindowCount(); i++)
        {
            var w = AppInfo.ResolvedStartupWindow(i);
            if (w.Id == windowId) return w;
        }
        return AppInfo.ResolvedStartupWindow(0) with { Id = windowId };
    }

    private void SpawnBrowser(WindowOptions window, string url)
    {
        var windowInfo = CefWindowInfo.Create();
        windowInfo.SetAsPopup(IntPtr.Zero, window.ResolvedTitle(AppInfo.AppName));
        windowInfo.Bounds = new CefRectangle(
            (int)window.DefaultFrame.X,
            (int)window.DefaultFrame.Y,
            (int)window.DefaultFrame.Width,
            (int)window.DefaultFrame.Height);

        var client = new CefClientImpl(
            onCreated: b => OnBrowserCreated(window.Id, b),
            onBridge: OnBridgeMessage,
            policyAccessor: () => _policy);
        CefBrowserHost.CreateBrowser(windowInfo, client, new CefBrowserSettings(), url);
    }

    private void OnBrowserCreated(ulong windowId, CefBrowser browser)
    {
        _browsers[windowId] = browser;
        _windowIdByBrowserIdentifier[browser.Identifier] = windowId;
        if (windowId == _primaryWindowId) _primaryBrowser = browser;
    }

    private CefBrowser? BrowserFor(ulong windowId)
        => _browsers.TryGetValue(windowId, out var b) ? b : _primaryBrowser;

    private string ConfigureAssetSource(WebViewAssetSource o)
    {
        _assetServer = new AssetServer(o.RootPath, o.Entry, o.SpaFallback);
        _assetOrigin = o.Origin.TrimEnd('/');
        return $"{_assetOrigin}/{o.Entry.TrimStart('/')}";
    }

    void IPlatformServices.CompleteWindowBridge(ulong windowId, string response)
    {
        var frame = BrowserFor(windowId)?.GetMainFrame();
        frame?.ExecuteJavaScript(
            $"window.__zero_native_bridge_response && window.__zero_native_bridge_response({response});",
            "zero://inline",
            0);
    }

    WindowInfo IPlatformServices.CreateWindow(WindowOptions options)
    {
        EnsureInitialized();
        if (_browsers.ContainsKey(options.Id))
            throw new DuplicateWindowException("duplicate window id");

        // The browser load URL is supplied by the runtime via a subsequent
        // LoadWindowWebView call. Use about:blank as a placeholder; the runtime
        // replaces it immediately. (CEF needs an initial URL at creation time.)
        SpawnBrowser(options, "about:blank");
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
        BrowserFor(windowId)?.GetHost().SetFocus(true);
    }

    void IPlatformServices.SetWindowFrame(ulong windowId, RectF frame)
    {
        var browser = BrowserFor(windowId)
            ?? throw new WindowNotFoundException();
        var handle = browser.GetHost().GetWindowHandle();
        if (handle == IntPtr.Zero)
            throw new UnsupportedServiceException("CEF browser has no native window handle yet");
        CefNativeWindow.SetFrame(handle, frame);
    }

    void IPlatformServices.CloseWindow(ulong windowId)
    {
        if (_browsers.TryGetValue(windowId, out var browser))
        {
            browser.GetHost().CloseBrowser(true);
            _browsers.Remove(windowId);
            _windowIdByBrowserIdentifier.Remove(browser.Identifier);
            if (windowId == _primaryWindowId) _primaryBrowser = null;
        }

        // When the primary closes — or every window has closed — drop the loop.
        if (_browsers.Count == 0 || windowId == _primaryWindowId)
            CefRuntime.QuitMessageLoop();
    }

    void IPlatformServices.ConfigureSecurityPolicy(SecurityPolicy policy) => _policy = policy;

    void IPlatformServices.EmitWindowEvent(ulong windowId, string eventName, string detailJson)
    {
        var frame = BrowserFor(windowId)?.GetMainFrame();
        if (frame is null) return;
        var safeName = System.Text.Json.JsonSerializer.Serialize(eventName);
        frame.ExecuteJavaScript(
            $"window.dispatchEvent(new CustomEvent({safeName}, {{ detail: {detailJson} }}));",
            "zero://inline",
            0);
    }

    private void OnBridgeMessage(int browserId, string payload)
    {
        var windowId = _windowIdByBrowserIdentifier.TryGetValue(browserId, out var id) ? id : _primaryWindowId;
        _handler?.Invoke(new PlatformEvent.BridgeReceived(new BridgeMessage(payload, "zero://inline", windowId)));
    }

    public void Dispose()
    {
        if (_initialized)
        {
            try { CefRuntime.Shutdown(); } catch { /* idempotent */ }
        }
    }

    /// <summary>Convenience factory that creates a CefPlatform for the current OS.</summary>
    public static IPlatform CreateForCurrentOs(AppInfo appInfo, CefPlatformOptions options, Surface? surface = null)
        => new CefPlatform(appInfo, surface ?? new Surface(), options);

    internal static void OpenExternally(string url)
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
            // Best effort.
        }
    }
}

internal sealed class CefClientImpl : CefClient
{
    private readonly CefLifeSpanHandlerImpl _lifeSpan;
    private readonly CefRequestHandlerImpl _request;
    private readonly Action<int, string> _onBridge;

    public CefClientImpl(Action<CefBrowser> onCreated, Action<int, string> onBridge, Func<SecurityPolicy> policyAccessor)
    {
        _lifeSpan = new CefLifeSpanHandlerImpl(onCreated, policyAccessor);
        _request = new CefRequestHandlerImpl(policyAccessor);
        _onBridge = onBridge;
    }

    protected override CefLifeSpanHandler? GetLifeSpanHandler() => _lifeSpan;
    protected override CefRequestHandler? GetRequestHandler() => _request;

    protected override bool OnProcessMessageReceived(CefBrowser browser, CefFrame frame, CefProcessId sourceProcess, CefProcessMessage message)
    {
        if (message.Name != CefRenderHandler.ProcessMessageName) return false;
        var args = message.Arguments;
        if (args is null) return true;
        var payload = args.GetString(0);
        if (string.IsNullOrEmpty(payload)) return true;
        _onBridge(browser.Identifier, payload);
        return true;
    }
}

internal sealed class CefLifeSpanHandlerImpl : CefLifeSpanHandler
{
    private readonly Action<CefBrowser> _onCreated;
    private readonly Func<SecurityPolicy> _policy;

    public CefLifeSpanHandlerImpl(Action<CefBrowser> onCreated, Func<SecurityPolicy> policy)
    {
        _onCreated = onCreated;
        _policy = policy;
    }

    protected override void OnAfterCreated(CefBrowser browser)
    {
        base.OnAfterCreated(browser);
        _onCreated(browser);
    }

    protected override bool DoClose(CefBrowser browser) => false;

    protected override void OnBeforeClose(CefBrowser browser)
    {
        base.OnBeforeClose(browser);
        CefRuntime.QuitMessageLoop();
    }

    protected override bool OnBeforePopup(
        CefBrowser browser,
        CefFrame frame,
        string targetUrl,
        string targetFrameName,
        CefWindowOpenDisposition targetDisposition,
        bool userGesture,
        CefPopupFeatures popupFeatures,
        CefWindowInfo windowInfo,
        ref CefClient client,
        CefBrowserSettings settings,
        ref CefDictionaryValue extraInfo,
        ref bool noJavascriptAccess)
    {
        if (IsHostInitiatedUri(targetUrl)) return false;
        var decision = _policy().DecideNavigation(targetUrl);
        return decision switch
        {
            NavigationDecision.AllowInline => false,
            NavigationDecision.OpenExternally => OpenExternallyCancel(targetUrl),
            _ => true,
        };
    }

    private static bool IsHostInitiatedUri(string uri)
    {
        if (string.IsNullOrEmpty(uri)) return true;
        if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return true;
        if (uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return true;
        if (uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool OpenExternallyCancel(string url)
    {
        CefPlatform.OpenExternally(url);
        return true;
    }
}

internal sealed class CefRequestHandlerImpl : CefRequestHandler
{
    private readonly Func<SecurityPolicy> _policy;

    public CefRequestHandlerImpl(Func<SecurityPolicy> policy) => _policy = policy;

    protected override bool OnBeforeBrowse(CefBrowser browser, CefFrame frame, CefRequest request, bool userGesture, bool isRedirect)
    {
        if (IsHostInitiatedUri(request.Url)) return false;
        var decision = _policy().DecideNavigation(request.Url);
        return decision switch
        {
            NavigationDecision.AllowInline => false,
            NavigationDecision.OpenExternally => OpenExternallyCancel(request.Url),
            _ => true,
        };
    }

    private static bool IsHostInitiatedUri(string uri)
    {
        if (string.IsNullOrEmpty(uri)) return true;
        if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return true;
        if (uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return true;
        if (uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    protected override CefResourceRequestHandler? GetResourceRequestHandler(
        CefBrowser browser, CefFrame frame, CefRequest request, bool isNavigation, bool isDownload,
        string requestInitiator, ref bool disableDefaultHandling) => null;

    private static bool OpenExternallyCancel(string url)
    {
        CefPlatform.OpenExternally(url);
        return true;
    }
}

internal sealed class ZeroSchemeHandlerFactory : CefSchemeHandlerFactory
{
    private readonly Func<AssetServer?> _serverAccessor;

    public ZeroSchemeHandlerFactory(Func<AssetServer?> serverAccessor) => _serverAccessor = serverAccessor;

    protected override CefResourceHandler? Create(CefBrowser browser, CefFrame frame, string schemeName, CefRequest request)
    {
        var server = _serverAccessor();
        if (server is null) return null;
        return new ZeroAssetResourceHandler(server, request.Url);
    }
}

internal sealed class ZeroAssetResourceHandler : CefResourceHandler
{
    private readonly AssetServer _server;
    private readonly string _url;
    private AssetResponse? _response;
    private int _offset;

    public ZeroAssetResourceHandler(AssetServer server, string url)
    {
        _server = server;
        _url = url;
    }

    protected override bool Open(CefRequest request, out bool handleRequest, CefCallback callback)
    {
        _response = _server.Resolve(_url) ?? new AssetResponse(ReadOnlyMemory<byte>.Empty, "text/plain; charset=utf-8", 404);
        handleRequest = true;
        return true;
    }

    protected override void GetResponseHeaders(CefResponse response, out long responseLength, out string? redirectUrl)
    {
        redirectUrl = null;
        var body = _response ?? new AssetResponse(ReadOnlyMemory<byte>.Empty, "text/plain; charset=utf-8", 404);
        response.Status = body.StatusCode;
        response.StatusText = body.StatusCode == 200 ? "OK" : "Not Found";
        var (mime, charset) = SplitContentType(body.ContentType);
        response.MimeType = mime;
        if (charset is not null) response.Charset = charset;
        response.SetHeaderByName("Access-Control-Allow-Origin", "*", true);
        responseLength = body.Body.Length;
    }

    protected override bool Skip(long bytesToSkip, out long bytesSkipped, CefResourceSkipCallback callback)
    {
        var body = _response?.Body ?? ReadOnlyMemory<byte>.Empty;
        var available = body.Length - _offset;
        var skipped = (int)Math.Min(bytesToSkip, available);
        _offset += skipped;
        bytesSkipped = skipped;
        return skipped > 0;
    }

    protected override bool Read(Stream response, int bytesToRead, out int bytesRead, CefResourceReadCallback callback)
    {
        var body = _response?.Body ?? ReadOnlyMemory<byte>.Empty;
        var remaining = body.Length - _offset;
        if (remaining <= 0)
        {
            bytesRead = 0;
            return false;
        }

        var chunk = Math.Min(bytesToRead, remaining);
        response.Write(body.Span.Slice(_offset, chunk));
        _offset += chunk;
        bytesRead = chunk;
        return true;
    }

    protected override void Cancel() { }

    private static (string mime, string? charset) SplitContentType(string contentType)
    {
        var idx = contentType.IndexOf(';');
        if (idx < 0) return (contentType.Trim(), null);
        var mime = contentType[..idx].Trim();
        var rest = contentType[(idx + 1)..];
        var charsetIdx = rest.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
        if (charsetIdx < 0) return (mime, null);
        return (mime, rest[(charsetIdx + "charset=".Length)..].Trim());
    }
}

internal static class CefRid
{
    public static string Current()
    {
        if (OperatingSystem.IsWindows())
            return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
        if (OperatingSystem.IsMacOS())
            return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        if (OperatingSystem.IsLinux())
            return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
        return "unknown";
    }
}
