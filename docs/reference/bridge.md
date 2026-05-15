---
order: 2
title: Bridge
icon: bolt
tags: [reference]
---

# Bridge

The bridge connects JavaScript in the WebView to .NET handlers via JSON
messages. It is identical in shape to the Zig original.

## Architecture

```
WebView JS                           .NET Runtime
──────────                           ────────────
window.zero.invoke(cmd, payload)
        │                                   │
        ├────── JSON request ──────────────►│
        │                              size check (16 KiB)
        │                              policy check (origins + permissions)
        │                              handler lookup + dispatch
        │◄───── JSON response ──────────────┤
```

## Protocol

Request:

```json
{ "id": "<string>", "command": "<dotted.name>", "payload": <any-json> }
```

Response (success):

```json
{ "id": "<id>", "ok": true, "result": <any-json> }
```

Response (error):

```json
{ "id": "<id>", "ok": false, "error": { "code": "<code>", "message": "..." } }
```

## Registering a handler

`BridgeHandler` is the synchronous form; `AsyncBridgeHandler` is the async
form. The dispatcher prefers the async overload when both are registered.

```csharp
using ZeroNative.Bridge;

var registry = new BridgeRegistry()
    .Register(new BridgeHandler("native.ping", invocation =>
    {
        // invocation.Request.Payload is the raw JSON payload string.
        return $"{{\"pong\":true,\"echo\":{invocation.Request.Payload}}}";
    }))
    .Register(new AsyncBridgeHandler("native.slow", async invocation =>
    {
        await Task.Delay(50);
        return "{\"done\":true}";
    }));
```

The handler returns a JSON value as a string. Non-JSON strings are rejected
with `handler_failed`. Use `JsonWriter` for safe escaping when echoing
untrusted data:

```csharp
using ZeroNative.Primitives;

return JsonWriter.WriteString(userSuppliedName);
```

## Policy

`BridgePolicy` decides which commands are allowed and from which origins.
The dispatcher pairs a policy with a registry:

```csharp
var dispatcher = new BridgeDispatcher
{
    Policy = new BridgePolicy(
        Enabled: true,
        Commands: new[]
        {
            new BridgeCommandPolicy(
                Name: "native.ping",
                Origins: new[] { "zero://app" }),
            new BridgeCommandPolicy(
                Name: "zero-native.window.create",
                Permissions: new[] { Permissions.Window },
                Origins: new[] { "zero://app" }),
        }),
    Registry = registry,
};
```

Prefer exact origins. Use `"*"` only for local development or commands that
don't expose native state.

## Invocation context

Every handler receives a `BridgeInvocation`:

| Field | Description |
|---|---|
| `Request.Id` | Caller-provided request id (max 64 bytes). |
| `Request.Command` | Command name (max 128 bytes). |
| `Request.Payload` | Raw JSON payload string. |
| `Source.Origin` | Origin of the requesting page (e.g. `zero://app`). |
| `Source.WindowId` | The window that issued the call. |

## Calling from JavaScript

```javascript
const result = await window.zero.invoke("native.ping", { source: "webview" });
// → { pong: true, echo: { source: "webview" } }

try {
  await window.zero.invoke("nope.unknown", null);
} catch (err) {
  console.error(err.code, err.message); // "unknown_command", ...
}
```

`window.zero` is injected by `BridgeJavascript` on document-create, before any
page script runs.

## Size limits

| Constant | Value |
|---|---|
| `MaxMessageBytes` | 16 KiB |
| `MaxResponseBytes` | 16 KiB |
| `MaxResultBytes` | 12 KiB |
| `MaxIdBytes` | 64 |
| `MaxCommandBytes` | 128 |

## Error codes

| Code | Cause |
|---|---|
| `invalid_request` | Malformed JSON message. |
| `unknown_command` | No handler registered for this name. |
| `permission_denied` | Origin or permission check failed. |
| `handler_failed` | Handler threw or returned non-JSON. |
| `payload_too_large` | Message exceeds `MaxMessageBytes`. |
| `internal_error` | Unexpected runtime error. |

## Built-in commands

The runtime ships with command families under the
`zero-native.window.*` and `zero-native.dialog.*` namespaces.

Window commands are gated by the `JsWindowApi` flag (which is shorthand for
"allow `window.zero.window.*` from any allowed origin with the `window`
permission") **or** an explicit `BuiltinBridge` policy.

Dialog commands are always default-deny: list each one explicitly in
`BuiltinBridge`.

```csharp
RuntimeOptions.BuiltinBridge = new BridgePolicy(
    Enabled: true,
    Commands: new[]
    {
        new BridgeCommandPolicy(
            Name: "zero-native.window.create",
            Permissions: new[] { Permissions.Window },
            Origins: new[] { "zero://app" }),
        new BridgeCommandPolicy(
            Name: "zero-native.dialog.openFile",
            Origins: new[] { "zero://app" }),
    });
```

See [Security](/reference/security) for the full policy semantics.
