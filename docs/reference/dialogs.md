---
order: 5
title: Dialogs and Tray
icon: folder-open
tags: [reference]
---

# Dialogs and Tray

`IPlatformServices` exposes the OS shell affordances: open / save / message
dialogs, the system tray, and the clipboard. Every host backend implements
the same surface.

## Open / save / message dialogs

The .NET API uses immutable record options:

```csharp
var result = platform.Services.ShowOpenDialog(new OpenDialogOptions
{
    Title           = "Pick a file",
    DefaultPath     = "/Users/me/Documents",
    Filters         = new[]
    {
        new FileFilter("Images", new[] { "png", "jpg", "webp" }),
        new FileFilter("All",    new[] { "*" }),
    },
    AllowDirectories = false,
    AllowMultiple    = true,
});

if (result.Cancelled) return;
foreach (var path in result.Paths) Console.WriteLine(path);
```

| Option | Default |
|---|---|
| `Title` | `""` |
| `DefaultPath` | `""` |
| `Filters` | `[]` |
| `AllowDirectories` | `false` |
| `AllowMultiple` | `false` (open) / N/A (save) |

`ShowSaveDialog` returns a single path; `ShowMessageDialog` returns the index
of the clicked button (primary / secondary / tertiary).

Per-OS implementation:

| OS | Backend |
|---|---|
| Windows | Modern `IFileOpenDialog` / `IFileSaveDialog` Shell COM, with `GetOpenFileNameW`/`GetSaveFileNameW` as a fallback. `MessageBoxW` for alerts. |
| macOS | `NSOpenPanel` / `NSSavePanel` / `NSAlert`, with `allowedFileTypes:` mapped from the filters. |
| Linux | `GtkFileDialog` / `GtkAlertDialog` (GTK4) or `GtkFileChooserDialog` / `GtkMessageDialog` (GTK3). |

## System tray

Tray icons surface through `IPlatformServices.CreateTray`:

```csharp
var tray = platform.Services.CreateTray(new TrayOptions
{
    IconPath = "assets/tray.png",
    Tooltip  = "My App",
    Items = new[]
    {
        new TrayMenuItem(1, "Show window"),
        new TrayMenuItem(2, "Quit"),
    },
});
```

Tray clicks fan out as `PlatformEvent.TrayAction` with the menu item id, and
the runtime forwards them through your `OnEvent` callback as a `CommandEvent`
named `"tray.action"`.

Per-OS implementation:

| OS | Backend |
|---|---|
| Windows | `Shell_NotifyIconW` + `CreatePopupMenu` / `TrackPopupMenu`. |
| macOS | `NSStatusBar` + `NSStatusItem` + `NSMenu`. |
| Linux | `libayatana-appindicator3-1`. Probed at runtime; raises `UnsupportedServiceException` when missing. |

## Clipboard

The clipboard supports text, file URIs, and image bytes. The `ReadClipboard*`
methods return `null` when the clipboard contains no matching data.

```csharp
platform.Services.WriteClipboard("hello");
var text = platform.Services.ReadClipboard();

platform.Services.WriteClipboardFiles(new[] { "/Users/me/file.png" });
var files = platform.Services.ReadClipboardFiles();

platform.Services.WriteClipboardImage(pngBytes);
var image = platform.Services.ReadClipboardImage();
```

Per-OS implementation:

| OS | Backend |
|---|---|
| Windows | `OpenClipboard` + `CF_UNICODETEXT` / `CF_HDROP` / `CF_DIB`. |
| macOS | `NSPasteboard` with `public.utf8-plain-text`, `public.file-url`, `public.png` / `public.tiff`. |
| Linux | `gtk_clipboard_*` (GTK3) or `gdk_clipboard_*` (GTK4). |

## From JavaScript

The built-in bridge exposes file and message dialogs to JS. They require an
explicit `BuiltinBridge` policy because they're default-deny:

```javascript
const path = await window.zero.invoke('zero-native.dialog.openFile', {
    title:   'Pick a file',
    filters: [{ name: 'Images', extensions: ['png', 'jpg'] }],
});

await window.zero.invoke('zero-native.dialog.showMessage', {
    title:   'Heads up',
    message: 'Saved.',
    buttons: ['OK'],
});
```

See [Security](/reference/security#built-in-commands) for the policy snippet.
