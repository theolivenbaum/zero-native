# TODO

Open work to bring the .NET 10 port to parity with the original Zig
implementation. Items are roughly grouped by subsystem and ordered by impact.

> Completed items are marked `[x]`; this file is updated as work lands.

## Platform backends

### Windows (WebView2)

- [x] **Multi-window support.** `WebView2Platform` tracks
      `(WindowId, HWND)` pairs and `CreateWindow` spawns additional Win32
      HWNDs against the shared WebView2 environment. The primary HWND is
      flagged so closing it (or the last open window) ends the loop.
      Hosting an additional `CoreWebView2Controller` per HWND remains
      future work — secondary windows currently render an empty Win32
      shell until the controller fan-out lands.
- [x] **DPI awareness.** `SetProcessDpiAwarenessContext(PER_MONITOR_V2)`
      is called before any HWND is created (with a `SetProcessDpiAwareness`
      fallback for older Windows). `GetDpiForWindow` is read on creation
      and `WM_DPICHANGED` is forwarded as a `SurfaceResized` carrying the
      new scale factor.
- [x] **Open / save / message dialogs.** Implemented in
      `Win32Dialogs.cs` using `GetOpenFileNameW`/`GetSaveFileNameW`/`MessageBoxW`.
      Future work: migrate to the COM `IFileOpenDialog`/`IFileSaveDialog`
      APIs for the modern shell experience.
- [x] **Tray icon.** `Win32Tray` wraps `Shell_NotifyIconW` (NIM_ADD /
      NIM_DELETE), builds the popup menu via `CreatePopupMenu` +
      `AppendMenuW`, and tracks clicks via a custom `WM_USER + 1` message
      routed through the shared WndProc to `TrackPopupMenu` (returning
      the selected command id as a `PlatformEvent.TrayAction`).
- [x] **Window events.** `Win32.WindowCallbacks` forwards `WM_SIZE`,
      `WM_MOVE`, and `WM_ACTIVATE` so the runtime sees
      `SurfaceResized` / `WindowFrameChanged` / `WindowFocused`.
- [x] **Clipboard.** `Win32Clipboard` reads/writes Unicode text via
      `OpenClipboard` / `GetClipboardData(CF_UNICODETEXT)` /
      `SetClipboardData(CF_UNICODETEXT)`, wired through
      `IPlatformServices.ReadClipboard` / `WriteClipboard`.
- [x] **Bridge inbound channel.** `AddScriptToExecuteOnDocumentCreatedAsync`
      injects the shared `BridgeJavascript` shim and `WebMessageReceived`
      forwards payloads back to the runtime.
- [x] **Custom scheme handler for assets.** `WebView2Platform` registers
      `AddWebResourceRequestedFilter(origin/*, All)` for the configured
      asset origin and answers each `WebResourceRequested` event from a
      shared `AssetServer` so a `zero://app/` URL maps to disk.
- [x] **Navigation policy enforcement.** `NavigationStarting` and
      `NewWindowRequested` consult the configured `SecurityPolicy` via
      `DecideNavigation` and apply the resulting cancel / open-externally
      decision (the latter via `Process.Start` with `UseShellExecute`).
      Data / about / file / asset-origin URIs are exempted so the initial
      document load is never blocked.
- [x] **Window-state restore.** Apps can now call
      `WindowStateRestoration.Apply(appInfo, store)` to fold persisted
      window geometry into `AppInfo` before constructing the platform,
      and the runtime additionally honors the store for windows created
      at runtime via `Runtime.CreateWindow`. Per-platform startup wiring
      (resizing the actual HWND/NSWindow without going through AppInfo)
      remains TODO.

### macOS (WKWebView)

- [x] **Bridge inbound channel.** A `WKUserContentController` injects the
      `BridgeJavascript` shim at document start and the platform registers
      the script-message handler name. Wiring the actual ObjC handler
      object (so messages from JS reach managed code) is still required.
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
      `CFBundleIdentifier`, icons, and entitlements.
- [ ] **Universal binaries.** Wire `RuntimeIdentifiers` for `osx-x64;osx-arm64`
      in the samples and document `lipo`-style packaging.

### Linux (WebKitGTK)

- [x] **User-content manager.** The platform injects the `BridgeJavascript`
      shim via `webkit_user_content_manager_add_script` and registers a
      `script-message-received` handler. JS payloads are read out of the
      `WebKitJavascriptResult` and forwarded to the runtime.
- [x] **Custom URI scheme.** `WebKitGtkPlatform` registers a
      `webkit_web_context_register_uri_scheme` callback that resolves the
      request through the shared `AssetServer` and answers with a
      `g_memory_input_stream_new_from_bytes` body via
      `webkit_uri_scheme_request_finish`.
- [ ] **GTK4 + WebKitGTK 6.0 path.** Modern distros ship GTK4 first; add
      runtime probing for `libwebkitgtk-6.0` alongside the current
      `webkit2gtk-4.1` / `4.0` fallback chain.
- [x] **Open / save / message dialogs** via `GtkFileChooserDialog` and
      `GtkMessageDialog`. See `GtkDialogs.cs` — file filters, multi-select,
      and primary/secondary/tertiary button mapping are all wired through
      `IPlatformServices.ShowOpenDialog` / `ShowSaveDialog` / `ShowMessageDialog`.
- [x] **Tray** via `libayatana-appindicator3-1`. `AyatanaIndicator`
      probes the library at runtime; when missing the platform raises a
      `UnsupportedServiceException` for `CreateTray` so callers can
      detect the gap. Menu items map activations back to
      `PlatformEvent.TrayAction`.
- [x] **Clipboard** via `gtk_clipboard_get(GDK_SELECTION_CLIPBOARD)` +
      `gtk_clipboard_set_text` / `gtk_clipboard_wait_for_text`, wired through
      `IPlatformServices.ReadClipboard` / `WriteClipboard`.
- [x] **Window resize/focus events.** `configure-event` reads
      `gtk_window_get_position` + `gtk_window_get_size` and emits
      `WindowFrameChanged` (plus `SurfaceResized` for the primary window);
      `focus-in-event` emits `WindowFocused`.
- [x] **Navigation policy.** Hooks the `decide-policy` signal,
      resolves the requested URI through
      `webkit_navigation_policy_decision_get_navigation_action` →
      `webkit_navigation_action_get_request` → `webkit_uri_request_get_uri`,
      and applies the SecurityPolicy verdict via
      `webkit_policy_decision_use` / `webkit_policy_decision_ignore`.
      External links route through `Process.Start(UseShellExecute=true)`.

### CEF (CefGlue)

- [ ] **macOS arm64 binary distribution.** CefGlue.Common only publishes
      `cef.redist.osx64` on NuGet. Provide either a build script that
      downloads `cef-builds.spotifycdn.com/cef_binary_*_macosarm64.tar.bz2`
      and stages it under the user's `runtimes/osx-arm64/native/` directory,
      or document a manual `CefPlatformOptions.CefDirectory` workflow.
- [x] **Bridge inbound.** `CefRenderHandler` registers a V8 `__zero_native_send`
      function in the renderer process; `CefClientImpl.OnProcessMessageReceived`
      receives the `CefProcessMessage` in the browser process and forwards
      the payload to the runtime.
- [ ] **Multi-window.** Track `(WindowId, CefBrowser)` pairs and use
      `CefBrowserHost.CreateBrowser` per new window. Map
      `WindowOptions.DefaultFrame` to `CefWindowInfo.Bounds`.
- [x] **Resource interception** for `WebViewSource.Assets`. A
      `ZeroSchemeHandlerFactory` is registered as a `CustomScheme` at
      `CefRuntimeLoader.Initialize`; each request flows through
      `ZeroAssetResourceHandler` which streams bytes back from a shared
      `AssetServer`. The asset scheme defaults to `"zero"` and is
      configurable via `CefPlatformOptions.AssetScheme`.
- [x] **Navigation policy** via `CefRequestHandler.OnBeforeBrowse`,
      delegating the decision to `SecurityPolicy.DecideNavigation`.
      Data / about / file URIs are exempted so initial loads work.
- [x] **External link policy** via `CefLifeSpanHandler.OnBeforePopup`.
      Cancels the popup and, when the policy says so, opens the URL
      via `Process.Start(UseShellExecute=true)`.

## Core / shared

- [x] **Async bridge handlers.** `BridgeDispatcher.DispatchAsync` now
      prefers async handlers when registered; the runtime calls it and
      blocks on synchronous completions, deferring response delivery
      through `IPlatformServices.CompleteWindowBridge` for true async
      handlers.
- [x] **`AssetServer` content negotiation + SPA fallback.** Adds the
      `Resolve` API that returns content type + status code, honors the
      `SpaFallback` flag for path-style routes, and rejects requests
      that escape the asset root.
- [x] **Window-state store.** `IWindowStateStore` + `JsonWindowStateStore`
      under `ZeroNative.Core/WindowState/`. The runtime persists every
      `WindowFrameChanged` event when a store is configured.
- [x] **Trace sink helpers.** `TraceSinks.Console`, `TraceSinks.JsonFile`,
      `TraceSinks.Tee`, `TraceSinks.WithMinLevel`, and `TraceSinks.Null`
      for common pipelines.
- [x] **`ILogger` adapter.** `TraceSinks.FromLogger(...)` accepts a
      delegate matching `Microsoft.Extensions.Logging.ILogger.Log` and
      maps trace levels to `LogLevel` integers, all without taking a
      dependency on the logging package from Core.
- [x] **Automation server.** `ZeroNative.Automation` ports the Zig
      `src/automation/` subsystem to Core. `AutomationProtocol` parses
      `reload` / `wait` / `bridge` lines, `AutomationSnapshot` writes the
      text + a11y + windows artifacts, and `AutomationServer` reads/writes
      them under a configurable directory. `Runtime.BuildAutomationInput()`
      snapshots window list + diagnostics for the harness.
- [x] **Extensions / module registry.** `ZeroNative.Extensions` recreates
      the Zig `src/extensions/root.zig` model in idiomatic C#:
      `IModule` (with default no-op hooks) + a `ModuleRegistry` that
      validates duplicate ids / missing dependencies, starts modules in
      order, stops in reverse, and routes targeted / broadcast commands.
      A `DelegateModule` convenience covers the common case without
      writing a new class.
- [x] **`AppManifest` JSON parsing.** `AppManifestJson.Parse` /
      `AppManifestJson.Load` reads a JSON manifest mirroring the Zig
      `app.zon` schema (snake_case keys, semver-ish version with
      pre-release/build, identity, bridge commands, security policy,
      frontend dev config, windows, CEF, package, updates). A TOML
      loader is still open.
- [ ] **Strong-name / signing.** Decide on an `AssemblyOriginatorKeyFile`
      and Authenticode/macOS notarization story for the published NuGets.

## Tests

- [x] Round-trip tests for the JSON helpers
      (`JsonUtilities.StringField` / `NumberField` / `BoolField`) including
      nested objects, escapes, unicode, and malformed inputs.
- [x] Async dispatch tests covering preferred-async, fallback-sync, and
      sync-dispatch-of-async-only-handler error reporting.
- [x] `AssetServer` tests covering SPA fallback, scheme stripping,
      path-traversal protection, and content-type guessing.
- [x] `JsonWindowStateStore` tests covering save/load, replacement,
      removal, and corrupt-file tolerance.
- [x] `TraceSinks` tests for ndjson output, multi-sink fan-out, and
      level filtering.
- [x] `BridgeJavascript` channel tests confirming the right transport
      snippet ships per host.
- [x] `AutomationProtocol` / `AutomationSnapshot` / `AutomationServer`
      tests covering command parsing, snapshot formatting, file
      round-trips, and the `Runtime.BuildAutomationInput()` integration.
- [x] `ModuleRegistry` tests covering start/stop ordering, targeted
      dispatch, duplicate-id and missing-dependency rejection, and
      exception wrapping into `ModuleFailedException`.
- [ ] Smoke-test each platform backend on its target OS (CI matrix:
      `windows-latest`, `macos-latest`, `ubuntu-latest`, plus arm64
      runners once available).
- [ ] Snapshot tests for `Runtime` lifecycle traces (capture
      `TraceRecord` sequence and diff against golden files).

## Tooling / packaging

- [x] **Per-package READMEs** embedded in the NuGet via
      `PackageReadmeFile`. All three packages (`ZeroNative.Core`,
      `ZeroNative`, `ZeroNative.Cef`) now ship with usage docs.
- [x] **GitHub Actions matrix.** `.github/workflows/dotnet.yml` builds
      and tests on `windows-latest`, `macos-latest`, and `ubuntu-latest`,
      then packs `ZeroNative.Core`, `ZeroNative`, and `ZeroNative.Cef`
      and uploads the `.nupkg` artifacts.
- [ ] **`dotnet pack --version` driven from CI tags.** Currently the
      `Directory.Build.props` pins `Version=0.1.0`. Pull from
      `GITVERSION_*` or the tag at pack time.
- [ ] **`dotnet new` template.** The Zig original had a `zero-native init`
      CLI. Provide a `dotnet new zero-native` template that scaffolds an
      app project with a sensible `Program.cs`, an `app.json` (or `app.zon`
      compatible reader), and a starter HTML page.
- [x] **Source link.** `Directory.Build.props` adds
      `Microsoft.SourceLink.GitHub` to every packable project and turns on
      `PublishRepositoryUrl` / `EmbedUntrackedSources` /
      `ContinuousIntegrationBuild` so package consumers can step into the
      shipped sources.

## Documentation

- [ ] Port the docs site under `.reference/docs/` (Next.js MDX) to point
      at the C# API surface, or replace with DocFX / .NET docs.
- [x] Per-package READMEs that get embedded in the NuGet.
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
