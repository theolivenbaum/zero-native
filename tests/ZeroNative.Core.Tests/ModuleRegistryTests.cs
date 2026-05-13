using Xunit;
using ZeroNative.Extensions;
using ZeroNative.Manifest;

namespace ZeroNative.Tests;

public class ModuleRegistryTests
{
    private static readonly ExtensionRuntimeContext Ctx = new("null");

    [Fact]
    public void StartAll_StopAll_DispatchCommand_HitsAllHooks()
    {
        var started = false;
        var stopped = false;
        var commands = 0;

        var module = new DelegateModule(new ModuleInfo
        {
            Id = 1,
            Name = "test",
            Capabilities = new[] { Capability.NativeModule() },
        })
        {
            OnStart = _ => started = true,
            OnStop = _ => stopped = true,
            OnCommand = (_, cmd) => { if (cmd.Name == "ping") commands++; },
        };

        var registry = new ModuleRegistry(new[] { module });
        registry.StartAll(Ctx);
        registry.DispatchCommand(Ctx, new ExtensionCommand("ping"));
        registry.StopAll(Ctx);

        Assert.True(started);
        Assert.True(stopped);
        Assert.Equal(1, commands);
        Assert.True(registry.HasCapability(CapabilityKind.NativeModule));
    }

    [Fact]
    public void Validate_RejectsDuplicateIds()
    {
        var registry = new ModuleRegistry(new IModule[]
        {
            new DelegateModule(new ModuleInfo { Id = 1, Name = "a" }),
            new DelegateModule(new ModuleInfo { Id = 1, Name = "b" }),
        });
        Assert.Throws<DuplicateModuleException>(() => registry.Validate());
    }

    [Fact]
    public void Validate_RejectsMissingDependencies()
    {
        var registry = new ModuleRegistry(new IModule[]
        {
            new DelegateModule(new ModuleInfo
            {
                Id = 1,
                Name = "bad",
                Dependencies = new ulong[] { 42 },
            }),
        });
        Assert.Throws<MissingDependencyException>(() => registry.Validate());
    }

    [Fact]
    public void DispatchCommand_TargetedCommand_OnlyHitsTarget()
    {
        var firstCalls = 0;
        var secondCalls = 0;

        var first = new DelegateModule(new ModuleInfo { Id = 1, Name = "core" })
        {
            OnCommand = (_, _) => firstCalls++,
        };
        var second = new DelegateModule(new ModuleInfo
        {
            Id = 2,
            Name = "dependent",
            Dependencies = new ulong[] { 1 },
        })
        {
            OnCommand = (_, _) => secondCalls++,
        };

        var registry = new ModuleRegistry(new IModule[] { first, second });
        registry.Validate();
        registry.DispatchCommand(Ctx, new ExtensionCommand("targeted", Target: 2));

        Assert.Equal(0, firstCalls);
        Assert.Equal(1, secondCalls);
    }

    [Fact]
    public void DispatchCommand_UnknownTarget_ThrowsMissingDependency()
    {
        var registry = new ModuleRegistry(new IModule[]
        {
            new DelegateModule(new ModuleInfo { Id = 1, Name = "core" }),
        });
        Assert.Throws<MissingDependencyException>(
            () => registry.DispatchCommand(Ctx, new ExtensionCommand("targeted", Target: 99)));
    }

    [Fact]
    public void StartAll_ModuleThrows_PropagatesAsModuleFailed()
    {
        var module = new DelegateModule(new ModuleInfo { Id = 1, Name = "boom" })
        {
            OnStart = _ => throw new InvalidOperationException("nope"),
        };
        var registry = new ModuleRegistry(new IModule[] { module });
        var ex = Assert.Throws<ModuleFailedException>(() => registry.StartAll(Ctx));
        Assert.Contains("boom", ex.Message);
        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }

    [Fact]
    public void StopAll_RunsInReverseOrder()
    {
        var order = new List<string>();
        var a = new DelegateModule(new ModuleInfo { Id = 1, Name = "a" }) { OnStop = _ => order.Add("a") };
        var b = new DelegateModule(new ModuleInfo { Id = 2, Name = "b" }) { OnStop = _ => order.Add("b") };
        var c = new DelegateModule(new ModuleInfo { Id = 3, Name = "c" }) { OnStop = _ => order.Add("c") };

        new ModuleRegistry(new IModule[] { a, b, c }).StopAll(Ctx);
        Assert.Equal(new[] { "c", "b", "a" }, order);
    }

    [Fact]
    public void FindById_ReturnsModuleOrNull()
    {
        var module = new DelegateModule(new ModuleInfo { Id = 7, Name = "x" });
        var registry = new ModuleRegistry(new IModule[] { module });
        Assert.Same(module, registry.FindById(7));
        Assert.Null(registry.FindById(99));
    }

    [Fact]
    public void HasCapability_ScansAllModules()
    {
        var a = new DelegateModule(new ModuleInfo
        {
            Id = 1,
            Name = "a",
            Capabilities = new[] { Capability.WebView() },
        });
        var b = new DelegateModule(new ModuleInfo
        {
            Id = 2,
            Name = "b",
            Capabilities = new[] { Capability.JsBridge() },
        });
        var registry = new ModuleRegistry(new IModule[] { a, b });
        Assert.True(registry.HasCapability(CapabilityKind.WebView));
        Assert.True(registry.HasCapability(CapabilityKind.JsBridge));
        Assert.False(registry.HasCapability(CapabilityKind.Clipboard));
    }
}
