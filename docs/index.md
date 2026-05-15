---
order: 1
title: Introduction
icon: home
---

# zero-native (.NET 10 / C#)

Build native desktop apps with a web UI, in C#. zero-native gives you a small
managed runtime, a JSON bridge between JavaScript and .NET, and a choice of
web engine for each platform.

The original Zig sources live in [`.reference/`](https://github.com/theolivenbaum/zero-native/tree/main/.reference) and
remain authoritative for behaviour. This site documents the .NET 10 port that
ships as three NuGet packages.

## Why .NET 10

- **Single managed runtime** – one C# codebase targets Windows, macOS, and
  Linux. The platform backend is selected at runtime by `WebViewPlatform.CreateForCurrentOs`.
- **Small binaries** – the system-WebView host has no bundled browser. AOT-ready
  Core ships with `IsAotCompatible=true` and uses `JsonSerializerContext` for
  the JSON code paths.
- **Pick your engine** – use the OS WebView (WebView2 / WKWebView / WebKitGTK)
  for small, fast apps. Drop in CEF when you need Chromium parity.
- **Familiar tooling** – `dotnet build`, `dotnet test`, `dotnet pack`. NuGet
  packages, MSBuild targets, and `dotnet new zero-native-app`.

## Packages

| Package | Purpose | Platforms |
|---|---|---|
| `ZeroNative.Core` | Cross-platform abstractions: runtime, bridge, security, manifest. Pure managed. | All `net10.0` |
| `ZeroNative` | Unified system-WebView host. WebView2 + WKWebView + WebKitGTK. | Windows x64+arm64, macOS x64+arm64, Linux x64+arm64 |
| `ZeroNative.Cef` | CEF (Chromium Embedded Framework) host based on CefGlue. | Windows x64+arm64, Linux x64+arm64, macOS x64 |

## Hello world

```csharp
using ZeroNative;
using ZeroNative.Platform;
using ZeroNative.Primitives;
using ZeroNative.Runtime;

var appInfo = new AppInfo
{
    AppName  = "Hello",
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

Run it with `dotnet run`. Read the full [Quick Start](/guides/quick-start) for
project scaffolding, the bridge, and packaging.

## Learn more

- [Quick Start](/guides/quick-start) – scaffold and run your first app
- [App Model](/reference/app-model) – `AppInfo`, `AppBuilder`, `Runtime`
- [Bridge](/reference/bridge) – call .NET from JavaScript
- [Windows](/reference/windows) – multi-window APIs and state persistence
- [Security](/reference/security) – permissions, origins, navigation
- [Web Engines](/reference/web-engines) – system WebView vs CEF
- [Packaging](/guides/packaging) – `dotnet publish`, app bundles, Velopack
