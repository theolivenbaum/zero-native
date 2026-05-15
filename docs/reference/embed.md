---
order: 11
title: Embedded App
icon: layer-group
tags: [reference]
---

# Embedded App

`EmbeddedApp` drives the runtime without the full platform event loop. Use
it when the host already owns the OS message pump — for example when
embedding zero-native inside an existing iOS/Android shell, a game engine,
or a custom render loop.

## Usage

```csharp
using ZeroNative.Runtime;
using ZeroNative.Platform;
using ZeroNative.Primitives;

var embedded = new EmbeddedApp(app, platform);

embedded.Start();

// In your render loop:
embedded.Frame();

// On resize:
embedded.Resize(new Surface(width, height, scale));

// On shutdown:
embedded.Stop();
```

`EmbeddedApp` constructs the underlying `Runtime` for you. Pass a fully
configured `RuntimeOptions` if you need a bridge dispatcher, security
policy, or trace sink:

```csharp
var embedded = new EmbeddedApp(app, new RuntimeOptions
{
    Platform         = platform,
    BridgeDispatcher = dispatcher,
    Security         = securityPolicy,
});
```

## Methods

| Method | Description |
|---|---|
| `Start()` | Dispatch `AppStart` and load the WebView source. |
| `Resize(surface)` | Notify the runtime that the host surface changed. |
| `Frame()` | Pump one runtime frame (lifecycle + bridge dispatch). |
| `Stop()` | Dispatch `AppShutdown`. |
| `Bridge(message)` | Inject a bridge message synthesised by host code. |

`EmbeddedApp` exposes the wrapped `App` and `Runtime` instances via
`embedded.App` and `embedded.Runtime` if you need to call them directly.
