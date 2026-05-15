---
order: 2
title: Quick Start
icon: bolt
tags: [guide]
---

# Quick Start

Go from zero to a running .NET 10 desktop app in a few minutes.

## 1. Create a project

```bash
dotnet new install ZeroNative.Templates
dotnet new zero-native-app -n Hello --BundleId dev.example.hello
cd Hello
```

## 2. Run it

```bash
dotnet run
```

The first run uses the system WebView (WebView2 on Windows, WKWebView on
macOS, WebKitGTK on Linux). A native window opens, loads `wwwroot/index.html`
through the `zero://app/` scheme, and the runtime's trace sink writes
lifecycle events to stderr.

## 3. Wire a bridge command

Open `Program.cs` and register a handler:

```csharp
using ZeroNative.Bridge;
using ZeroNative.Security;

var registry = new BridgeRegistry()
    .Register(new BridgeHandler("native.ping", invocation =>
    {
        return $"{{\"pong\":true,\"echo\":{invocation.Request.Payload}}}";
    }));

var dispatcher = new BridgeDispatcher
{
    Policy = new BridgePolicy(
        Enabled: true,
        Commands: new[]
        {
            new BridgeCommandPolicy("native.ping", Origins: new[] { "zero://app" }),
        }),
    Registry = registry,
};

var runtime = new Runtime(new RuntimeOptions
{
    Platform         = platform,
    BridgeDispatcher = dispatcher,
    Security         = new SecurityPolicy(
        Navigation: new NavigationPolicy(AllowedOrigins: new[] { "zero://app" })),
    JsWindowApi = true,
});
```

Then call it from `wwwroot/index.html`:

```html
<button id="ping">Ping native</button>
<pre id="out"></pre>
<script>
  document.getElementById('ping').addEventListener('click', async () => {
    const result = await window.zero.invoke('native.ping', { from: 'browser' });
    document.getElementById('out').textContent = JSON.stringify(result, null, 2);
  });
</script>
```

`window.zero` is injected by the runtime on document-create. See the
[Bridge](/reference/bridge) reference for the full protocol.

## 4. Package it

```bash
dotnet publish -c Release -r win-x64 --self-contained
dotnet publish -c Release -r osx-arm64 -p:ZeroNativeBundleApp=true -p:ZeroNativeBundleId=dev.example.hello
dotnet publish -c Release -r linux-x64 --self-contained
```

On macOS, `ZeroNativeBundleApp=true` assembles `Hello.app/Contents/MacOS`,
`Contents/Resources`, and an `Info.plist` next to the published binary. For
self-updating installers, see [Velopack packaging](/guides/packaging#velopack).

## 5. Use CEF instead of the system WebView

Switch the host package and the platform factory:

```xml
<PackageReference Include="ZeroNative.Cef" Version="*" />
```

```csharp
using ZeroNative.Cef;

var platform = CefPlatform.CreateForCurrentOs(appInfo, new CefPlatformOptions());
```

The rest of `Runtime`, `AppBuilder`, `BridgeDispatcher`, and `SecurityPolicy`
are identical. CEF bundles Chromium per RID via the `CefGlue.Common`
dependency. See [Web Engines](/reference/web-engines).
