---
order: 1
title: Getting Started
icon: rocket
tags: [guide]
---

# Getting Started

Install the SDK, scaffold a project, run it.

## Prerequisites

- .NET 10 SDK
- On Windows: the WebView2 runtime is preinstalled on Windows 11 / current
  Windows 10 builds. Older Windows 10 needs the
  [Evergreen WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/).
- On Linux: `libwebkit2gtk-4.1-0` (or `4.0` / `6.0`) and GTK3 (or GTK4). The
  Linux backend probes for whichever ABI is present.
- On macOS: nothing extra. WKWebView ships with the OS.

## Install the templates

```bash
dotnet new install ZeroNative.Templates
```

## Scaffold a new app

```bash
dotnet new zero-native-app -n MyApp --BundleId dev.example.myapp
cd MyApp
dotnet run
```

For a CEF-backed app pass `--UseCef`:

```bash
dotnet new zero-native-app -n MyChromiumApp --BundleId dev.example.cef --UseCef
```

The template produces:

| File | Purpose |
|---|---|
| `Program.cs` | Top-level entry point that constructs `AppInfo`, the platform, and a `Runtime`, then calls `runtime.Run(app)` |
| `MyApp.csproj` | Targets `net10.0` (and `net10.0-windows…` for the WebView2 TFM); references `ZeroNative` or `ZeroNative.Cef` |
| `app.json` | Optional JSON manifest mirroring the Zig `app.zon` schema; parse with `AppManifestJson.Load(...)` |
| `wwwroot/index.html` | Static frontend served through the `zero://app/` scheme |

## Next steps

- [App Model](/reference/app-model) – how `AppInfo`, `AppBuilder`, and `Runtime` fit together
- [Bridge](/reference/bridge) – wire a JSON command from JavaScript to .NET
- [Web Engines](/reference/web-engines) – decide between the system WebView and CEF
- [Packaging](/guides/packaging) – ship a `.msi`, `.app`, or `.AppImage`
