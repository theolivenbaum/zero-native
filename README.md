# zero-native (.NET 10 port)

Modern .NET 10 / C# port of the original Zig zero-native framework. Build native desktop apps with a web UI using either the system WebView or the Chromium Embedded Framework.

The original Zig sources live in [`.reference/`](./.reference/) and remain authoritative for behavior. This branch ports the API surface to idiomatic C# 12+, targeting `net10.0`.

## Packages

| Package           | Purpose                                                                 | Platforms                                      |
| ----------------- | ----------------------------------------------------------------------- | ---------------------------------------------- |
| `ZeroNative.Core` | Cross-platform abstractions: runtime, bridge, security, manifest types. | All `net10.0`                                  |
| `ZeroNative`      | Unified system-WebView host. Uses WebView2/WKWebView/WebKitGTK.         | Windows x64+arm64, macOS x64+arm64, Linux x64+arm64 |
| `ZeroNative.Cef`  | CEF (Chromium Embedded Framework) host based on CefGlue.                | Windows x64+arm64, Linux x64+arm64, macOS x64       |

## Solution layout

```
src/
  ZeroNative.Core/          # platform-agnostic library
  ZeroNative/               # system WebView host
    Windows/                # WebView2 backend
    MacOS/                  # WKWebView backend
    Linux/                  # WebKitGTK backend
  ZeroNative.Cef/           # CEF host (CefGlue.Common)
samples/
  ZeroNative.Sample/        # WebView sample
  ZeroNative.Sample.Cef/    # CEF sample
tests/
  ZeroNative.Core.Tests/    # xUnit tests
.reference/                 # original Zig sources
```

## Quick start

```csharp
using ZeroNative;
using ZeroNative.Platform;
using ZeroNative.Primitives;
using ZeroNative.Runtime;

var appInfo = new AppInfo
{
    AppName = "Hello",
    BundleId = "dev.example.hello",
    MainWindow = new WindowOptions
    {
        DefaultFrame = new RectF(0, 0, 960, 600),
    },
};

var platform = WebViewPlatform.CreateForCurrentOs(appInfo);
var runtime  = new Runtime(new RuntimeOptions { Platform = platform });
var app      = new AppBuilder()
    .Named("hello")
    .WithSource(WebViewSource.Html("<h1>Hello, zero-native</h1>"))
    .Build();

runtime.Run(app);
```

For CEF, replace `WebViewPlatform.CreateForCurrentOs(appInfo)` with `CefPlatform.CreateForCurrentOs(appInfo, new CefPlatformOptions())` and add `<CefGlueTargetPlatform>` to your project (e.g. `linux-x64`, `win-x64`, `osx-x64`).

## Building

```bash
dotnet restore
dotnet build
dotnet test
```

## Packaging

```bash
dotnet pack -c Release src/ZeroNative/ZeroNative.csproj
dotnet pack -c Release src/ZeroNative.Cef/ZeroNative.Cef.csproj
```

## Status

Initial port. Core abstractions (runtime, bridge, security, manifest) are complete and unit-tested. Platform backends are wired to the appropriate native APIs (WebView2 / WKWebView / WebKitGTK / CefGlue) and ready for further fleshing out (multi-window, dialogs, tray, IPC).
