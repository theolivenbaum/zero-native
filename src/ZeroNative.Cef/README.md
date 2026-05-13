# ZeroNative.Cef

Build native desktop apps with a web UI rendered by the **Chromium Embedded Framework** (CEF), via the [CefGlue.Common](https://www.nuget.org/packages/CefGlue.Common/) binding.

This package gives you a pinned Chromium engine instead of relying on whatever browser the OS provides — useful when you need predictable rendering, modern web platform features, and a known V8 version on every target.

## Platform support

CEF binaries ship transitively from `CefGlue.Common` per RID. Set `CefGlueTargetPlatform` in your `.csproj`:

| RID          | Supported |
| ------------ | --------- |
| `win-x64`    | ✓         |
| `win-arm64`  | ✓         |
| `linux-x64`  | ✓         |
| `linux-arm64`| ✓         |
| `osx-x64`    | ✓         |
| `osx-arm64`  | ✗ (manual — `cef.redist.osxarm64` is not on NuGet) |

```xml
<PropertyGroup>
  <CefGlueTargetPlatform>linux-x64</CefGlueTargetPlatform>
</PropertyGroup>
```

For `osx-arm64`, fetch a CEF distribution from <https://cef-builds.spotifycdn.com/> and point the host at it via `CefPlatformOptions.CefDirectory`.

## Quick start

```csharp
using ZeroNative.Cef;
using ZeroNative.Platform;
using ZeroNative.Runtime;

var appInfo = new AppInfo
{
    AppName = "Hello",
    BundleId = "dev.example.hello",
    MainWindow = new WindowOptions { DefaultFrame = new(0, 0, 960, 600) },
};

var platform = CefPlatform.CreateForCurrentOs(appInfo, new CefPlatformOptions());
var runtime  = new Runtime(new RuntimeOptions { Platform = platform });
var app      = new AppBuilder()
    .Named("hello")
    .WithSource(WebViewSource.Url("https://example.com/"))
    .Build();

runtime.Run(app);
```

## JS → .NET bridge

The renderer-side bridge is exposed as `window.zero.invoke(command, payload?)`, identical to the system-WebView host. Under the hood, calls are forwarded from the renderer process to the browser process via `CefProcessMessage`, then routed to the `BridgeDispatcher`.

## Companion package

If you don't need a pinned Chromium, the lighter [`ZeroNative`](https://www.nuget.org/packages/ZeroNative/) package uses the system WebView and ships much smaller binaries.
