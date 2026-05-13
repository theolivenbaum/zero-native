# ZeroNative.Core

Cross-platform, host-agnostic abstractions for [zero-native](https://github.com/theolivenbaum/zero-native) .NET apps.

This package has no native dependencies. It defines:

- The `Runtime` event loop and lifecycle model.
- The `BridgeDispatcher` for routing JSON commands from JavaScript to C#.
- A `SecurityPolicy` with origin allowlists, permission grants, and external-link rules.
- The `AppManifest` types and a slim validator.
- A `JsonWindowStateStore` to persist window geometry across runs.
- Geometry types, JSON helpers, asset server, app-dirs resolver, trace sinks.

You usually consume Core indirectly through one of the host packages:

- **`ZeroNative`** — system WebView (WebView2 / WKWebView / WebKitGTK).
- **`ZeroNative.Cef`** — Chromium Embedded Framework via CefGlue.

```csharp
using ZeroNative.Bridge;
using ZeroNative.Runtime;
using ZeroNative.Security;

var dispatcher = new BridgeDispatcher
{
    Policy = new BridgePolicy(Enabled: true,
        Commands: new[] { new BridgeCommandPolicy("native.ping", Origins: new[] { "*" }) }),
    Registry = new BridgeRegistry()
        .Register(new BridgeHandler("native.ping", inv => """{"pong":true}""")),
};
```
