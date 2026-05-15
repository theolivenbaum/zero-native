---
order: 3
title: Packaging
icon: box
tags: [guide]
---

# Packaging

zero-native ships a few MSBuild targets and a Velopack integration to turn a
`dotnet publish` output into a distributable bundle on each OS.

## Per-RID publish

Pick the runtime identifier (RID) and publish. The .NET SDK does the rest:

```bash
dotnet publish -c Release -r win-x64    --self-contained
dotnet publish -c Release -r win-arm64  --self-contained
dotnet publish -c Release -r osx-x64
dotnet publish -c Release -r osx-arm64
dotnet publish -c Release -r linux-x64  --self-contained
dotnet publish -c Release -r linux-arm64 --self-contained
```

The sample projects declare the full RID set via `<RuntimeIdentifiers>`. For
a fat macOS binary, run both `osx-x64` and `osx-arm64` and combine them with
`lipo -create -output …`.

## macOS `.app` bundles

The `ZeroNative` NuGet ships `ZeroNative.AppBundle.targets` under `build/`,
which is auto-imported by consumers. Two MSBuild properties drive it:

```xml
<PropertyGroup>
  <ZeroNativeBundleApp>true</ZeroNativeBundleApp>
  <ZeroNativeBundleId>dev.example.hello</ZeroNativeBundleId>
  <ZeroNativeBundleVersion>1.0.0</ZeroNativeBundleVersion>
</PropertyGroup>
```

A `dotnet publish -c Release -r osx-arm64` then assembles:

```
bin/Release/net10.0/osx-arm64/publish/Hello.app/
├── Contents/
│   ├── Info.plist
│   ├── MacOS/Hello
│   └── Resources/
```

`Info.plist` is generated from `ZeroNativeBundleId`, `ZeroNativeBundleVersion`,
and the bundle name. Codesigning and notarization remain out-of-band:

```bash
codesign --deep --force --options runtime --sign "Developer ID Application: …" Hello.app
xcrun notarytool submit --apple-id … --team-id … --keychain-profile … Hello.app
```

## Velopack installers and auto-updates {#velopack}

[Velopack](https://velopack.io) produces a `Setup.exe`, `.app`, AppImage, or
`.deb` from the same `dotnet publish` output and ships an in-app
`UpdateManager` that handles delta downloads and apply-on-restart.

A condensed end-to-end recipe:

1. Add the package:

   ```xml
   <PackageReference Include="Velopack" Version="0.0.*" />
   ```

2. Initialise Velopack before constructing the runtime:

   ```csharp
   using Velopack;

   VelopackApp.Build()
       .WithFirstRun(version => { /* welcome window, analytics opt-in, … */ })
       .Run();

   // …existing AppInfo / platform / runtime / app setup here…

   runtime.Run(app);
   ```

3. Pack per OS:

   ```bash
   # Windows
   dotnet publish -c Release -r win-x64 --self-contained
   vpk pack --packId dev.example.hello --packVersion 1.0.0 \
            --packDir ./bin/Release/net10.0/win-x64/publish \
            --mainExe Hello.exe --icon assets/icon.ico

   # macOS
   dotnet publish -c Release -r osx-arm64 \
            -p:ZeroNativeBundleApp=true -p:ZeroNativeBundleId=dev.example.hello \
            -p:ZeroNativeBundleVersion=1.0.0
   vpk [osx] pack --packId dev.example.hello --packVersion 1.0.0 \
                  --packDir ./bin/Release/net10.0/osx-arm64/publish/Hello.app \
                  --mainExe Hello --icon assets/Icon.icns

   # Linux
   dotnet publish -c Release -r linux-x64 --self-contained
   vpk [linux] pack --packId dev.example.hello --packVersion 1.0.0 \
                    --packDir ./bin/Release/net10.0/linux-x64/publish \
                    --mainExe Hello --icon assets/icon.png
   ```

The full walkthrough lives in the [Velopack Packaging](/guides/velopack) guide.

## CEF macOS arm64 staging

CefGlue does not publish a `cef.redist.osxarm64` package. For apps that ship
the CEF host on Apple silicon, stage the matching CEF binaries:

```bash
tools/stage-cef-macos-arm64.sh \
    --version 120.1.0+g0ab1d8a+chromium-120.0.6099.71 \
    ./bin/Release/net10.0/osx-arm64/publish
```

The script downloads `cef-builds.spotifycdn.com/cef_binary_*_macosarm64.tar.bz2`,
verifies it, and unpacks the Release + Resources payload under
`runtimes/osx-arm64/native/`. Point `CefPlatformOptions.CefDirectory` at the
staged folder for framework-dependent publish.

## CI matrix

`.github/workflows/dotnet.yml` already builds and tests on
`windows-latest`, `macos-latest`, and `ubuntu-latest`, then packs the four
NuGets and uploads them as artifacts. The pack job resolves `Version`:

- From a pushed tag `vX.Y.Z` when present.
- Otherwise `0.1.0-dev.<run>.<sha>`.
- Override with `ZERO_NATIVE_VERSION`.
