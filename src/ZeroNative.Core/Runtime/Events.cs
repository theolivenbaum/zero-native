namespace ZeroNative.Runtime;

public enum LifecycleEvent
{
    Start,
    Frame,
    Stop,
}

public readonly record struct CommandEvent(string Name);

public enum InvalidationReason
{
    Startup,
    SurfaceResize,
    Command,
    State,
}

public readonly record struct FrameDiagnostics(
    ulong FrameIndex,
    int CommandCount,
    int DirtyRegionCount,
    int ResourceUploadCount,
    long DurationNs);

public abstract record RuntimeEvent
{
    public abstract string Name { get; }

    public sealed record Lifecycle(LifecycleEvent Phase) : RuntimeEvent
    {
        public override string Name => Phase switch
        {
            LifecycleEvent.Start => "start",
            LifecycleEvent.Frame => "frame",
            LifecycleEvent.Stop => "stop",
            _ => "lifecycle",
        };
    }

    public sealed record Command(string Value) : RuntimeEvent
    {
        public override string Name => Value;
    }
}
