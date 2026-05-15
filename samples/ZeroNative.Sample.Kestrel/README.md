# ZeroNative.Sample.Kestrel

Reference for the "C# everywhere" path: a ZeroNative desktop shell pairs with an
in-process Kestrel server so the WebView fetches managed code by URL instead of
through the JSON command bridge.

## What this sample shows

- `WebApplication.CreateSlimBuilder` is wired in `Program.cs`. Kestrel listens
  on `http://127.0.0.1:0` (ephemeral port). Static assets ship from `wwwroot/`,
  and `/api/echo` plus `/api/info` are mapped as minimal-API endpoints.
- After `web.StartAsync()` resolves the bound URL, `WebViewSource.Url(baseUrl)`
  points the WebView at it. `SecurityPolicy.Navigation.AllowedOrigins` is
  narrowed to that single origin, so the WebView refuses to wander.
- A couple of bridge commands (`native.clipboard`, `native.shell.open`) stay
  available for the things HTTP cannot do, demonstrating that the bridge and the
  data plane coexist.
- Lifecycle is tied: when the runtime stops (last window closes), the host
  awaits `web.StopAsync` before returning.

## Running

```bash
dotnet run --project samples/ZeroNative.Sample.Kestrel
```

The desktop window opens against the local Kestrel URL — stderr shows the
resolved address (e.g. `[kestrel] listening at http://127.0.0.1:54321`).

## Bringing your own frontend (Tesserae / H5 / SPA bundlers)

`wwwroot/` is a regular static-files root, so any frontend that compiles to
HTML + JS slots in:

- **Tesserae / H5.** Run `h5-compiler` against a separate UI project and copy
  the emitted `bin/Debug/netstandard2.0/h5/` output into `wwwroot/`. The
  ZeroNative host code does not need to change.
- **Vite / Next / esbuild.** Build into `wwwroot/` (or a dist dir copied at
  publish-time).
- **Hot reload.** For development, spawn the bundler via
  `ZeroNative.Tooling.DevServer.Start(...)` and pass the resolved dev URL to
  `WebViewSource.Url(...)` instead of starting Kestrel.

When Tesserae owns the frontend, the same Kestrel app keeps serving `/api/*`
endpoints — the H5-emitted JS uses `fetch` exactly like the included
`app.js` does.
