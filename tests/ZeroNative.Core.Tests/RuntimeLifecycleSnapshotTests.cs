using Xunit;
using ZeroNative.Bridge;
using ZeroNative.Platform;
using ZeroNative.Primitives;
using ZeroNative.Runtime;
using ZeroNative.Security;

namespace ZeroNative.Tests;

/// <summary>
/// Locks down the trace-event sequence the runtime emits over a full lifecycle.
/// Trace names are part of the observable contract — tools, dashboards, and the
/// automation server depend on them, so these tests catch accidental renames or
/// missing emits.
/// </summary>
public class RuntimeLifecycleSnapshotTests
{
    [Fact]
    public void Startup_to_shutdown_emits_canonical_trace_sequence()
    {
        var records = new List<string>();
        var platform = new NullPlatform(new Surface(), WebEngine.System, new AppInfo
        {
            AppName = "snapshot",
            BundleId = "dev.zero_native.snapshot",
        });
        var runtime = new ZeroNative.Runtime.Runtime(new RuntimeOptions
        {
            Platform = platform,
            TraceSink = r => records.Add(r.Name),
        });

        var app = new AppBuilder()
            .Named("snapshot")
            .WithSource(WebViewSource.Html("<h1>snapshot</h1>"))
            .Build();
        runtime.Run(app);

        // Expected canonical lifecycle: init, then platform events for app_start,
        // surface_resized, window_frame_changed, frame_requested, app_shutdown.
        // app.start fires the lifecycle hook which loads the webview source,
        // so "webview.load" appears before SurfaceResized propagates downstream.
        AssertContainsInOrder(records,
            "runtime.init",
            "platform.event",         // AppStart
            "webview.load",           // emitted from LoadStartupWindows during AppStart
            "app.start",
            "surface.resize",
            "window.frame",
            "runtime.frame",
            "app.stop",
            "runtime.done");
    }

    [Fact]
    public void Bridge_dispatch_emits_dispatch_trace()
    {
        var records = new List<TraceRecord>();
        var platform = new NullPlatform();
        var registry = new BridgeRegistry().Register(new BridgeHandler("echo", inv => "{\"ok\":true}"));
        var runtime = new ZeroNative.Runtime.Runtime(new RuntimeOptions
        {
            Platform = platform,
            BridgeDispatcher = new BridgeDispatcher
            {
                Policy = new BridgePolicy(Enabled: true,
                    Commands: new[] { new BridgeCommandPolicy("echo", Origins: new[] { "zero://inline" }) }),
                Registry = registry,
            },
            TraceSink = r => records.Add(r),
        });

        runtime.DispatchPlatformEvent(new AppBuilder().Named("snapshot").Build(),
            new PlatformEvent.BridgeReceived(new BridgeMessage(
                "{\"id\":\"1\",\"command\":\"echo\",\"payload\":null}",
                "zero://inline", 7)));

        var names = records.Select(r => r.Name).ToList();
        Assert.Contains("bridge.dispatch", names);
        var dispatch = records.First(r => r.Name == "bridge.dispatch");
        Assert.True(dispatch.Fields.ContainsKey("request_bytes"));
        Assert.True(dispatch.Fields.ContainsKey("response_bytes"));
    }

    [Fact]
    public void Window_state_save_failure_is_logged_but_does_not_crash()
    {
        var records = new List<TraceRecord>();
        var failingStore = new ThrowingWindowStateStore();
        var platform = new NullPlatform();
        var runtime = new ZeroNative.Runtime.Runtime(new RuntimeOptions
        {
            Platform = platform,
            WindowStateStore = failingStore,
            TraceSink = r => records.Add(r),
        });

        runtime.DispatchPlatformEvent(new AppBuilder().Named("snapshot").Build(),
            new PlatformEvent.WindowFrameChanged(new WindowState
            {
                Id = 1,
                Label = "main",
                Title = "t",
                Frame = new RectF(0, 0, 100, 100),
                ScaleFactor = 1,
                Open = true,
                Focused = true,
            }));

        Assert.Contains(records, r => r.Name == "window.state.save_failed");
    }

    private static void AssertContainsInOrder(List<string> names, params string[] expected)
    {
        var cursor = 0;
        foreach (var e in expected)
        {
            var found = names.IndexOf(e, cursor);
            Assert.True(found >= 0,
                $"Expected '{e}' after index {cursor}. Actual sequence: [{string.Join(", ", names)}]");
            cursor = found + 1;
        }
    }

    private sealed class ThrowingWindowStateStore : ZeroNative.WindowStateStores.IWindowStateStore
    {
        public WindowState? LoadWindow(string label) => null;
        public IReadOnlyList<WindowState> LoadWindows() => Array.Empty<WindowState>();
        public void SaveWindow(WindowState state) => throw new IOException("disk full");
        public void RemoveWindow(string label) { }
    }
}
