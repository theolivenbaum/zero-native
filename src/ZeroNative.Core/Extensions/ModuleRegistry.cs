using ZeroNative.Manifest;

namespace ZeroNative.Extensions;

public sealed record ExtensionRuntimeContext(string PlatformName);

public sealed record ExtensionCommand(string Name, ulong? Target = null);

public sealed record ModuleInfo
{
    public required ulong Id { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<ulong> Dependencies { get; init; } = Array.Empty<ulong>();
    public IReadOnlyList<Capability> Capabilities { get; init; } = Array.Empty<Capability>();
}

/// <summary>
/// A native module that can be hosted by the runtime alongside the WebView.
/// Default <see cref="Start"/>, <see cref="Stop"/>, and <see cref="Command"/>
/// implementations are no-ops so modules can opt-in to whichever lifecycle
/// hooks they care about.
/// </summary>
public interface IModule
{
    ModuleInfo Info { get; }

    void Start(ExtensionRuntimeContext context) { }

    void Stop(ExtensionRuntimeContext context) { }

    void Command(ExtensionRuntimeContext context, ExtensionCommand command) { }
}

public class ModuleRegistryException : Exception
{
    public ModuleRegistryException(string message) : base(message) { }
    public ModuleRegistryException(string message, Exception inner) : base(message, inner) { }
}

public class DuplicateModuleException : ModuleRegistryException
{
    public DuplicateModuleException(ulong id) : base($"duplicate module id: {id}") { }
}

public class MissingDependencyException : ModuleRegistryException
{
    public MissingDependencyException(ulong id) : base($"missing module dependency: {id}") { }
}

public class ModuleFailedException : ModuleRegistryException
{
    public ModuleFailedException(string module, Exception inner)
        : base($"module '{module}' failed: {inner.Message}", inner) { }
}

public sealed class ModuleRegistry
{
    public IReadOnlyList<IModule> Modules { get; }

    public ModuleRegistry() : this(Array.Empty<IModule>()) { }

    public ModuleRegistry(IReadOnlyList<IModule> modules)
    {
        Modules = modules;
    }

    public void Validate()
    {
        var seen = new HashSet<ulong>();
        foreach (var module in Modules)
        {
            if (!seen.Add(module.Info.Id))
                throw new DuplicateModuleException(module.Info.Id);
        }
        foreach (var module in Modules)
        {
            foreach (var dep in module.Info.Dependencies)
            {
                if (!seen.Contains(dep))
                    throw new MissingDependencyException(dep);
            }
        }
    }

    public void StartAll(ExtensionRuntimeContext context)
    {
        Validate();
        foreach (var module in Modules)
        {
            try { module.Start(context); }
            catch (Exception ex) { throw new ModuleFailedException(module.Info.Name, ex); }
        }
    }

    public void StopAll(ExtensionRuntimeContext context)
    {
        for (var i = Modules.Count - 1; i >= 0; i--)
        {
            var module = Modules[i];
            try { module.Stop(context); }
            catch (Exception ex) { throw new ModuleFailedException(module.Info.Name, ex); }
        }
    }

    public void DispatchCommand(ExtensionRuntimeContext context, ExtensionCommand command)
    {
        if (command.Target is { } target)
        {
            var module = FindById(target) ?? throw new MissingDependencyException(target);
            try { module.Command(context, command); }
            catch (Exception ex) { throw new ModuleFailedException(module.Info.Name, ex); }
            return;
        }

        foreach (var module in Modules)
        {
            try { module.Command(context, command); }
            catch (Exception ex) { throw new ModuleFailedException(module.Info.Name, ex); }
        }
    }

    public bool HasCapability(CapabilityKind kind)
    {
        foreach (var module in Modules)
        {
            foreach (var capability in module.Info.Capabilities)
            {
                if (capability.Kind == kind) return true;
            }
        }
        return false;
    }

    public IModule? FindById(ulong id)
    {
        foreach (var module in Modules)
        {
            if (module.Info.Id == id) return module;
        }
        return null;
    }
}

/// <summary>
/// Convenience implementation of <see cref="IModule"/> that wires up
/// delegate-based lifecycle hooks without a custom class.
/// </summary>
public sealed class DelegateModule : IModule
{
    public ModuleInfo Info { get; }
    public Action<ExtensionRuntimeContext>? OnStart { get; init; }
    public Action<ExtensionRuntimeContext>? OnStop { get; init; }
    public Action<ExtensionRuntimeContext, ExtensionCommand>? OnCommand { get; init; }

    public DelegateModule(ModuleInfo info) { Info = info; }

    public void Start(ExtensionRuntimeContext context) => OnStart?.Invoke(context);
    public void Stop(ExtensionRuntimeContext context) => OnStop?.Invoke(context);
    public void Command(ExtensionRuntimeContext context, ExtensionCommand command)
        => OnCommand?.Invoke(context, command);
}
