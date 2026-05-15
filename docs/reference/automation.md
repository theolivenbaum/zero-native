---
order: 7
title: Automation
icon: robot
tags: [reference]
---

# Automation

The automation server is a file-based harness for driving and inspecting a
running app from CI or another process. It mirrors the Zig
`src/automation/` subsystem.

## Wiring it up

```csharp
using ZeroNative.Automation;

var server = new AutomationServer(
    directory: ".zero-native-automation",
    appName:   "Hello");

var runtime = new Runtime(new RuntimeOptions
{
    Platform         = platform,
    AutomationServer = server,
});
```

When the runtime is running, the server reads commands from
`<directory>/commands.txt` and writes snapshots to the same directory.

## Snapshot files

`AutomationSnapshot` writes three artifacts atomically:

| File | Description |
|---|---|
| `snapshot.txt` | Runtime state: app name, source kind, window list, `ready=true/false`. |
| `accessibility.txt` | Accessibility tree summary. |
| `windows.txt` | One line per window: `window @w{id} "{title}" focused={true/false}`. |

`Runtime.BuildAutomationInput()` returns the data fed to the server – use it
for one-shot diagnostics outside the automation loop.

## Command protocol

`AutomationProtocol.Parse(line)` accepts three command shapes:

| Command | Effect |
|---|---|
| `reload` | Re-publish the snapshot. |
| `wait` | Block until the runtime has finished its current frame. |
| `bridge <command> <payload-json>` | Invoke a bridge command and write the response to `bridge-response.txt`. |

Lines starting with `#` are ignored; trailing whitespace is trimmed.

## Headless tests

`NullPlatform` is the canonical test platform. Combined with `Runtime`, it
supports every option without ever creating an OS window. The xUnit suite
in `tests/ZeroNative.Core.Tests/` shows the pattern:

```csharp
var platform = new NullPlatform();
var runtime  = new Runtime(new RuntimeOptions
{
    Platform = platform,
    Security = new SecurityPolicy(),
});

var app = new AppBuilder()
    .Named("test")
    .WithSource(WebViewSource.Html("<h1>hi</h1>"))
    .Build();

runtime.Run(app);

// Inspect: platform.WindowEvents, platform.WindowSources,
// platform.WindowBridgeResponses, etc.
```
