---
order: 6
title: Web Engines
icon: globe
tags: [reference]
---

# Web Engines

zero-native has one app/runtime API and selectable web-engine backends. The
default is the platform system WebView. CEF is available as a separate NuGet
package when you need Chromium parity.

## Support matrix

| Platform | System WebView | CEF |
|---|---|---|
| Windows | WebView2 (Edge Chromium) | CefGlue.Common 120.x |
| macOS | WKWebView | CefGlue.Common 120.x (osx-x64); osx-arm64 needs `tools/stage-cef-macos-arm64.sh` |
| Linux | WebKitGTK (4.1 → 4.0 → 6.0 ABI) on GTK3 or GTK4 | CefGlue.Common 120.x |

The parity contract is the same `AppInfo`, the same `Runtime`, the same
built-in bridge commands, and the same security policy semantics regardless
of which engine you pick.

## System WebView

```csharp
using ZeroNative.Platform;

var platform = WebViewPlatform.CreateForCurrentOs(appInfo);
```

`WebViewPlatform.CreateForCurrentOs` picks the right backend at runtime:

| OS detection | Backend type |
|---|---|
| `RuntimeInformation.IsOSPlatform(Windows)` | `WebView2Platform` |
| `RuntimeInformation.IsOSPlatform(OSX)` | `WKWebViewPlatform` |
| `RuntimeInformation.IsOSPlatform(Linux)` | `WebKitGtkPlatform` |

System mode has no bundled browser. Rendering follows the user's OS install.
The package multi-targets `net10.0-windows10.0.19041.0` for WebView2 so the
WebView2 dependency only ships on Windows TFMs.

## CEF (Chromium Embedded Framework)

```csharp
using ZeroNative.Cef;

var platform = CefPlatform.CreateForCurrentOs(appInfo, new CefPlatformOptions
{
    AssetScheme   = "zero",
    CefDirectory  = null,  // null → use CefGlue's per-RID binaries
});
```

`ZeroNative.Cef` references `CefGlue.Common 120.x`, which transitively pulls
per-RID CEF binaries via `chromiumembeddedframework.runtime.*` and
`cef.redist.*` for:

- `win-x64`, `win-arm64`
- `linux-x64`, `linux-arm64`
- `osx-x64`

macOS arm64 is the lone gap because CefGlue does not publish
`cef.redist.osxarm64`. Run `tools/stage-cef-macos-arm64.sh` to stage the
Spotify CDN build under `runtimes/osx-arm64/native/` and point
`CefPlatformOptions.CefDirectory` at it.

`CefPlatformOptions` fields:

| Field | Default | Description |
|---|---|---|
| `AssetScheme` | `"zero"` | Scheme registered with the resource handler. Matches `WebViewSource.Assets.Origin`. |
| `CefDirectory` | `null` | Override the CEF binary location. Useful for macOS arm64 and bespoke CEF layouts. |
| `CommandLineArgs` | `[]` | Extra Chromium command-line flags. |

## Choosing an engine

| Consideration | System WebView | CEF |
|---|---|---|
| Bundle size | Minimal – uses the OS browser | Large – CEF binaries are tens of MB per RID |
| Rendering consistency | Varies by OS version | Consistent if you ship the same CEF build |
| Startup time | Fastest | Slower because CEF initialises Chromium |
| Best fit | Small apps, OS-native footprint, minimal deps | Apps that need Chromium behaviour or tight rendering control |

Both engines register a custom resource scheme (`zero://app/` by default)
that maps to disk through the shared `AssetServer`. Both honour the same
`SecurityPolicy.DecideNavigation` verdict for navigations and popups.

## Switching engines in code

The platform factory is the only line that changes:

```csharp
// System WebView
var platform = WebViewPlatform.CreateForCurrentOs(appInfo);

// CEF
var platform = CefPlatform.CreateForCurrentOs(appInfo, new CefPlatformOptions());
```

Everything else – the `Runtime`, the `AppBuilder`, the `BridgeDispatcher`,
the `SecurityPolicy` – is identical.
