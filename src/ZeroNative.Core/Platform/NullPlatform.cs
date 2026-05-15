using ZeroNative.Security;

namespace ZeroNative.Platform;

/// <summary>
/// Deterministic in-memory platform used for tests and as a reference implementation.
/// Emits the canonical lifecycle without doing any UI.
/// </summary>
public sealed class NullPlatform : IPlatform, IPlatformServices
{
    private readonly List<WindowInfo> _windows = new();
    private readonly Dictionary<ulong, WebViewSource> _windowSources = new();
    private readonly Dictionary<ulong, string> _windowBridgeResponses = new();
    private readonly List<WindowEventRecord> _windowEvents = new();

    public string Name => "null";
    public Surface Surface { get; }
    public AppInfo AppInfo { get; }
    public WebEngine WebEngine { get; }
    public IPlatformServices Services => this;
    public uint RequestedFrames { get; init; } = 1;

    public WebViewSource? LoadedSource { get; private set; }
    public SecurityPolicy LastSecurityPolicy { get; private set; } = new();

    public string LastBridgeResponse { get; private set; } = "";
    public ulong LastBridgeResponseWindowId { get; private set; }

    /// <summary>Source loaded into each known window, keyed by window id.</summary>
    public IReadOnlyDictionary<ulong, WebViewSource> WindowSources => _windowSources;

    /// <summary>Most recent bridge response delivered per window, keyed by window id.</summary>
    public IReadOnlyDictionary<ulong, string> WindowBridgeResponses => _windowBridgeResponses;

    /// <summary>Every <c>EmitWindowEvent</c> call observed, in dispatch order.</summary>
    public IReadOnlyList<WindowEventRecord> WindowEvents => _windowEvents;

    public NullPlatform()
        : this(new Surface(), WebEngine.System, new AppInfo()) { }

    public NullPlatform(Surface surface)
        : this(surface, WebEngine.System, new AppInfo()) { }

    public NullPlatform(Surface surface, WebEngine engine, AppInfo appInfo)
    {
        Surface = surface;
        WebEngine = engine;
        AppInfo = appInfo;
    }

    public void Run(Action<PlatformEvent> handler)
    {
        handler(new PlatformEvent.AppStart());
        handler(new PlatformEvent.SurfaceResized(Surface));

        var count = AppInfo.StartupWindowCount();
        for (var i = 0; i < count; i++)
        {
            var window = AppInfo.ResolvedStartupWindow(i);
            handler(new PlatformEvent.WindowFrameChanged(new WindowState
            {
                Id = window.Id,
                Label = window.Label,
                Title = window.ResolvedTitle(AppInfo.AppName),
                Frame = window.DefaultFrame,
                ScaleFactor = Surface.ScaleFactor,
                Open = true,
                Focused = i == 0,
            }));
        }

        for (var i = 0; i < RequestedFrames; i++)
        {
            handler(new PlatformEvent.FrameRequested());
        }

        handler(new PlatformEvent.AppShutdown());
    }

    void IPlatformServices.LoadWindowWebView(ulong windowId, WebViewSource source)
    {
        _windowSources[windowId] = source;
        if (windowId == 1) LoadedSource = source;
    }

    void IPlatformServices.CompleteWindowBridge(ulong windowId, string response)
    {
        LastBridgeResponse = response;
        LastBridgeResponseWindowId = windowId;
        _windowBridgeResponses[windowId] = response;
    }

    WindowInfo IPlatformServices.CreateWindow(WindowOptions options)
    {
        if (_windows.Count >= PlatformLimits.MaxWindows) throw new WindowLimitReachedException();
        if (_windows.Any(w => w.Id == options.Id)) throw new DuplicateWindowException("Duplicate window id");
        if (_windows.Any(w => w.Label == options.Label)) throw new DuplicateWindowException("Duplicate window label");

        var info = new WindowInfo
        {
            Id = options.Id,
            Label = options.Label,
            Title = options.ResolvedTitle(AppInfo.AppName),
            Frame = options.DefaultFrame,
            ScaleFactor = Surface.ScaleFactor,
            Open = true,
            Focused = false,
        };
        _windows.Add(info);
        return info;
    }

    void IPlatformServices.FocusWindow(ulong windowId)
    {
        var idx = _windows.FindIndex(w => w.Id == windowId);
        if (idx < 0) throw new WindowNotFoundException();
        for (var i = 0; i < _windows.Count; i++)
        {
            _windows[i] = _windows[i] with { Focused = i == idx };
        }
    }

    void IPlatformServices.CloseWindow(ulong windowId)
    {
        var idx = _windows.FindIndex(w => w.Id == windowId);
        if (idx < 0) throw new WindowNotFoundException();
        _windows[idx] = _windows[idx] with { Open = false, Focused = false };
    }

    void IPlatformServices.ConfigureSecurityPolicy(SecurityPolicy policy)
    {
        LastSecurityPolicy = policy;
    }

    void IPlatformServices.EmitWindowEvent(ulong windowId, string eventName, string detailJson)
    {
        _windowEvents.Add(new WindowEventRecord(windowId, eventName, detailJson));
    }
}

/// <summary>
/// Snapshot of a single <see cref="IPlatformServices.EmitWindowEvent"/> call.
/// </summary>
public sealed record WindowEventRecord(ulong WindowId, string Name, string DetailJson);
