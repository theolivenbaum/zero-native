# CLAUDE.md

Notes for future Claude (or any AI assistant) working on this repository.

## Project overview

This is a **.NET 10 / C# port** of the original Zig framework `zero-native`. The
upstream Zig sources are preserved verbatim in [`.reference/`](./.reference/)
and remain the source of truth for behavior. New work happens in the C#
projects under `src/`, `samples/`, and `tests/`.

The port follows the same conceptual model as the Zig original:

- An `App` describes name, web-view source, and lifecycle hooks.
- A `Runtime` owns the event loop, windows, bridge dispatch, and tracing.
- A `Platform` implementation provides the system-specific event source and
  the services bag (windowing, dialogs, clipboard, tray, etc).
- A `BridgeDispatcher` routes JSON-encoded messages from the WebView to
  registered C# handlers under a permission/origin policy.
- A `SecurityPolicy` controls allowed origins, permission grants, and the
  external-link policy.

## Repository layout

```
src/
  ZeroNative.Core/      cross-platform managed-only library (runtime, bridge,
                        security, manifest, geometry, JSON, asset helpers)
  ZeroNative/           unified system-WebView host package
    Windows/            WebView2 backend (Microsoft.Web.WebView2 + Win32)
    MacOS/              WKWebView backend (Objective-C runtime P/Invoke)
    Linux/              WebKitGTK backend (GTK3 + WebKit2GTK P/Invoke)
    WebViewPlatform.cs  factory that picks the backend for the current OS
  ZeroNative.Cef/       CefGlue-based CEF host package
samples/
  ZeroNative.Sample/        WebView sample with bridge demo
  ZeroNative.Sample.Cef/    CEF sample
tests/
  ZeroNative.Core.Tests/    xUnit coverage
.reference/                 the original Zig sources (read-only reference)
```

## Key design choices

- **One NuGet package per host kind.** `ZeroNative` bundles every system
  WebView backend so a single dependency works on Win/macOS/Linux; OS
  detection happens at runtime in `WebViewPlatform.CreateForCurrentOs`. The
  CEF host is shipped separately as `ZeroNative.Cef` because the CEF binary
  payload is large and not everyone needs it.

- **`ZeroNative.Core` has no native dependencies.** Anything that requires
  Win32/GTK/Cocoa lives in `ZeroNative` or `ZeroNative.Cef`. This keeps
  Core unit-testable in a vanilla CLR.

- **`net10.0` (multi-targeting `net10.0-windows10.0.19041.0` for WebView2).**
  The Core library is pure managed and uses only `net10.0`. The unified
  WebView package multi-targets so the WebView2 dependency only ships on
  Windows TFMs.

- **CefGlue.Common 120.x for CEF.** Picked because it transitively pulls
  per-RID CEF binaries via `chromiumembeddedframework.runtime.*` and
  `cef.redist.*` for **win-x64, win-arm64, linux-x64, linux-arm64, osx-x64**.
  macOS arm64 is the lone gap (CefGlue does not publish `cef.redist.osxarm64`)
  — users targeting it must supply CEF binaries via `CefPlatformOptions.CefDirectory`.

- **Records and `with` everywhere** for option/state types. Most types are
  immutable; mutation goes through `state with { ... }`.

- **No backwards-compat shims.** This is a fresh port; the Zig API surface
  is recreated in idiomatic C# without trying to preserve every Zig naming
  quirk (e.g. `snake_case` becomes `PascalCase`).

## Building and testing

```bash
dotnet restore
dotnet build              # builds the entire solution
dotnet test               # runs the xUnit tests
dotnet pack -c Release src/ZeroNative/ZeroNative.csproj
dotnet pack -c Release src/ZeroNative.Cef/ZeroNative.Cef.csproj
```

Linux test runs require nothing special; the WebView2 TFM only restores
WebView2 on Windows. The `net10.0-windows10.0.19041.0` target framework
needs `EnableWindowsTargeting=true` to build on non-Windows machines
(already set in `src/ZeroNative/ZeroNative.csproj`).

## Bridge protocol

Identical to the Zig version. JSON request envelopes:

```json
{ "id": "<string>", "command": "<dotted.name>", "payload": <any-json> }
```

Responses:

```json
{ "id": "<id>", "ok": true, "result": <any-json> }
{ "id": "<id>", "ok": false, "error": { "code": "<code>", "message": "..." } }
```

Built-in window/dialog commands live under the `zero-native.window.*` and
`zero-native.dialog.*` namespaces and are gated by either the explicit
`builtin_bridge` policy or the implicit `JsWindowApi`/`window` permission flow.

## Coding conventions for this repo

- Prefer records and `init`-only properties for option/state types.
- Use `LibraryImport` / source-generated P/Invoke (not `DllImport`) where
  available. The Win32 host still uses `DllImport` for the WndProc thunk
  because of the delegate-marshaling pattern.
- Always keep the original Zig logic in `.reference/` as the canonical
  behavior reference — when in doubt, compare to the Zig original.
- Tests should live next to the affected subsystem in
  `tests/ZeroNative.Core.Tests/` (one file per concept).
- Don't add new top-level docs without explicit user request.

## Branches and commits

The active development branch is `claude/dotnet10-csharp-port-IVoFY`. All
.NET work goes there until merged upstream.

Commit messages should be specific about what changed, not just "fix port".
Follow the existing style:

> Initial .NET 10 / C# port of zero-native
> Moves the original Zig sources to .reference/ and replaces them ...

## Known limitations / what's next

See [TODO.md](./TODO.md) for the open work list.
