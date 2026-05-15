# ZeroNative project templates

Install the template pack:

```bash
dotnet new install ZeroNative.Templates
```

Scaffold a new app:

```bash
dotnet new zero-native-app -n MyApp
cd MyApp
dotnet run
```

The template produces a single-project layout with:

- `Program.cs` — wires up `Runtime`, the bridge, and the security policy.
- `app.json` — a JSON manifest mirroring the Zig `app.zon` schema.
- `wwwroot/index.html` — a placeholder HTML page that talks to the bridge.

Replace `WebViewSource.Html(...)` with `WebViewSource.Assets(...)` once you wire
a real frontend bundler (Vite, Next.js, Svelte, etc).
