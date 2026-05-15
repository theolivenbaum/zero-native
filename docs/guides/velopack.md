---
order: 5
title: Velopack Packaging
icon: box-archive
tags: [guide]
---

# Shipping a ZeroNative app with Velopack

[Velopack](https://velopack.io) is a cross-platform installer + auto-updater
framework. It produces a Setup.exe for Windows, a `.app`/`.pkg` for macOS, and
an AppImage / `.deb` / `.rpm` for Linux from the same publish output, and it
ships an in-app `UpdateManager` API that handles delta downloads and apply-on-
restart.

This guide walks through wiring a ZeroNative app for Velopack release packaging
and in-app updates.

## Prerequisites

- The .NET 10 SDK
- The Velopack CLI:
  ```bash
  dotnet tool install --global vpk
  ```
- A working ZeroNative app that publishes cleanly:
  ```bash
  dotnet publish -c Release -r win-x64    # or osx-arm64, linux-x64, etc.
  ```

## 1. Add the runtime dependency

`Velopack` carries the `UpdateManager`, the bootstrapper, and the small native
launcher Velopack uses on each OS.

```xml
<ItemGroup>
  <PackageReference Include="Velopack" Version="0.0.*" />
</ItemGroup>
```

## 2. Initialise Velopack before the runtime

Velopack must be initialised before any window is created so it can handle the
first-run hooks and the apply-and-restart handshake. Put it at the very top of
`Program.cs`:

```csharp
using Velopack;
using ZeroNative;
using ZeroNative.Platform;
using ZeroNative.Primitives;
using ZeroNative.Runtime;

VelopackApp.Build()
    .WithFirstRun(version =>
    {
        // Optional: anything you want to do on first launch (welcome window,
        // analytics opt-in, etc.). Keep this fast.
    })
    .Run();

var appInfo = new AppInfo
{
    AppName = "Hello",
    BundleId = "dev.example.hello",
    MainWindow = new WindowOptions { DefaultFrame = new RectF(0, 0, 960, 600) },
};

var platform = WebViewPlatform.CreateForCurrentOs(appInfo);
var runtime  = new Runtime(new RuntimeOptions { Platform = platform });
var app      = new AppBuilder()
    .Named("hello")
    .WithSource(WebViewSource.Html("<h1>Hello, zero-native</h1>"))
    .Build();

runtime.Run(app);
```

`VelopackApp.Build().Run()` is a no-op when the binary is launched outside a
Velopack install (e.g. during `dotnet run`), so this code is safe in
development too.

## 3. Wire the in-app updater

Expose a bridge command (or a menu item) that asks the configured feed for a
new build and applies it on next start.

```csharp
var dispatcher = new BridgeDispatcher
{
    Registry = new BridgeRegistry().Register(new BridgeHandler(
        "app.checkForUpdate",
        async inv =>
        {
            var mgr = new UpdateManager("https://releases.example.com/hello");
            var info = await mgr.CheckForUpdatesAsync();
            if (info is null) return """{"status":"up-to-date"}""";

            await mgr.DownloadUpdatesAsync(info);
            mgr.ApplyUpdatesAndRestart(info);
            return """{"status":"restarting"}""";
        })),
};
```

`UpdateManager` accepts any HTTPS feed URL — Velopack publishes a flat
`releases.<channel>.json` index that points at the per-RID `.nupkg` deltas, so
GitHub Releases, S3, or any plain static host works.

## 4. Pack per-OS releases

Velopack reads from the framework-dependent publish output and adds a thin
native launcher.

### Windows

```bash
dotnet publish -c Release -r win-x64 --self-contained
vpk pack \
    --packId dev.example.hello \
    --packVersion 1.0.0 \
    --packDir ./bin/Release/net10.0/win-x64/publish \
    --mainExe Hello.exe \
    --icon assets/icon.ico
```

`vpk pack` emits a `Releases/` folder containing `Setup.exe`, the
`releases.win.json` index, and the `.nupkg` delta. Upload the folder to the URL
you configured in `UpdateManager`.

### macOS

ZeroNative's `ZeroNative.AppBundle.targets` already assembles the `.app`
structure (`Contents/MacOS`, `Contents/Resources`, `Info.plist`) during
`dotnet publish`. Hand that bundle directly to `vpk pack`:

```bash
dotnet publish -c Release -r osx-arm64 \
    -p:ZeroNativeBundleApp=true \
    -p:ZeroNativeBundleId=dev.example.hello \
    -p:ZeroNativeBundleVersion=1.0.0

vpk [osx] pack \
    --packId dev.example.hello \
    --packVersion 1.0.0 \
    --packDir ./bin/Release/net10.0/osx-arm64/publish/Hello.app \
    --mainExe Hello \
    --icon assets/Icon.icns
```

> The macOS branch of Velopack expects `--packDir` to point at the `.app`
> bundle root (not the directory containing it). The bundle's
> `CFBundleVersion` and Velopack's `--packVersion` must match; the
> `ZeroNativeBundleVersion` MSBuild property feeds the former.

Codesigning and notarization remain an out-of-band step — sign the `.app`
before `vpk pack` or pass `--signAppIdentity` / `--notaryProfile` to let
Velopack invoke `codesign` / `notarytool` itself.

### Linux

```bash
dotnet publish -c Release -r linux-x64 --self-contained
vpk [linux] pack \
    --packId dev.example.hello \
    --packVersion 1.0.0 \
    --packDir ./bin/Release/net10.0/linux-x64/publish \
    --mainExe Hello \
    --icon assets/icon.png
```

Velopack emits an `AppImage`. Pass `--linuxOutput deb` or `rpm` to produce
distro packages instead.

## 5. CI matrix

A minimal GitHub Actions matrix looks like this:

```yaml
strategy:
  matrix:
    include:
      - { os: windows-latest, rid: win-x64 }
      - { os: macos-latest,   rid: osx-arm64 }
      - { os: ubuntu-latest,  rid: linux-x64 }

steps:
  - uses: actions/checkout@v4
  - uses: actions/setup-dotnet@v4
    with: { dotnet-version: '10.x' }
  - run: dotnet tool install --global vpk
  - run: dotnet publish -c Release -r ${{ matrix.rid }} --self-contained
  - run: vpk pack --packId dev.example.hello --packVersion ${{ github.ref_name }} ...
  - uses: actions/upload-artifact@v4
    with: { name: release-${{ matrix.rid }}, path: ./Releases }
```

Combine the per-RID `Releases/` folders into a single upload directory and
push it to your feed host. `UpdateManager` only needs the JSON index and the
matching `.nupkg` files to be reachable at the URL it was constructed with.

## See also

- Velopack docs: <https://docs.velopack.io>
- `src/ZeroNative/build/ZeroNative.AppBundle.targets` for the macOS bundle
  generator that pairs with Velopack's `--packDir`.
