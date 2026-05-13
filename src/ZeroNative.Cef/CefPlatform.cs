using System.Runtime.InteropServices;
using Xilium.CefGlue;
using Xilium.CefGlue.Common;
using ZeroNative.Platform;
using ZeroNative.Primitives;

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

    public CefPlatform(AppInfo appInfo, Surface surface, CefPlatformOptions options)
    {
        AppInfo = appInfo;
        Surface = surface;
        Options = options;
    }

    public void Run(Action<PlatformEvent> handler)
    {
        _handler = handler;
        EnsureInitialized();

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

        var customSchemes = Array.Empty<Xilium.CefGlue.Common.Shared.CustomScheme>();
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
                => new Uri(Path.GetFullPath(Path.Combine(o.RootPath, o.Entry))).AbsoluteUri,
            _ => source.Body,
        };

        if (_primaryBrowser is not null)
        {
            _primaryBrowser.GetMainFrame()?.LoadUrl(url);
            return;
        }

        var window = AppInfo.ResolvedStartupWindow(0);
        var windowInfo = CefWindowInfo.Create();
        windowInfo.SetAsPopup(IntPtr.Zero, window.ResolvedTitle(AppInfo.AppName));
        windowInfo.Bounds = new CefRectangle(
            (int)window.DefaultFrame.X,
            (int)window.DefaultFrame.Y,
            (int)window.DefaultFrame.Width,
            (int)window.DefaultFrame.Height);

        var client = new CefClientImpl(b => _primaryBrowser = b);
        CefBrowserHost.CreateBrowser(windowInfo, client, new CefBrowserSettings(), url);
    }

    void IPlatformServices.CompleteWindowBridge(ulong windowId, string response)
    {
        var frame = _primaryBrowser?.GetMainFrame();
        frame?.ExecuteJavaScript(
            $"window.__zero_native_bridge_response && window.__zero_native_bridge_response({response});",
            "zero://inline",
            0);
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

    void IPlatformServices.FocusWindow(ulong windowId) => _primaryBrowser?.GetHost().SetFocus(true);

    void IPlatformServices.CloseWindow(ulong windowId)
    {
        _primaryBrowser?.GetHost().CloseBrowser(true);
        CefRuntime.QuitMessageLoop();
    }

    void IPlatformServices.EmitWindowEvent(ulong windowId, string eventName, string detailJson)
    {
        var frame = _primaryBrowser?.GetMainFrame();
        if (frame is null) return;
        var safeName = System.Text.Json.JsonSerializer.Serialize(eventName);
        frame.ExecuteJavaScript(
            $"window.dispatchEvent(new CustomEvent({safeName}, {{ detail: {detailJson} }}));",
            "zero://inline",
            0);
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
}

internal sealed class CefClientImpl : CefClient
{
    private readonly CefLifeSpanHandlerImpl _lifeSpan;

    public CefClientImpl(Action<CefBrowser> onCreated)
    {
        _lifeSpan = new CefLifeSpanHandlerImpl(onCreated);
    }

    protected override CefLifeSpanHandler? GetLifeSpanHandler() => _lifeSpan;
}

internal sealed class CefLifeSpanHandlerImpl : CefLifeSpanHandler
{
    private readonly Action<CefBrowser> _onCreated;

    public CefLifeSpanHandlerImpl(Action<CefBrowser> onCreated) => _onCreated = onCreated;

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
