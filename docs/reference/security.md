---
order: 4
title: Security
icon: shield
tags: [reference]
---

# Security

zero-native treats the WebView as untrusted by default. Native power is
opt-in: every command, navigation, and external link goes through an
explicit policy.

## Permissions

Permissions are runtime grants checked before native commands execute.
Bundled constants live on `ZeroNative.Security.Permissions`:

| Permission | Grants |
|---|---|
| `Permissions.Window` | `window` – window create / focus / close |
| `Permissions.Filesystem` | `filesystem` – file system access from bridge handlers |
| `Permissions.Clipboard` | `clipboard` – clipboard read / write |
| `Permissions.Network` | `network` – native HTTP / socket access |

Custom permissions are plain strings using reverse-DNS names
(e.g. `"com.example.my-permission"`). Use the smallest set that covers your app.

```csharp
var security = new SecurityPolicy(
    Permissions: new[] { Permissions.Window, Permissions.Clipboard },
    Navigation:  new NavigationPolicy(AllowedOrigins: new[] { "zero://app" }));
```

## App-defined commands

Every bridge command is default-deny. To make it callable, register the
handler **and** add it to the dispatcher policy:

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
                Name: "native.deleteFile",
                Permissions: new[] { Permissions.Filesystem },
                Origins: new[] { "zero://app" }),
        }),
    Registry = registry,
};
```

Prefer exact origins. `"*"` should only appear during local development or
for handlers that touch no native state.

## Built-in commands

The runtime ships built-in command families:

- `zero-native.window.*` – `list`, `create`, `focus`, `close`.
- `zero-native.dialog.*` – `openFile`, `saveFile`, `showMessage`.

Window commands accept the `JsWindowApi` shortcut (any allowed origin + the
`window` permission), or an explicit `BuiltinBridge` policy.

Dialog commands are **always default-deny**. They require an explicit
`BuiltinBridge` entry:

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
        new BridgeCommandPolicy(
            Name: "zero-native.dialog.showMessage",
            Origins: new[] { "zero://app" }),
    });
```

## Navigation policy

Main-frame navigation is allowlisted. Packaged assets use `zero://app`,
inline HTML uses `zero://inline`, and a Vite/Next dev server should list its
exact local origin.

```csharp
var navigation = new NavigationPolicy(
    AllowedOrigins: new[]
    {
        "zero://app",
        "zero://inline",
        "http://127.0.0.1:5173",
    });
```

Unknown navigations are blocked unless the external-link policy catches them.

`SecurityPolicy.DecideNavigation(url)` returns one of:

- `NavigationDecision.AllowInline` – let the WebView load the URL.
- `NavigationDecision.OpenExternally` – cancel and hand off to the OS.
- `NavigationDecision.Block` – cancel outright.

Backends call `DecideNavigation` from their navigation-policy callbacks
(`NavigationStarting` / `NewWindowRequested` on WebView2,
`decide-policy` on WebKitGTK, `OnBeforeBrowse` on CEF).

## External links

External links are denied by default. To open them in the system browser:

```csharp
var navigation = new NavigationPolicy(
    AllowedOrigins: new[] { "zero://app" },
    ExternalLinks:  new ExternalLinkPolicy(
        Action:      ExternalLinkAction.OpenSystemBrowser,
        AllowedUrls: new[] { "https://example.com/docs/*" }));
```

Wildcards only match as a trailing `/*`. Use a tight prefix; do not allow
broad external patterns for pages that can be influenced by remote content.

## CSP guidance

zero-native does **not** inject a Content Security Policy. Set one in your
HTML. For packaged assets, start strict:

```html
<meta http-equiv="Content-Security-Policy"
      content="default-src 'self'; script-src 'self'; style-src 'self';
               img-src 'self' data:; connect-src 'self'">
```

For inline samples that embed scripts and styles, add only what you need:

```html
<meta http-equiv="Content-Security-Policy"
      content="default-src 'self'; script-src 'self' 'unsafe-inline';
               style-src 'self' 'unsafe-inline';
               img-src 'self' data:; connect-src 'self'">
```

For dev servers, extend `connect-src` to the framework's HMR endpoint. Keep
the development CSP separate from production.

## Summary

| Layer | Default | Opt-in |
|---|---|---|
| App bridge commands | Denied | Per-command policy with origin and permission checks |
| Built-in window commands | Denied unless `JsWindowApi` or `BuiltinBridge` allows them | `window` permission plus allowed origins |
| Built-in dialog commands | Denied | Explicit `BuiltinBridge` policy |
| Navigation | Blocked | Allowlisted origins |
| External links | Denied | Explicit action + URL prefix list |
| Permissions | None granted | Listed in `SecurityPolicy.Permissions` |
| CSP | Not enforced by zero-native | Set in your HTML `<meta>` tag |

The goal is defense in depth: even if a command is registered, it won't run
unless the policy lets it through.
