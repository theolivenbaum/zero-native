# ZeroNative

Build native desktop apps with a web UI using the **system WebView**.

This unified package bundles three native backends and selects the right one for the current OS at runtime:

| OS      | Backend                                                                 |
| ------- | ----------------------------------------------------------------------- |
| Windows | [Microsoft Edge WebView2](https://learn.microsoft.com/en-us/microsoft-edge/webview2/) |
| macOS   | WKWebView (via the Objective-C runtime)                                 |
| Linux   | WebKitGTK 4.1 (with a 4.0 fallback) via GTK3                            |

```csharp
using ZeroNative;
using ZeroNative.Bridge;
using ZeroNative.Platform;
using ZeroNative.Runtime;
using ZeroNative.Security;

var appInfo = new AppInfo
{
    AppName = "Hello",
    BundleId = "dev.example.hello",
    MainWindow = new WindowOptions { DefaultFrame = new(0, 0, 960, 600) },
};

var platform = WebViewPlatform.CreateForCurrentOs(appInfo);
var runtime  = new Runtime(new RuntimeOptions { Platform = platform });
var app      = new AppBuilder()
    .Named("hello")
    .WithSource(WebViewSource.Html("<h1>Hello, zero-native</h1>"))
    .Build();

runtime.Run(app);
```

## JS → .NET bridge

The package injects a small JS shim at document creation:

```js
const result = await window.zero.invoke("native.ping", { from: "browser" });
```

Register handlers on the .NET side:

```csharp
var registry = new BridgeRegistry()
    .Register(new BridgeHandler("native.ping",
        inv => """{"pong":true}"""));

var dispatcher = new BridgeDispatcher
{
    Policy = new BridgePolicy(Enabled: true,
        Commands: new[] { new BridgeCommandPolicy("native.ping", Origins: new[] { "*" }) }),
    Registry = registry,
};
```

## Platform support

- Windows 10 1809+ / 11 (WebView2 Runtime required, ships with Windows 11)
- macOS 11+
- Linux distributions with `libgtk-3.so.0` and `libwebkit2gtk-4.1.so.0` or `libwebkit2gtk-4.0.so.37`

Both `x64` and `arm64` architectures are supported.

## Companion package

For Chromium-based rendering (consistent across OS versions), use [`ZeroNative.Cef`](https://www.nuget.org/packages/ZeroNative.Cef/).
