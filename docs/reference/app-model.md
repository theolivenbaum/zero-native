---
order: 1
title: App Model
icon: cube
tags: [reference]
---

# App Model

A zero-native app is the composition of three pieces:

1. **`AppInfo`** – static identity and window metadata fed to the platform
   when it is constructed.
2. **`App`** – the runtime-facing object built via `AppBuilder`; carries the
   `WebViewSource` and lifecycle callbacks.
3. **`Runtime`** – owns the platform event loop, the bridge, the security
   policy, and any optional services (automation, trace sinks, window state).

## AppInfo

`AppInfo` is consumed once, by the platform constructor. It captures things
that have to be known before a native window is created.

```csharp
var appInfo = new AppInfo
{
    AppName     = "Hello",
    WindowTitle = "Hello, world",
    BundleId    = "dev.example.hello",
    MainWindow  = new WindowOptions
    {
        Id           = 1,
        Label        = "main",
        Title        = "Hello",
        DefaultFrame = new RectF(0, 0, 960, 600),
    },
};
```

| Field | Type | Description |
|---|---|---|
| `AppName` | `string` | Logical app name. Surfaces in traces and automation snapshots. |
| `WindowTitle` | `string` | Default title for the primary window. |
| `BundleId` | `string` | Reverse-DNS identifier; used by the macOS bundler and packaging. |
| `MainWindow` | `WindowOptions` | Geometry, label, and title for the main window. |

## AppBuilder

```csharp
var app = new AppBuilder()
    .Named("hello")
    .WithSource(WebViewSource.Html("<h1>Hello</h1>"))
    .OnStart(rt   => Console.Error.WriteLine("started"))
    .OnEvent((rt, e) => Console.Error.WriteLine($"event: {e}"))
    .OnStop(rt    => Console.Error.WriteLine("stopped"))
    .Build();
```

`AppBuilder` is the canonical way to construct an `App`. Callbacks are all
optional. The `WebViewSource` is required.

## WebViewSource

Three constructors, mirroring the Zig original:

```csharp
WebViewSource.Html("<h1>Inline HTML</h1>");
WebViewSource.Url("http://127.0.0.1:5173");
WebViewSource.Assets(new WebViewAssetSource
{
    RootPath    = "wwwroot",
    Entry       = "index.html",
    Origin      = "zero://app",
    SpaFallback = true,
});
```

| Source | Behaviour |
|---|---|
| `Html(content)` | Inline HTML, served as `zero://inline`. |
| `Url(uri)` | Loads a remote URL or `file://` path. |
| `Assets(options)` | Serves a local file tree through a custom origin via `AssetServer`. |

`Assets` is the production default for SPAs. `SpaFallback=true` returns
`Entry` for unknown paths so client-side routers keep working.

## Runtime

```csharp
var runtime = new Runtime(new RuntimeOptions
{
    Platform          = platform,
    BridgeDispatcher  = dispatcher,
    Security          = securityPolicy,
    BuiltinBridge     = builtinBridgePolicy,
    JsWindowApi       = true,
    TraceSink         = TraceSinks.Console(),
    WindowStateStore  = new JsonWindowStateStore(stateDir),
    AutomationServer  = automationServer,
    Modules           = moduleRegistry,
});

runtime.Run(app);
```

| Option | Type | Default | Description |
|---|---|---|---|
| `Platform` | `IPlatform` | required | The OS backend (system WebView or CEF). |
| `BridgeDispatcher` | `BridgeDispatcher?` | `null` | App-defined bridge commands. |
| `BuiltinBridge` | `BridgePolicy?` | `null` | Policy for the built-in `zero-native.window.*` / `zero-native.dialog.*` commands. |
| `Security` | `SecurityPolicy?` | safe defaults | Origins, permissions, external-link rules. |
| `JsWindowApi` | `bool` | `false` | Inject `window.zero.window.*` helpers. |
| `TraceSink` | `Action<TraceRecord>?` | `null` | Where to send structured trace records. |
| `WindowStateStore` | `IWindowStateStore?` | `null` | Persist window geometry between launches. |
| `AutomationServer` | `AutomationServer?` | `null` | Enables the file-based automation harness. |
| `Modules` | `ModuleRegistry?` | `null` | Module extensions with start/stop hooks. |

## Runtime methods

| Method | Description |
|---|---|
| `Run(app)` | Enter the platform event loop. Blocks until the runtime exits. |
| `CreateWindow(options)` | Open a new window (returns `WindowInfo`). |
| `ListWindows()` | Snapshot of all open windows. |
| `FocusWindow(id)` | Bring a window to front. |
| `CloseWindow(id)` | Close a window. |
| `BuildAutomationInput()` | Snapshot of windows + diagnostics for the automation harness. |

## Lifecycle hooks

The builder accepts three optional callbacks:

- `OnStart(Action<Runtime>)` – first frame loaded.
- `OnEvent(Action<Runtime, RuntimeEvent>)` – every lifecycle and bridge event.
- `OnStop(Action<Runtime>)` – shutdown.

For headless tests use `NullPlatform`: it supports every option above without
ever creating an OS window. The xUnit suite under
`tests/ZeroNative.Core.Tests/` shows the pattern.
