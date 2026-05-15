---
order: 4
title: Frontend Projects
icon: code
tags: [guide]
---

# Frontend Projects

For apps with a build step (React, Vue, Svelte, Vite, Next.js…), zero-native
provides two paths:

1. Serve the built output through the `zero://app/` asset scheme.
2. Point the WebView at a local dev server during development.

## Built output → asset scheme

After `npm run build`, point `WebViewSource.Assets` at the build directory:

```csharp
var source = WebViewSource.Assets(new WebViewAssetSource
{
    RootPath    = "wwwroot",   // or "dist", or "out", …
    Entry       = "index.html",
    Origin      = "zero://app",
    SpaFallback = true,
});
```

`SpaFallback=true` returns the entry file for any path that does not resolve
to an asset on disk, so client-side routers like React Router or Next.js
static export keep working.

`AssetServer` performs content-type sniffing by file extension, rejects path
traversal (`..` outside the root), and is consumed by every backend's
resource handler.

## Dev server during development

`ZeroNative.Tooling.DevServer` mirrors the Zig `tooling/dev.zig` helper. It
parses a dev URL, optionally spawns the dev command, polls until the server
answers a 2xx/3xx response, and returns a handle that owns the child process.

```csharp
using ZeroNative.Tooling;

await using var dev = await DevServer.StartAsync(new DevServerOptions
{
    Url     = "http://127.0.0.1:5173",
    Command = "npm run dev",
    Cwd     = "frontend",
    Timeout = TimeSpan.FromSeconds(15),
});

var source = WebViewSource.Url(dev.ResolvedUrl);
```

When the dev URL is reachable already, `DevServer.StartAsync` skips spawning
and just resolves the URL. The returned handle's `DisposeAsync` shuts the
child down cleanly.

## Switching by environment variable

A common pattern is to check an environment variable so the same `Program.cs`
serves the dev server in development and bundled assets in production:

```csharp
var devUrl = Environment.GetEnvironmentVariable("ZERO_NATIVE_FRONTEND_URL");
WebViewSource source = devUrl is { Length: > 0 }
    ? WebViewSource.Url(devUrl)
    : WebViewSource.Assets(new WebViewAssetSource
        {
            RootPath = "wwwroot",
            Entry    = "index.html",
            Origin   = "zero://app",
        });
```

`launchSettings.json` (or your IDE run config) sets `ZERO_NATIVE_FRONTEND_URL`
for the dev run; release builds leave it unset.

## Allowing the dev origin

Add the dev origin to the navigation policy:

```csharp
var security = new SecurityPolicy(
    Navigation: new NavigationPolicy(AllowedOrigins: new[]
    {
        "zero://app",
        "http://127.0.0.1:5173",
    }));
```

Keep the development and production policies separate where possible.

## Kestrel data plane

For apps that need a real ASP.NET back end inside the same process, see
`samples/ZeroNative.Sample.Kestrel`. It boots a `WebApplication.CreateSlimBuilder`
on `127.0.0.1:0`, narrows `NavigationPolicy.AllowedOrigins` to the resolved
origin, and points `WebViewSource.Url(…)` at it. The frontend then calls
`/api/echo` via `fetch` while still using `window.zero.invoke(...)` for
bridge commands.
