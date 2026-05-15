---
order: 8
title: Extensions
icon: puzzle-piece
tags: [reference]
---

# Extensions

`ModuleRegistry` adds modular start/stop/command hooks to the runtime. It is
the .NET equivalent of the Zig `src/extensions/root.zig` model.

## Defining a module

Implement `IModule`. Every hook has a default no-op implementation:

```csharp
using ZeroNative.Extensions;

public sealed class MyModule : IModule
{
    public ModuleInfo Info { get; } = new("my-module")
    {
        Capabilities = new[] { ModuleCapability.NativeModule },
    };

    private int _counter;

    public Task StartAsync(RuntimeContext ctx, CancellationToken ct)
    {
        _counter = 42;
        return Task.CompletedTask;
    }

    public Task OnCommandAsync(RuntimeContext ctx, ModuleCommand command, CancellationToken ct)
    {
        if (command.Name == "reset") _counter = 0;
        return Task.CompletedTask;
    }

    public Task StopAsync(RuntimeContext ctx, CancellationToken ct)
        => Task.CompletedTask;
}
```

For the common case use `DelegateModule`:

```csharp
var module = new DelegateModule("metrics")
{
    Start  = (_, _) => { /* … */ return Task.CompletedTask; },
    Stop   = (_, _) => Task.CompletedTask,
    OnCommand = (_, cmd, _) => { /* … */ return Task.CompletedTask; },
};
```

## Registering modules

```csharp
var registry = new ModuleRegistry()
    .Add(new MyModule())
    .Add(new DelegateModule("logging"));

var runtime = new Runtime(new RuntimeOptions
{
    Platform = platform,
    Modules  = registry,
});
```

The registry validates duplicate ids and missing dependencies, starts modules
in declaration order, and stops them in reverse.

## Dispatching commands

```csharp
// Targeted: only the named module receives the command.
await registry.SendAsync("my-module", new ModuleCommand("reset"));

// Broadcast: every module's OnCommandAsync fires.
await registry.BroadcastAsync(new ModuleCommand("flush"));
```

Exceptions raised by a hook are wrapped in `ModuleFailedException` with the
module id attached.
