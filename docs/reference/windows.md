---
order: 3
title: Windows
icon: window-maximize
tags: [reference]
---

# Windows

The runtime supports multiple windows. The main window is created
automatically from `AppInfo.MainWindow`; secondary windows can be created
from .NET via `Runtime.CreateWindow` or from JavaScript via
`window.zero.window.create`.

## Creating windows from .NET

```csharp
var info = runtime.CreateWindow(new WindowOptions
{
    Label        = "tools",
    Title        = "Tools",
    DefaultFrame = new RectF(80, 80, 420, 320),
});
runtime.FocusWindow(info.Id);
```

`WindowOptions` mirrors the Zig original:

| Field | Type | Description |
|---|---|---|
| `Id` | `int` | Numeric window id. Auto-assigned when `0`. |
| `Label` | `string` | Stable label for window-state persistence and the automation harness. |
| `Title` | `string` | OS window title. |
| `DefaultFrame` | `RectF` | Initial position and size in DIP units. |

## Creating windows from JavaScript

Enable the `JsWindowApi` flag and grant the `window` permission:

```csharp
var runtime = new Runtime(new RuntimeOptions
{
    Platform    = platform,
    Security    = new SecurityPolicy(
        Permissions: new[] { Permissions.Window },
        Navigation:  new NavigationPolicy(AllowedOrigins: new[] { "zero://app" })),
    JsWindowApi = true,
});
```

```javascript
const win = await window.zero.window.create({
    label:  "tools",
    title:  "Tools",
    width:  420,
    height: 320,
});

const all = await window.zero.window.list();
await window.zero.window.focus(win.id);
await window.zero.window.close(win.id);
```

Window calls are subject to the same origin and permission checks as any
other bridge command.

## Platform limits

| Constant | Value |
|---|---|
| `MaxWindows` | 16 |
| `MaxWindowLabelBytes` | 64 |
| `MaxWindowTitleBytes` | 128 |

## Window state persistence

`IWindowStateStore` persists window geometry between launches. The bundled
`JsonWindowStateStore` writes a JSON document to a configurable directory:

```csharp
var store = new JsonWindowStateStore(AppDirs.StateDirectory("dev.example.hello"));

var runtime = new Runtime(new RuntimeOptions
{
    Platform         = platform,
    WindowStateStore = store,
});
```

When the runtime is constructed:

1. The store is queried for a frame matching the main window's `Label`. If
   found, `AppInfo.MainWindow.DefaultFrame` is replaced and the live
   `HWND`/`NSWindow`/`GtkWindow` is resized via
   `IPlatformServices.SetWindowFrame`.
2. Each subsequent `WindowFrameChanged` event is forwarded to the store.

The persisted record contains:

| Field | Description |
|---|---|
| `Id` | Numeric window id. |
| `Label` | Used for merge matching across runs. |
| `Frame` | Position and size (x, y, width, height). |
| `Maximized` | Whether the window was maximized. |
| `Fullscreen` | Whether the window was fullscreen. |
| `Scale` | Display scale factor. |
| `Open` | Whether the window was open at the time of the snapshot. |
| `Focused` | Whether the window was the key window. |

The store merges by `Label` first and falls back to `Id`, so secondary
windows are preserved alongside the main window. Records with empty or
malformed labels are ignored on load and dropped from the next write.

For one-off restore at construction time use `WindowStateRestoration.Apply`:

```csharp
appInfo = WindowStateRestoration.Apply(appInfo, store);
var platform = WebViewPlatform.CreateForCurrentOs(appInfo);
```

## Window events

Each platform backend emits `WindowFrameChanged`, `SurfaceResized`, and
`WindowFocused` events. The runtime persists them through the configured
store and forwards them through your `OnEvent` callback.
