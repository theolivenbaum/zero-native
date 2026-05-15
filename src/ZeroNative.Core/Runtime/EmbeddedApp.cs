using ZeroNative.Platform;
using ZeroNative.Primitives;

namespace ZeroNative.Runtime;

/// <summary>
/// Convenience wrapper that drives a <see cref="Runtime"/> from external
/// platform code (e.g. an iOS / Android host or a custom rendering loop).
/// Mirrors the Zig <c>src/embed/root.zig</c> shape: callers pump
/// <see cref="Start"/> / <see cref="Resize"/> / <see cref="Frame"/> /
/// <see cref="Stop"/> rather than handing control to <see cref="Runtime.Run"/>.
/// </summary>
public sealed class EmbeddedApp
{
    public App App { get; }
    public Runtime Runtime { get; }

    public EmbeddedApp(App app, IPlatform platform)
    {
        App = app ?? throw new ArgumentNullException(nameof(app));
        Runtime = new Runtime(new RuntimeOptions { Platform = platform });
    }

    public EmbeddedApp(App app, RuntimeOptions options)
    {
        App = app ?? throw new ArgumentNullException(nameof(app));
        Runtime = new Runtime(options);
    }

    public void Start() => Runtime.DispatchPlatformEvent(App, new PlatformEvent.AppStart());
    public void Resize(Surface surface) => Runtime.DispatchPlatformEvent(App, new PlatformEvent.SurfaceResized(surface));
    public void Frame() => Runtime.DispatchPlatformEvent(App, new PlatformEvent.FrameRequested());
    public void Stop() => Runtime.DispatchPlatformEvent(App, new PlatformEvent.AppShutdown());
    public void Bridge(BridgeMessage message) => Runtime.DispatchPlatformEvent(App, new PlatformEvent.BridgeReceived(message));
}
