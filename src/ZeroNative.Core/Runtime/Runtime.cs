using System.Diagnostics;
using System.Text.Json;
using ZeroNative.Automation;
using ZeroNative.Bridge;
using ZeroNative.Platform;
using ZeroNative.Primitives;
using ZeroNative.Security;
using ZeroNative.WindowStateStores;

namespace ZeroNative.Runtime;

public sealed record RuntimeOptions
{
    public required IPlatform Platform { get; init; }
    public Action<TraceRecord>? TraceSink { get; init; }
    public string? LogPath { get; init; }
    public BridgeDispatcher? BridgeDispatcher { get; init; }
    public BridgePolicy BuiltinBridge { get; init; } = new();
    public SecurityPolicy Security { get; init; } = new();
    public bool JsWindowApi { get; init; } = false;
    public WindowStateStores.IWindowStateStore? WindowStateStore { get; init; }
}

public sealed record TraceRecord(DateTimeOffset Timestamp, string Level, string Name, string? Message, IReadOnlyDictionary<string, object?> Fields);

public sealed class Runtime
{
    public RuntimeOptions Options { get; }
    public Surface Surface { get; private set; }
    public ulong FrameIndex { get; private set; }
    public int CommandCount { get; private set; }
    public FrameDiagnostics LastDiagnostics { get; private set; }
    public InvalidationReason LastInvalidationReason { get; private set; } = InvalidationReason.Startup;
    public WebViewSource? LoadedSource { get; private set; }
    public bool Invalidated { get; private set; } = true;

    private readonly List<WindowInfo> _windows = new();
    private readonly Dictionary<ulong, WebViewSource> _windowSources = new();
    private readonly List<RectF> _dirtyRegions = new();
    private ulong _nextWindowId = 2;

    public IReadOnlyList<WindowInfo> Windows => _windows;

    public Runtime(RuntimeOptions options)
    {
        Options = options;
        Surface = options.Platform.Surface;
    }

    public void Invalidate() => InvalidateFor(InvalidationReason.State, null);

    public void InvalidateFor(InvalidationReason reason, RectF? dirty)
    {
        Invalidated = true;
        LastInvalidationReason = reason;
        if (dirty is { } region && _dirtyRegions.Count < 8)
            _dirtyRegions.Add(region);
    }

    public void Run(App app)
    {
        Log("runtime.init", "runtime initialized", new()
        {
            ["app"] = app.Name,
            ["platform"] = Options.Platform.Name,
            ["log_path"] = Options.LogPath ?? "",
        });

        try { Options.Platform.Services.ConfigureSecurityPolicy(Options.Security); }
        catch (UnsupportedServiceException) { /* not all platforms need this */ }

        Options.Platform.Run(ev => DispatchPlatformEvent(app, ev));

        Log("runtime.done", "runtime finished", null);
    }

    public void DispatchPlatformEvent(App app, PlatformEvent ev)
    {
        if (ev is not PlatformEvent.FrameRequested || Invalidated)
        {
            Log("platform.event", null, new() { ["event"] = ev.Name });
        }

        switch (ev)
        {
            case PlatformEvent.AppStart:
                app.Start(this);
                DispatchEvent(app, new RuntimeEvent.Lifecycle(LifecycleEvent.Start));
                LoadStartupWindows(app);
                InvalidateFor(InvalidationReason.Startup, null);
                Log("app.start", "app started", new() { ["app"] = app.Name });
                break;

            case PlatformEvent.SurfaceResized resized:
                Surface = resized.Surface;
                var index = FindWindowIndexById(resized.Surface.Id);
                if (index >= 0)
                {
                    _windows[index] = _windows[index] with
                    {
                        Frame = _windows[index].Frame with
                        {
                            Width = resized.Surface.Size.Width,
                            Height = resized.Surface.Size.Height,
                        },
                        ScaleFactor = resized.Surface.ScaleFactor,
                    };
                }
                InvalidateFor(InvalidationReason.SurfaceResize, RectF.FromSize(resized.Surface.Size));
                Log("surface.resize", "surface updated", new()
                {
                    ["width"] = resized.Surface.Size.Width,
                    ["height"] = resized.Surface.Size.Height,
                    ["scale"] = resized.Surface.ScaleFactor,
                });
                break;

            case PlatformEvent.WindowFrameChanged changed:
                UpdateWindowState(changed.State);
                if (Options.WindowStateStore is not null)
                {
                    try { Options.WindowStateStore.SaveWindow(changed.State); }
                    catch (Exception ex)
                    {
                        Log("window.state.save_failed", ex.Message, new() { ["label"] = changed.State.Label });
                    }
                }
                Log("window.frame", "window frame updated", new()
                {
                    ["label"] = changed.State.Label,
                    ["x"] = changed.State.Frame.X,
                    ["y"] = changed.State.Frame.Y,
                    ["width"] = changed.State.Frame.Width,
                    ["height"] = changed.State.Frame.Height,
                });
                break;

            case PlatformEvent.WindowFocused focused:
                var fidx = FindWindowIndexById(focused.WindowId);
                if (fidx >= 0) SetFocusedIndex(fidx);
                Invalidated = true;
                break;

            case PlatformEvent.FrameRequested:
                Frame(app);
                break;

            case PlatformEvent.BridgeReceived bm:
                HandleBridgeMessage(bm.Message);
                break;

            case PlatformEvent.TrayAction tray:
                Log("tray.action", "tray item selected", new() { ["item_id"] = tray.ItemId });
                DispatchEvent(app, new RuntimeEvent.Command("tray.action"));
                break;

            case PlatformEvent.AppShutdown:
                DispatchEvent(app, new RuntimeEvent.Lifecycle(LifecycleEvent.Stop));
                app.Stop(this);
                Log("app.stop", "app stopped", new() { ["app"] = app.Name });
                break;
        }
    }

    public void DispatchEvent(App app, RuntimeEvent ev)
    {
        Log("runtime.event", null, new() { ["event"] = ev.Name });
        app.HandleEvent(this, ev);

        if (ev is RuntimeEvent.Command) InvalidateFor(InvalidationReason.Command, null);
    }

    public void Frame(App app)
    {
        var stopwatch = Stopwatch.StartNew();
        if (!Invalidated) return;

        FrameIndex++;
        LastDiagnostics = new FrameDiagnostics(
            FrameIndex,
            CommandCount,
            _dirtyRegions.Count,
            0,
            stopwatch.ElapsedTicks * (1_000_000_000L / Stopwatch.Frequency));
        CommandCount = 0;
        _dirtyRegions.Clear();
        Invalidated = false;
        Log("runtime.frame", "frame published", new()
        {
            ["frame"] = FrameIndex,
            ["dirty_regions"] = LastDiagnostics.DirtyRegionCount,
        });
        app.HandleEvent(this, new RuntimeEvent.Lifecycle(LifecycleEvent.Frame));
    }

    public WindowInfo CreateWindow(WindowCreateOptions options)
    {
        var source = options.Source ?? LoadedSource ?? throw new MissingWindowSourceException();
        if (string.IsNullOrEmpty(options.Label)) throw new InvalidWindowOptionsException();
        var id = options.Id != 0 ? options.Id : AllocateWindowId();
        if (FindWindowIndexById(id) >= 0) throw new DuplicateWindowException("duplicate window id");
        if (FindWindowIndexByLabel(options.Label) >= 0) throw new DuplicateWindowException("duplicate window label");

        var resolved = ApplyPersistedState(options.ToWindowOptions(id, options.Label) with { DefaultFrame = options.DefaultFrame });
        options = options with { DefaultFrame = resolved.DefaultFrame };

        var info = new WindowInfo
        {
            Id = id,
            Label = options.Label,
            Title = options.Title,
            Frame = options.DefaultFrame,
            ScaleFactor = Surface.ScaleFactor,
            Open = true,
            Focused = false,
        };
        _windows.Add(info);
        _windowSources[id] = source;

        try
        {
            var native = Options.Platform.Services.CreateWindow(options.ToWindowOptions(id, options.Label));
            var idx = FindWindowIndexById(id);
            _windows[idx] = _windows[idx] with
            {
                Frame = native.Frame,
                ScaleFactor = native.ScaleFactor,
                Open = native.Open,
                Focused = native.Focused,
            };
            if (native.Focused) SetFocusedIndex(idx);

            Options.Platform.Services.LoadWindowWebView(id, source);
            Invalidated = true;
            return _windows[idx];
        }
        catch
        {
            var idx = FindWindowIndexById(id);
            if (idx >= 0) _windows.RemoveAt(idx);
            _windowSources.Remove(id);
            throw;
        }
    }

    public void FocusWindow(ulong windowId)
    {
        var idx = FindWindowIndexById(windowId);
        if (idx < 0) throw new WindowNotFoundException();
        Options.Platform.Services.FocusWindow(windowId);
        SetFocusedIndex(idx);
        Invalidated = true;
    }

    public void CloseWindow(ulong windowId)
    {
        var idx = FindWindowIndexById(windowId);
        if (idx < 0) throw new WindowNotFoundException();
        Options.Platform.Services.CloseWindow(windowId);
        _windows[idx] = _windows[idx] with { Open = false, Focused = false };
        Invalidated = true;
    }

    /// <summary>
    /// Programmatically moves and resizes <paramref name="windowId"/>. Updates the runtime's
    /// view of the window immediately so callers don't have to wait for a platform event,
    /// then delegates to the platform. Backends that don't support programmatic geometry
    /// changes raise <see cref="UnsupportedServiceException"/>, which the runtime swallows
    /// after logging.
    /// </summary>
    public void SetWindowFrame(ulong windowId, RectF frame)
    {
        var idx = FindWindowIndexById(windowId);
        if (idx < 0) throw new WindowNotFoundException();

        try { Options.Platform.Services.SetWindowFrame(windowId, frame); }
        catch (UnsupportedServiceException)
        {
            Log("window.set_frame.unsupported", "platform refused programmatic resize", new() { ["window_id"] = windowId });
            return;
        }

        _windows[idx] = _windows[idx] with { Frame = frame };
        if (Options.WindowStateStore is { } store)
        {
            try { store.SaveWindow(_windows[idx].ToState()); }
            catch (Exception ex)
            {
                Log("window.state.save_failed", ex.Message, new() { ["label"] = _windows[idx].Label });
            }
        }
        Invalidated = true;
    }

    public void EmitWindowEvent(ulong windowId, string name, string detailJson)
    {
        if (!Bridge.Bridge.IsValidJsonValue(detailJson))
            throw new ArgumentException("Detail must be a valid JSON value", nameof(detailJson));
        Options.Platform.Services.EmitWindowEvent(windowId, name, detailJson);
    }

    public void RespondToBridge(BridgeSource source, string response)
        => CompleteBridgeResponse(source.WindowId, response);

    /// <summary>
    /// Snapshots the runtime's current window list and diagnostics for the
    /// automation server. Pair with <see cref="AutomationServer.Publish"/>
    /// from a frame hook or the platform's idle callback.
    /// </summary>
    public AutomationInput BuildAutomationInput()
    {
        var windows = new AutomationWindow[_windows.Count];
        for (var i = 0; i < _windows.Count; i++)
        {
            var w = _windows[i];
            windows[i] = new AutomationWindow(w.Id, w.Title, w.Frame, w.Focused);
        }
        return new AutomationInput(
            windows,
            new AutomationDiagnostics(FrameIndex, CommandCount),
            LoadedSource);
    }

    private void LoadStartupWindows(App app)
    {
        var source = app.GetSource(this);
        LoadedSource = source;
        var appInfo = Options.Platform.AppInfo;
        var count = appInfo.StartupWindowCount();
        for (var i = 0; i < count; i++)
        {
            var original = appInfo.ResolvedStartupWindow(i);
            var window = ApplyPersistedState(original);
            var frameRestored = window.DefaultFrame != original.DefaultFrame;

            if (FindWindowIndexById(window.Id) < 0)
            {
                _windows.Add(new WindowInfo
                {
                    Id = window.Id,
                    Label = window.Label,
                    Title = window.ResolvedTitle(appInfo.AppName),
                    Frame = window.DefaultFrame,
                    ScaleFactor = Surface.ScaleFactor,
                    Open = true,
                    Focused = i == 0,
                });
                _windowSources[window.Id] = source;
            }

            if (i > 0)
            {
                try { Options.Platform.Services.CreateWindow(window); }
                catch (UnsupportedServiceException) { /* platform may auto-create */ }
            }
            else if (frameRestored)
            {
                // The primary window is constructed by the platform from AppInfo before
                // the runtime ever runs, so it never sees the persisted frame unless the
                // caller pre-applied WindowStateRestoration.Apply. Push the restored frame
                // through SetWindowFrame so backends can resize the live HWND/NSWindow.
                try { Options.Platform.Services.SetWindowFrame(window.Id, window.DefaultFrame); }
                catch (UnsupportedServiceException) { /* not all backends support it yet */ }
            }

            try { Options.Platform.Services.LoadWindowWebView(window.Id, source); }
            catch (UnsupportedServiceException) { /* platform may not be ready */ }

            if (_nextWindowId <= window.Id) _nextWindowId = window.Id + 1;
        }
        Log("webview.load", "loaded webview source", new()
        {
            ["kind"] = source.Kind.ToString().ToLowerInvariant(),
            ["bytes"] = source.Body.Length,
        });
    }

    /// <summary>
    /// When a <see cref="RuntimeOptions.WindowStateStore"/> is configured and the window
    /// opts into <see cref="WindowOptions.RestoreState"/>, replaces the default frame with
    /// the persisted geometry. The frame is sanitized by the configured restore policy.
    /// </summary>
    private WindowOptions ApplyPersistedState(WindowOptions window)
    {
        if (!window.RestoreState) return window;
        var store = Options.WindowStateStore;
        if (store is null) return window;

        Platform.WindowState? saved;
        try { saved = store.LoadWindow(window.Label); }
        catch (Exception ex)
        {
            Log("window.state.load_failed", ex.Message, new() { ["label"] = window.Label });
            return window;
        }

        if (saved is null) return window;
        var frame = WindowStateRestoration.Sanitize(saved.Frame, window.DefaultFrame, window.RestorePolicy);
        return window with { DefaultFrame = frame };
    }

    private void HandleBridgeMessage(BridgeMessage message)
    {
        CommandCount++;
        if (HandleBuiltinBridgeMessage(message)) return;

        var dispatcher = Options.BridgeDispatcher ?? new BridgeDispatcher();
        if (Options.Security.Permissions.Count > 0)
        {
            dispatcher = new BridgeDispatcher
            {
                Policy = dispatcher.Policy with { Permissions = Options.Security.Permissions },
                Registry = dispatcher.Registry,
            };
        }

        var source = new BridgeSource(message.Origin, message.WindowId);

        // Prefer async dispatch when an async handler is registered for this command.
        var dispatchTask = dispatcher.DispatchAsync(message.Bytes, source, AsyncRespondToBridge);
        BridgeDispatcher.DispatchResult outcome;
        if (dispatchTask.IsCompletedSuccessfully)
        {
            outcome = dispatchTask.Result;
        }
        else
        {
            // Block here; the runtime invariant is single-threaded message dispatch.
            // Async handlers are encouraged to defer work via their own scheduling.
            outcome = dispatchTask.AsTask().GetAwaiter().GetResult();
        }

        if (outcome.IsAsync)
        {
            // Response is delivered asynchronously through AsyncRespondToBridge.
            InvalidateFor(InvalidationReason.Command, null);
            Log("bridge.dispatch.async", "bridge request handed off to async handler", new()
            {
                ["request_bytes"] = message.Bytes.Length,
            });
            return;
        }

        var response = outcome.Response ?? Bridge.Bridge.WriteErrorResponse("", BridgeErrorCode.InternalError, "Empty dispatch response");
        CompleteBridgeResponse(message.WindowId, response);
        InvalidateFor(InvalidationReason.Command, null);
        Log("bridge.dispatch", "bridge request handled", new()
        {
            ["request_bytes"] = message.Bytes.Length,
            ["response_bytes"] = response.Length,
        });
    }

    private ValueTask AsyncRespondToBridge(BridgeSource source, string response)
    {
        CompleteBridgeResponse(source.WindowId, response);
        return ValueTask.CompletedTask;
    }

    private bool HandleBuiltinBridgeMessage(BridgeMessage message)
    {
        BridgeRequest request;
        try { request = Bridge.Bridge.ParseRequest(message.Bytes); }
        catch (BridgeParseException) { return false; }

        var isWindow = request.Command.StartsWith("zero-native.window.", StringComparison.Ordinal);
        var isDialog = request.Command.StartsWith("zero-native.dialog.", StringComparison.Ordinal);
        if (!isWindow && !isDialog) return false;

        if (!AllowsBuiltinBridgeCommand(request.Command, message.Origin, isWindow))
        {
            var deniedMsg = isWindow ? "Window API is not permitted" : "Dialog API is not permitted";
            CompleteBridgeResponse(message.WindowId,
                Bridge.Bridge.WriteErrorResponse(request.Id, BridgeErrorCode.PermissionDenied, deniedMsg));
            InvalidateFor(InvalidationReason.Command, null);
            return true;
        }

        string response;
        try
        {
            var result = isWindow ? DispatchWindowBridge(request) : DispatchDialogBridge(request);
            response = Bridge.Bridge.WriteSuccessResponse(request.Id, result);
        }
        catch (Exception ex)
        {
            response = Bridge.Bridge.WriteErrorResponse(request.Id, BridgeErrorCode.InternalError, ex.Message);
        }

        CompleteBridgeResponse(message.WindowId, response);
        InvalidateFor(InvalidationReason.Command, null);
        return true;
    }

    private bool AllowsBuiltinBridgeCommand(string command, string origin, bool isWindow)
    {
        var policy = Options.BuiltinBridge;
        if (Options.Security.Permissions.Count > 0)
            policy = policy with { Permissions = Options.Security.Permissions };
        if (policy.Enabled) return policy.Allows(command, origin);
        if (!isWindow || !Options.JsWindowApi) return false;
        if (!Options.Security.AllowsOrigin(origin)) return false;
        if (Options.Security.Permissions.Count == 0) return true;
        return Options.Security.HasPermission(Permissions.Window);
    }

    private string DispatchWindowBridge(BridgeRequest request)
    {
        return request.Command switch
        {
            "zero-native.window.list" => WindowListJson(),
            "zero-native.window.create" => CreateWindowFromJson(request.Payload),
            "zero-native.window.focus" => FocusWindowFromJson(request.Payload),
            "zero-native.window.close" => CloseWindowFromJson(request.Payload),
            _ => throw new BridgeHandlerException("Unknown window command", BridgeErrorCode.UnknownCommand),
        };
    }

    private string DispatchDialogBridge(BridgeRequest request)
    {
        return request.Command switch
        {
            "zero-native.dialog.openFile" => OpenFileDialogFromJson(request.Payload),
            "zero-native.dialog.saveFile" => SaveFileDialogFromJson(request.Payload),
            "zero-native.dialog.showMessage" => ShowMessageDialogFromJson(request.Payload),
            _ => throw new BridgeHandlerException("Unknown dialog command", BridgeErrorCode.UnknownCommand),
        };
    }

    private string WindowListJson()
        => "[" + string.Join(",", _windows.Select(WindowToJson)) + "]";

    private string CreateWindowFromJson(string payload)
    {
        var label = JsonUtilities.StringField(payload, "label") ?? "window";
        var title = JsonUtilities.StringField(payload, "title") ?? "";
        var width = JsonUtilities.NumberField(payload, "width") ?? 720;
        var height = JsonUtilities.NumberField(payload, "height") ?? 480;
        var x = JsonUtilities.NumberField(payload, "x") ?? 0;
        var y = JsonUtilities.NumberField(payload, "y") ?? 0;
        var url = JsonUtilities.StringField(payload, "url");
        var info = CreateWindow(new WindowCreateOptions
        {
            Label = label,
            Title = title,
            DefaultFrame = new RectF(x, y, width, height),
            RestoreState = JsonUtilities.BoolField(payload, "restoreState") ?? true,
            Source = url is { } u ? WebViewSource.Url(u) : null,
        });
        return WindowToJson(info);
    }

    private string FocusWindowFromJson(string payload)
    {
        var id = ResolveWindowSelector(payload);
        FocusWindow(id);
        return WindowToJson(_windows[FindWindowIndexById(id)]);
    }

    private string CloseWindowFromJson(string payload)
    {
        var id = ResolveWindowSelector(payload);
        var idx = FindWindowIndexById(id);
        var info = _windows[idx] with { Open = false, Focused = false };
        CloseWindow(id);
        return WindowToJson(info);
    }

    private ulong ResolveWindowSelector(string payload)
    {
        if (JsonUtilities.UnsignedField(payload, "id") is ulong id) return id;
        if (JsonUtilities.StringField(payload, "label") is string label)
        {
            var idx = FindWindowIndexByLabel(label);
            if (idx >= 0) return _windows[idx].Id;
        }
        throw new WindowNotFoundException();
    }

    private string OpenFileDialogFromJson(string payload)
    {
        var options = new OpenDialogOptions(
            JsonUtilities.StringField(payload, "title") ?? "",
            JsonUtilities.StringField(payload, "defaultPath") ?? "",
            null,
            JsonUtilities.BoolField(payload, "allowDirectories") ?? false,
            JsonUtilities.BoolField(payload, "allowMultiple") ?? false);
        var result = Options.Platform.Services.ShowOpenDialog(options);
        if (result.Paths.Count == 0) return "null";
        return "[" + string.Join(",", result.Paths.Select(JsonUtilities.EncodeString)) + "]";
    }

    private string SaveFileDialogFromJson(string payload)
    {
        var options = new SaveDialogOptions(
            JsonUtilities.StringField(payload, "title") ?? "",
            JsonUtilities.StringField(payload, "defaultPath") ?? "",
            JsonUtilities.StringField(payload, "defaultName") ?? "");
        var path = Options.Platform.Services.ShowSaveDialog(options);
        return path is null ? "null" : JsonUtilities.EncodeString(path);
    }

    private string ShowMessageDialogFromJson(string payload)
    {
        var styleStr = JsonUtilities.StringField(payload, "style") ?? "info";
        var style = styleStr switch
        {
            "warning" => MessageDialogStyle.Warning,
            "critical" => MessageDialogStyle.Critical,
            _ => MessageDialogStyle.Info,
        };
        var options = new MessageDialogOptions(
            style,
            JsonUtilities.StringField(payload, "title") ?? "",
            JsonUtilities.StringField(payload, "message") ?? "",
            JsonUtilities.StringField(payload, "informativeText") ?? "",
            JsonUtilities.StringField(payload, "primaryButton") ?? "OK",
            JsonUtilities.StringField(payload, "secondaryButton") ?? "",
            JsonUtilities.StringField(payload, "tertiaryButton") ?? "");
        var result = Options.Platform.Services.ShowMessageDialog(options);
        var name = result switch
        {
            MessageDialogResult.Primary => "primary",
            MessageDialogResult.Secondary => "secondary",
            MessageDialogResult.Tertiary => "tertiary",
            _ => "primary",
        };
        return JsonUtilities.EncodeString(name);
    }

    private static string WindowToJson(WindowInfo w)
    {
        return $"{{\"id\":{w.Id},\"label\":{JsonUtilities.EncodeString(w.Label)},\"title\":{JsonUtilities.EncodeString(w.Title)}," +
               $"\"open\":{(w.Open ? "true" : "false")},\"focused\":{(w.Focused ? "true" : "false")}," +
               $"\"x\":{w.Frame.X},\"y\":{w.Frame.Y},\"width\":{w.Frame.Width},\"height\":{w.Frame.Height},\"scale\":{w.ScaleFactor}}}";
    }

    private void CompleteBridgeResponse(ulong windowId, string response)
    {
        try { Options.Platform.Services.CompleteWindowBridge(windowId, response); }
        catch (UnsupportedServiceException) { Options.Platform.Services.CompleteBridge(response); }
    }

    private void UpdateWindowState(Platform.WindowState state)
    {
        var idx = FindWindowIndexById(state.Id);
        if (idx < 0)
        {
            _windows.Add(new WindowInfo
            {
                Id = state.Id,
                Label = state.Label,
                Title = state.Title,
                Frame = state.Frame,
                ScaleFactor = state.ScaleFactor,
                Open = state.Open,
                Focused = state.Focused,
            });
            if (state.Focused) SetFocusedIndex(_windows.Count - 1);
            if (_nextWindowId <= state.Id) _nextWindowId = state.Id + 1;
            return;
        }

        _windows[idx] = _windows[idx] with
        {
            Frame = state.Frame,
            ScaleFactor = state.ScaleFactor,
            Open = state.Open,
            Focused = state.Focused,
            Title = state.Title.Length > 0 ? state.Title : _windows[idx].Title,
            Label = state.Label.Length > 0 ? state.Label : _windows[idx].Label,
        };
        if (state.Focused) SetFocusedIndex(idx);
    }

    private void SetFocusedIndex(int focusedIndex)
    {
        for (var i = 0; i < _windows.Count; i++)
            _windows[i] = _windows[i] with { Focused = i == focusedIndex };
    }

    private int FindWindowIndexById(ulong id) => _windows.FindIndex(w => w.Id == id);
    private int FindWindowIndexByLabel(string label) => _windows.FindIndex(w => w.Label == label);

    private ulong AllocateWindowId()
    {
        while (FindWindowIndexById(_nextWindowId) >= 0) _nextWindowId++;
        return _nextWindowId++;
    }

    private void Log(string name, string? message, Dictionary<string, object?>? fields)
    {
        if (Options.TraceSink is null) return;
        var fieldDict = fields is null
            ? (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>()
            : fields;
        Options.TraceSink(new TraceRecord(DateTimeOffset.UtcNow, "info", name, message, fieldDict));
    }
}
