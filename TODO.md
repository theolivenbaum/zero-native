# TODO

Open work to bring the .NET 10 port to parity with the original Zig
implementation. Items are roughly grouped by subsystem and ordered by impact.

## Platform backends

### Windows (WebView2)

- [ ] **Multi-window support.** `WebView2Platform` currently only manages
      a single top-level HWND + CoreWebView2. Extend to host multiple
      `(HWND, CoreWebView2Controller)` pairs keyed by `WindowId`.
- [ ] **DPI awareness.** Call `SetProcessDpiAwarenessContext` on startup,
      read scale factor per monitor on `WM_DPICHANGED`, and forward it
      to the runtime's `SurfaceResized` event.
- [ ] **Open / save / message dialogs.** Wrap `IFileOpenDialog`,
      `IFileSaveDialog`, and `MessageBoxW` and implement the
      `IPlatformServices` dialog methods.
- [ ] **Tray icon.** Implement `Shell_NotifyIcon` for `CreateTray` /
      `UpdateTrayMenu` / `RemoveTray`.
- [ ] **Clipboard.** Read/write text via `OpenClipboard` /
      `SetClipboardData(CF_UNICODETEXT)`.
- [ ] **Bridge inbound payload.** Currently we read messages via
      `WebMessageReceived`, but we don't yet expose a JS-side
      `window.zero.invoke()` shim that wraps the response correlation.
      Inject the shim during `AddScriptToExecuteOnDocumentCreatedAsync`.
- [ ] **Custom scheme handler for assets.** Wire
      `CoreWebView2.AddWebResourceRequestedFilter` for `WebViewSource.Assets`
      so a `zero://app/` origin can serve files from the configured
      asset root (matching the Zig asset server behavior).
- [ ] **Navigation policy enforcement.** Hook `NavigationStarting` /
      `NewWindowRequested` to apply `NavigationPolicy.AllowedOrigins`
      and `ExternalLinkPolicy`.
- [ ] **Window-state restore.** Persist position/size to
      `%LOCALAPPDATA%/<BundleId>/window-state.json` (or a configured
      `IWindowStateStore`) and restore on next launch, clamped to a
      visible monitor.

### macOS (WKWebView)

- [ ] **Bridge inbound channel.** Register a `WKScriptMessageHandler`
      under name `zero` so the JS `window.webkit.messageHandlers.zero.postMessage`
      delivers bridge requests back to the platform.
- [ ] **Native event loop wiring.** The current `[NSApp run]` call blocks
      forever — there's no `applicationWillTerminate` plumbing back to
      the runtime. Subclass `NSApplicationDelegate` to forward `terminate:`
      and dock-quit signals as `app_shutdown` events.
- [ ] **Window delegate.** Forward `windowDidResize`, `windowDidMove`,
      `windowDidBecomeKey`, `windowWillClose` to the runtime.
- [ ] **Open / save / message dialogs.** `NSOpenPanel`, `NSSavePanel`,
      `NSAlert` wrappers behind the `IPlatformServices` API.
- [ ] **Tray icon.** `NSStatusBar` + `NSStatusItem` + `NSMenu` integration.
- [ ] **Custom URL scheme.** Implement a `WKURLSchemeHandler` for
      `WebViewSource.Assets` so the configured origin maps to disk.
- [ ] **Bundle / Info.plist generation.** Provide a packing target that
      assembles `.app` bundles with the right `Info.plist`,
      `CFBundleIdentifier`, icons, and entitlements (the Zig version
      had `tooling/codesign.zig`).
- [ ] **Universal binaries.** Wire `RuntimeIdentifiers` for `osx-x64;osx-arm64`
      in the samples and document `lipo`-style packaging.

### Linux (WebKitGTK)

- [ ] **WebKit user-content manager.** Register a script-message handler
      named `zero` via `webkit_user_content_manager_register_script_message_handler`
      so JS->C# bridge calls work.
- [ ] **Custom URI scheme.** Implement `webkit_web_context_register_uri_scheme`
      to serve `WebViewSource.Assets`.
- [ ] **GTK4 + WebKitGTK 6.0 path.** Modern distros ship GTK4 first; add
      runtime probing for `libwebkitgtk-6.0` alongside the current
      `webkit2gtk-4.1` / `4.0` fallback chain.
- [ ] **Open / save / message dialogs** via `GtkFileChooserDialog` and
      `GtkMessageDialog`.
- [ ] **Tray.** GTK no longer has a first-class tray API; integrate with
      `libayatana-appindicator3-1` or document the gap.
- [ ] **Clipboard** via `gtk_clipboard_get(GDK_SELECTION_CLIPBOARD)`.
- [ ] **Window resize/focus events.** Connect `configure-event` and
      `focus-in-event` to forward as `WindowFrameChanged` / `WindowFocused`.

### CEF (CefGlue)

- [ ] **macOS arm64 binary distribution.** CefGlue.Common only publishes
      `cef.redist.osx64` on NuGet. Provide either a build script that
      downloads `cef-builds.spotifycdn.com/cef_binary_*_macosarm64.tar.bz2`
      and stages it under the user's `runtimes/osx-arm64/native/` directory,
      or document a manual `CefPlatformOptions.CefDirectory` workflow.
- [ ] **Bridge inbound.** Use a custom `CefRenderProcessHandler` +
      `CefV8Handler` to expose `window.zero.invoke()` in the page and
      ferry messages back to the browser process via
      `CefProcessMessage`. Tie into `MethodCallHandler`.
- [ ] **Multi-window.** Track `(WindowId, CefBrowser)` pairs and use
      `CefBrowserHost.CreateBrowser` per new window. Map
      `WindowOptions.DefaultFrame` to `CefWindowInfo.Bounds`.
- [ ] **Resource interception** for `WebViewSource.Assets` (CEF custom
      scheme via `CefSchemeHandlerFactory`).
- [ ] **Navigation policy** via `CefRequestHandler.OnBeforeBrowse`.
- [ ] **External link policy** via `CefLifeSpanHandler.OnBeforePopup`.

## Core / shared

- [ ] **Async bridge handlers.** The async path
      (`AsyncBridgeHandler` + `AsyncBridgeResponder`) is wired through
      `BridgeRegistry`, but the `BridgeDispatcher.Dispatch` flow is
      synchronous and does not currently call `FindAsync`. Wire a
      `DispatchAsync(...)` overload and have `Runtime.HandleBridgeMessage`
      prefer the async registry when present.
- [ ] **`AssetServer` content negotiation + SPA fallback.** The class
      reads files, guesses MIME types, and looks up by `Id`/`BundlePath`,
      but the `SpaFallback = true` flag is not yet honored. When a path
      doesn't exist and `SpaFallback` is on, return the configured
      `Entry` (typically `index.html`).
- [ ] **Window-state store.** The Zig version had
      `src/window_state/root.zig`. Port that to an `IWindowStateStore`
      interface in `ZeroNative.Core` (no I/O dependency) plus a default
      JSON-file implementation under `ZeroNative.Core/WindowState/`.
- [ ] **Trace sink helpers.** `Runtime.TraceSink` is a raw
      `Action<TraceRecord>`. Provide a few canned sinks: console,
      rotating file, and an `ILogger` adapter via
      `Microsoft.Extensions.Logging.Abstractions` (optional package
      reference behind a `#if` so Core stays dep-free).
- [ ] **Automation server.** The Zig `src/automation/` subsystem exposes
      a JSON snapshot protocol for end-to-end automation. Port the
      `Server`, `protocol`, and `snapshot` modules to Core.
- [ ] **Extensions / module registry.** `src/extensions/root.zig` is
      not yet ported. Decide whether to recreate it or replace with an
      idiomatic `IServiceCollection`-based extension model.
- [ ] **`AppManifest` parsing.** The Zig version parses `app.zon` files
      and validates them deeply. The C# port has the typed model and
      a slim validator, but no parser. Add a JSON/TOML loader so apps
      can declare their manifest in a config file.
- [ ] **Strong-name / signing.** Decide on an `AssemblyOriginatorKeyFile`
      and Authenticode/macOS notarization story for the published NuGets.

## Tests

- [ ] Add round-trip tests for the JSON helpers
      (`JsonUtilities.StringField` / `NumberField` / `BoolField`)
      against tricky inputs (nested objects, escaped strings,
      `null` payloads, unicode escapes).
- [ ] Add a `BridgeDispatcher` async dispatch test once the path is
      wired (see Core TODO above).
- [ ] Smoke-test each platform backend on its target OS (CI matrix:
      `windows-latest`, `macos-latest`, `ubuntu-latest`, plus arm64
      runners once available).
- [ ] Snapshot tests for `Runtime` lifecycle traces (capture
      `TraceRecord` sequence and diff against golden files).

## Tooling / packaging

- [ ] **GitHub Actions matrix.** Add a workflow that builds Core +
      both unified packages on `windows-latest`, `macos-latest`, and
      `ubuntu-latest`, runs `dotnet test`, and uploads the `.nupkg` /
      `.snupkg` artifacts on tag pushes.
- [ ] **`dotnet pack --version` driven from CI tags.** Currently the
      `Directory.Build.props` pins `Version=0.1.0`. Pull from
      `GITVERSION_*` or the tag at pack time.
- [ ] **README per package** (`PackageReadmeFile`). `dotnet pack`
      currently warns about the missing readme.
- [ ] **`dotnet new` template.** The Zig original had a `zero-native init`
      CLI. Provide a `dotnet new zero-native` template that scaffolds an
      app project with a sensible `Program.cs`, an `app.json` (or `app.zon`
      compatible reader), and a starter HTML page.
- [ ] **Source link.** Add `Microsoft.SourceLink.GitHub` to the packable
      projects so debugging into the package works.

## Documentation

- [ ] Port the docs site under `.reference/docs/` (Next.js MDX) to point
      at the C# API surface, or replace with DocFX / .NET docs.
- [ ] Add per-package READMEs that get embedded in the NuGet (see Packaging
      TODO).
- [ ] Architecture diagram showing `Core` ↔ platform backends ↔ host apps.

## Nice-to-haves

- [ ] **Native AOT support.** Audit the P/Invoke surface for AOT
      compatibility; add `IsAotCompatible=true` and a trim/AOT smoke test.
- [ ] **MAUI / Avalonia integration samples.** Demonstrate hosting
      `Runtime` inside a MAUI or Avalonia app for projects that want
      richer native chrome around the WebView.
- [ ] **Hot reload of the web bundle.** Mirror the Zig `tooling/dev.zig`
      dev-server integration so saving a frontend file triggers a runtime
      reload.
