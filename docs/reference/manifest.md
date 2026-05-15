---
order: 10
title: App Manifest
icon: file-lines
tags: [reference]
---

# App Manifest

The optional `app.json` manifest mirrors the Zig `app.zon` schema. It's parsed
by `AppManifestJson.Load` / `AppManifestJson.Parse` from `ZeroNative.Manifest`
and consumed by tooling, packaging targets, and (when you opt in) the runtime
itself.

## Example

```json
{
  "id": "dev.example.hello",
  "name": "hello",
  "display_name": "Hello",
  "version": "1.0.0",
  "icons": ["assets/icon.icns", "assets/icon.ico"],
  "platforms": ["windows", "macos", "linux"],
  "permissions": ["window"],
  "capabilities": ["webview", "js_bridge"],
  "bridge": {
    "commands": [
      { "name": "native.ping", "origins": ["zero://app"] },
      { "name": "zero-native.window.create", "permissions": ["window"], "origins": ["zero://app"] }
    ]
  },
  "security": {
    "navigation": {
      "allowed_origins": ["zero://app", "http://127.0.0.1:5173"],
      "external_links": { "action": "deny" }
    }
  },
  "frontend": {
    "dev_url": "http://127.0.0.1:5173",
    "build_command": "npm run build",
    "dist": "wwwroot"
  },
  "windows": [
    { "label": "main", "title": "Hello", "width": 720, "height": 480, "restore_state": true }
  ],
  "cef": { "auto_install": false },
  "updates": { "feed_url": "https://releases.example.com/hello" }
}
```

## Loading at runtime

```csharp
using ZeroNative.Manifest;

var manifest = AppManifestJson.Load("app.json");

var appInfo = new AppInfo
{
    AppName     = manifest.Name,
    WindowTitle = manifest.DisplayName ?? manifest.Name,
    BundleId    = manifest.Id,
    MainWindow  = new WindowOptions
    {
        Label        = manifest.Windows[0].Label,
        Title        = manifest.Windows[0].Title,
        DefaultFrame = new RectF(0, 0, manifest.Windows[0].Width, manifest.Windows[0].Height),
    },
};
```

You can equally construct `AppInfo` by hand and treat the manifest as
build-time metadata only. The runtime doesn't require it.

## Top-level fields

| Field | Purpose |
|---|---|
| `id` | Bundle id (reverse-DNS, e.g. `dev.example.hello`). |
| `name` | Internal name. Used by traces and automation. |
| `display_name` | Optional human-readable name for OS chrome. |
| `version` | SemVer-ish (pre-release / build identifiers supported). |
| `icons` | Per-platform icon paths. |
| `platforms` | OSes the app supports. |
| `permissions` | Required permissions. |
| `capabilities` | Coarse-grained features. |
| `bridge` | App bridge commands and per-command policy. |
| `security` | Navigation allowlist and external-link policy. |
| `frontend` | Dev URL, build command, and bundle directory. |
| `windows` | Window declarations (label, title, geometry, `restore_state`). |
| `cef` | CEF directory and auto-install flag. |
| `updates` | Update feed metadata for in-app updaters. |
| `package` | Per-OS packaging metadata (codesign identity, AppImage settings, …). |

## Validation

`AppManifestJson.Parse` validates structure and throws
`AppManifestException` on unknown fields, missing required values, or
malformed JSON. The same parser is used by the test suite to lock down the
schema.
