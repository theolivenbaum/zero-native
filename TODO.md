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
- [x] **Open / save / message dialogs.** Default path is now the modern
      Shell `IFileOpenDialog` / `IFileSaveDialog` COM API (see
      `Win32ShellDialogs.cs`), which produces the Explorer-style chrome with
      places sidebar and breadcrumb path bar. `Win32Dialogs.cs` retains the
      `GetOpenFileNameW`/`GetSaveFileNameW` implementations as a fallback
      when COM instantiation fails, and still owns the `MessageBoxW`
      wrapper used by `ShowMessageDialog`.
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

- [x] **Bridge inbound channel.** A custom `ZeroNativeScriptHandler`
      class (registered via `objc_allocateClassPair` + `class_addMethod`)
      conforms to `WKScriptMessageHandler` and forwards
      `userContentController:didReceiveScriptMessage:` payloads back to
      the runtime, resolving the originating WKWebView so multi-window
      bridge responses route to the right window.
- [x] **Native event loop wiring.** A `ZeroNativeAppDelegate` is
      registered as the `NSApplication` delegate. It implements
      `applicationWillTerminate:` (publishes `AppShutdown`) and
      `applicationShouldTerminateAfterLastWindowClosed:` (returns YES so
      cmd-Q / dock-close ends the runloop cleanly).
- [x] **Window delegate.** `ZeroNativeWindowDelegate` forwards
      `windowDidResize:` / `windowDidMove:` (re-reading the NSWindow
      `frame` and `backingScaleFactor`), `windowDidBecomeKey:`
      (emitting `WindowFocused`), and `windowWillClose:` (emitting
      `WindowFrameChanged` with `Open=false` and terminating the app
      when the primary or last window closes).
- [x] **Open / save / message dialogs.** `NSOpenPanel`, `NSSavePanel`,
      `NSAlert` wrappers behind the `IPlatformServices` API.
      `allowedFileTypes:` carries the configured filter extensions and
      primary/secondary/tertiary buttons map to
      `NSAlertFirstButtonReturn` / `…SecondButtonReturn` / `…ThirdButtonReturn`.
- [x] **Tray icon.** `NSStatusBar` + `NSStatusItem` + `NSMenu` with a
      generated `ZeroNativeTrayTarget` target class. Menu activations
      dispatch through `invoke:` to `PlatformEvent.TrayAction`.
- [x] **Custom URL scheme.** `ZeroNativeUrlSchemeHandler` conforms to
      `WKURLSchemeHandler`. `webView:startURLSchemeTask:` resolves the
      request through the shared `AssetServer`, builds an
      `NSHTTPURLResponse` with the right status / content-type, and
      streams the body back via `didReceiveResponse:` / `didReceiveData:`
      / `didFinish`.
- [x] **Bundle / Info.plist generation.** `ZeroNative.AppBundle.targets`
      ships in the NuGet `build/` directory and is auto-imported by
      consumers. Setting `<ZeroNativeBundleApp>true</ZeroNativeBundleApp>`
      (plus `<ZeroNativeBundleId>…</ZeroNativeBundleId>`) assembles
      `$(OutputPath)$(BundleName).app/Contents/{MacOS,Resources}` with a
      generated `Info.plist`. Codesigning / notarization remains an
      out-of-band step.
- [x] **Universal binaries.** The sample now declares
      `<RuntimeIdentifiers>` for `win-x64;win-arm64;linux-x64;linux-arm64;osx-x64;osx-arm64`,
      so `dotnet publish -r osx-arm64` (or `-r osx-x64`) produces the
      native-AOT-friendly per-RID outputs. Combine the two outputs via
      `lipo -create -output ...` from a CI matrix to ship a fat binary.
- [x] **Clipboard via writable types.** `NSPasteboard` now also reads/writes
      `public.file-url` (single + array, plus the modern `readObjectsForClasses:`
      path) and `public.png` / `public.tiff` image data. The Core
      `IPlatformServices` interface gained matching `ReadClipboardFiles` /
      `WriteClipboardFiles` / `ReadClipboardImage` / `WriteClipboardImage` hooks,
      wired through on Windows (CF_HDROP + CF_DIB) and Linux (`gtk_clipboard_wait_for_uris`).
- [x] **Multi-window WKWebView fan-out.** `CreateWindow` allocates one
      WKWebView per NSWindow against the shared configuration, and the
      runtime's `LoadWindowWebView` routes to the matching WKWebView by
      window id. `NullPlatform` now exposes `WindowSources`,
      `WindowBridgeResponses`, and `WindowEvents` so the Core tests in
      `RuntimeTests.Runtime_CreateWindow_LoadsPerWindowSource` (plus
      bridge-response routing, event emission, and duplicate-id/label
      rejection) lock down the round-trip.

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
- [x] **WebKitGTK 6.0 probing.** `WebKit.WebViewNew` now probes for
      `libwebkitgtk-6.0.so.4` alongside the existing
      `libwebkit2gtk-4.1` / `4.0` chain, caches the detected ABI, and
      dispatches every WebKit P/Invoke (load HTML/URI, user-content
      manager, scheme handler, navigation policy, evaluate-JS) through
      the cached ABI. Probe order is 4.1 → 4.0 → 6.0 because the GTK
      host this platform builds against is GTK3 — the 6.0 entry is a
      safety net until a real GTK4 host pipeline lands.
- [x] **GTK4 host wiring (core path).** `Gtk` now carries a `GtkAbi`
      switch with a full GTK4 P/Invoke surface alongside the GTK3 one
      (`gtk_init`, `gtk_window_new`, `gtk_window_set_child`,
      `gtk_window_present`, `gtk_window_destroy`, `gtk_widget_get_width`/
      `_height`, `gtk_widget_get_scale_factor`). The main loop runs
      against a `GMainLoop` on GTK4, the destroy callback returns
      gboolean to satisfy `close-request`, and `WebKitGtkPlatform`
      probes WebKit via `NativeLibrary.TryLoad` and pairs the GTK ABI
      before any widget is created. GTK4 paths that lack a clean GTK4
      equivalent — `GtkClipboard`, `GtkFileChooserDialog`, the
      `configure-event`/`focus-in-event` signals — surface
      `UnsupportedServiceException` until the `GdkClipboard` /
      `GtkFileDialog` / `notify::default-width` wiring lands.
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

- [x] **macOS arm64 binary distribution.** `tools/stage-cef-macos-arm64.sh`
      downloads `cef-builds.spotifycdn.com/cef_binary_*_macosarm64.tar.bz2`
      and stages the Release + Resources payload under the published
      app's `runtimes/osx-arm64/native/` directory. Callers can pin a CEF
      version on the command line; the default matches the CefGlue.Common
      120.x branch. Apps that publish framework-dependent point at the
      staged folder via `CefPlatformOptions.CefDirectory`.
- [x] **Bridge inbound.** `CefRenderHandler` registers a V8 `__zero_native_send`
      function in the renderer process; `CefClientImpl.OnProcessMessageReceived`
      receives the `CefProcessMessage` in the browser process and forwards
      the payload to the runtime.
- [x] **Multi-window.** `CefPlatform` tracks `(WindowId, CefBrowser)` pairs
      and routes `LoadWindowWebView` / `CompleteWindowBridge` /
      `EmitWindowEvent` / `FocusWindow` / `CloseWindow` through them.
      `IPlatformServices.CreateWindow` spawns a fresh `CefBrowserHost.CreateBrowser`
      against `CefWindowInfo.Bounds` matching `WindowOptions.DefaultFrame`;
      bridge messages carry the originating browser id so multi-window
      responses route correctly. Closing the primary (or last) browser
      ends the CEF loop.
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
- [x] Snapshot tests for `Runtime` lifecycle traces (capture
      `TraceRecord` sequence and diff against golden files). Locked
      down the canonical order in
      `tests/ZeroNative.Core.Tests/RuntimeLifecycleSnapshotTests.cs`.

## Tooling / packaging

- [x] **Per-package READMEs** embedded in the NuGet via
      `PackageReadmeFile`. All three packages (`ZeroNative.Core`,
      `ZeroNative`, `ZeroNative.Cef`) now ship with usage docs.
- [x] **GitHub Actions matrix.** `.github/workflows/dotnet.yml` builds
      and tests on `windows-latest`, `macos-latest`, and `ubuntu-latest`,
      then packs `ZeroNative.Core`, `ZeroNative`, and `ZeroNative.Cef`
      and uploads the `.nupkg` artifacts.
- [x] **`dotnet pack --version` driven from CI tags.** The pack job in
      `.github/workflows/dotnet.yml` resolves `Version` from the pushed
      tag (`vX.Y.Z`), with a `0.1.0-dev.<run>.<sha>` fallback for branch
      builds and a `ZERO_NATIVE_VERSION` env override.
- [x] **`dotnet new` template.** `src/ZeroNative.Templates` ships a
      `dotnet new zero-native-app` template. Installs via
      `dotnet new install ZeroNative.Templates` and scaffolds a
      `Program.cs`, `app.json`, and `wwwroot/index.html`. Pass `--UseCef`
      to scaffold against `ZeroNative.Cef` instead.
- [x] **Source link.** `Directory.Build.props` adds
      `Microsoft.SourceLink.GitHub` to every packable project and turns on
      `PublishRepositoryUrl` / `EmbedUntrackedSources` /
      `ContinuousIntegrationBuild` so package consumers can step into the
      shipped sources.

## Documentation

- [ ] Port the docs site under `.reference/docs/` (Next.js MDX) to point
      at the C# API surface, or replace with DocFX / .NET docs.
- [ ] **Build the docs site with Neko.** Use the
      [Neko](https://github.com/theolivenbaum/neko) static-site generator
      (Markdown + components, Razor/H5 friendly) to publish the API and
      guide pages. Author content under `docs/`, drive Neko from the
      `pack` CI job, and publish to GitHub Pages.
- [x] Per-package READMEs that get embedded in the NuGet.
- [x] Architecture diagram showing `Core` ↔ platform backends ↔ host apps
      (`README.md` "Architecture" section).

## Nice-to-haves

- [x] **Native AOT support (Core).** `ZeroNative.Core` is marked
      `IsAotCompatible=true` and `IsTrimmable=true`; the JSON code paths
      use `JsonSerializerContext`-generated converters (manifest + window
      state store), so the package builds cleanly under the AOT analyzer.
      Platform host packages still need their own AOT audit when
      consumers publish AOT-ready apps.
- [x] **Velopack installer documentation.** `docs/velopack.md` walks
      through wiring Velopack into a ZeroNative app: adding the dependency,
      placing `VelopackApp.Build().Run()` ahead of the runtime, calling
      `UpdateManager` from a bridge handler, and the per-OS `vpk pack`
      invocation (including how `--packDir` lines up with
      `ZeroNative.AppBundle.targets` on macOS). A trimmed-down CI matrix
      snippet is included at the end.
- [x] **Sample app: ZeroNative + in-process Kestrel data plane.** Landed
      under `samples/ZeroNative.Sample.Kestrel`. `Program.cs` boots a
      `WebApplication.CreateSlimBuilder` listening on `127.0.0.1:0`,
      narrows `SecurityPolicy.Navigation.AllowedOrigins` to the resolved
      origin, and points `WebViewSource.Url(...)` at the Kestrel URL.
      Bundles a static `wwwroot/` frontend that exercises `/api/echo`
      / `/api/info` via `fetch()` plus a couple of bridge commands
      (`native.clipboard`, `native.shell.open`). Tesserae/H5 slots in by
      emitting its compiled output into `wwwroot/` — the sample README
      walks through the integration path so we don't need to ship the
      H5 toolchain in CI.
- [x] **Hot reload of the web bundle.** `ZeroNative.Tooling.DevServer`
      (in `src/ZeroNative.Core/Tooling/DevServer.cs`) mirrors the Zig
      `tooling/dev.zig`: parse the dev URL, optionally spawn the dev
      command, poll the server until it answers a 2xx/3xx response, and
      return a handle that owns the child process. Apps point their
      `WebViewSource.Url(...)` at the resolved URL during development.
