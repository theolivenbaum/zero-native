using Xunit;
using ZeroNative.Platform;
using ZeroNative.Primitives;
using ZeroNative.Runtime;
using ZeroNative.WindowStateStores;

namespace ZeroNative.Tests;

public class WindowStateRestoreTests
{
    [Fact]
    public void Apply_RestoresPersistedGeometryIntoAppInfo()
    {
        using var dir = new TempDir();
        var store = new JsonWindowStateStore(dir.Path);
        store.SaveWindow(new Platform.WindowState
        {
            Id = 1,
            Label = "main",
            Frame = new RectF(150, 200, 1000, 700),
            ScaleFactor = 2,
        });

        var info = new AppInfo
        {
            MainWindow = new WindowOptions { Id = 1, Label = "main", DefaultFrame = new RectF(0, 0, 720, 480) },
        };

        var restored = WindowStateRestoration.Apply(info, store);
        Assert.Equal(150f, restored.MainWindow.DefaultFrame.X);
        Assert.Equal(1000f, restored.MainWindow.DefaultFrame.Width);
    }

    [Fact]
    public void Apply_LeavesDefault_WhenRestoreStateOff()
    {
        using var dir = new TempDir();
        var store = new JsonWindowStateStore(dir.Path);
        store.SaveWindow(new Platform.WindowState
        {
            Id = 1,
            Label = "main",
            Frame = new RectF(150, 200, 1000, 700),
        });

        var info = new AppInfo
        {
            MainWindow = new WindowOptions
            {
                Id = 1,
                Label = "main",
                DefaultFrame = new RectF(0, 0, 720, 480),
                RestoreState = false,
            },
        };

        var restored = WindowStateRestoration.Apply(info, store);
        Assert.Equal(720f, restored.MainWindow.DefaultFrame.Width);
        Assert.Equal(480f, restored.MainWindow.DefaultFrame.Height);
    }

    [Fact]
    public void Apply_RejectsBogusFrame()
    {
        using var dir = new TempDir();
        var store = new JsonWindowStateStore(dir.Path);
        store.SaveWindow(new Platform.WindowState
        {
            Id = 1,
            Label = "main",
            Frame = new RectF(0, 0, -10, -5),
        });

        var info = new AppInfo
        {
            MainWindow = new WindowOptions { Id = 1, Label = "main", DefaultFrame = new RectF(0, 0, 720, 480) },
        };

        var restored = WindowStateRestoration.Apply(info, store);
        Assert.Equal(720f, restored.MainWindow.DefaultFrame.Width);
        Assert.Equal(480f, restored.MainWindow.DefaultFrame.Height);
    }

    [Fact]
    public void Apply_CenterOnPrimary_ClearsPersistedOffsets()
    {
        using var dir = new TempDir();
        var store = new JsonWindowStateStore(dir.Path);
        store.SaveWindow(new Platform.WindowState
        {
            Id = 1,
            Label = "main",
            Frame = new RectF(800, 900, 640, 360),
        });

        var info = new AppInfo
        {
            MainWindow = new WindowOptions
            {
                Id = 1,
                Label = "main",
                DefaultFrame = new RectF(0, 0, 720, 480),
                RestorePolicy = WindowRestorePolicy.CenterOnPrimary,
            },
        };

        var restored = WindowStateRestoration.Apply(info, store);
        Assert.Equal(0f, restored.MainWindow.DefaultFrame.X);
        Assert.Equal(0f, restored.MainWindow.DefaultFrame.Y);
        Assert.Equal(640f, restored.MainWindow.DefaultFrame.Width);
    }

    [Fact]
    public void Run_AppliesPersistedFrameToPrimaryWindowViaPlatform()
    {
        using var dir = new TempDir();
        var store = new JsonWindowStateStore(dir.Path);
        store.SaveWindow(new Platform.WindowState
        {
            Id = 1,
            Label = "main",
            Frame = new RectF(123, 456, 1280, 800),
        });

        var info = new AppInfo
        {
            MainWindow = new WindowOptions { Id = 1, Label = "main", DefaultFrame = new RectF(0, 0, 640, 480) },
        };

        var platform = new NullPlatform(new Surface(), WebEngine.System, info);
        var runtime = new ZeroNative.Runtime.Runtime(new RuntimeOptions
        {
            Platform = platform,
            WindowStateStore = store,
        });

        runtime.Run(new AppBuilder().WithSource(WebViewSource.Html("<p/>")).Build());

        Assert.True(platform.WindowFrameOverrides.TryGetValue(1, out var frame));
        Assert.Equal(123f, frame.X);
        Assert.Equal(456f, frame.Y);
        Assert.Equal(1280f, frame.Width);
        Assert.Equal(800f, frame.Height);
    }

    [Fact]
    public void Run_SkipsSetFrame_WhenNoPersistedState()
    {
        using var dir = new TempDir();
        var store = new JsonWindowStateStore(dir.Path);

        var info = new AppInfo
        {
            MainWindow = new WindowOptions { Id = 1, Label = "main", DefaultFrame = new RectF(0, 0, 640, 480) },
        };

        var platform = new NullPlatform(new Surface(), WebEngine.System, info);
        var runtime = new ZeroNative.Runtime.Runtime(new RuntimeOptions
        {
            Platform = platform,
            WindowStateStore = store,
        });

        runtime.Run(new AppBuilder().WithSource(WebViewSource.Html("<p/>")).Build());
        Assert.Empty(platform.WindowFrameOverrides);
    }

    [Fact]
    public void SetWindowFrame_UpdatesRuntimeStateAndPersists()
    {
        using var dir = new TempDir();
        var store = new JsonWindowStateStore(dir.Path);

        var info = new AppInfo
        {
            MainWindow = new WindowOptions { Id = 1, Label = "main", DefaultFrame = new RectF(0, 0, 640, 480) },
        };

        var platform = new NullPlatform(new Surface(), WebEngine.System, info);
        var runtime = new ZeroNative.Runtime.Runtime(new RuntimeOptions
        {
            Platform = platform,
            WindowStateStore = store,
        });
        runtime.Run(new AppBuilder().WithSource(WebViewSource.Html("<p/>")).Build());

        runtime.SetWindowFrame(1, new RectF(100, 200, 1024, 768));

        var window = runtime.Windows.Single(w => w.Id == 1);
        Assert.Equal(new RectF(100, 200, 1024, 768), window.Frame);

        var persisted = store.LoadWindow("main");
        Assert.NotNull(persisted);
        Assert.Equal(new RectF(100, 200, 1024, 768), persisted!.Frame);
    }

    [Fact]
    public void CreateWindow_RuntimeAppliesPersistedFrame()
    {
        using var dir = new TempDir();
        var store = new JsonWindowStateStore(dir.Path);
        store.SaveWindow(new Platform.WindowState
        {
            Id = 0,
            Label = "tools",
            Frame = new RectF(50, 75, 900, 600),
        });

        var platform = new NullPlatform();
        var runtime = new ZeroNative.Runtime.Runtime(new RuntimeOptions
        {
            Platform = platform,
            WindowStateStore = store,
        });
        runtime.Run(new AppBuilder().WithSource(WebViewSource.Html("<p/>")).Build());

        var info = runtime.CreateWindow(new WindowCreateOptions
        {
            Label = "tools",
            Title = "Tools",
            DefaultFrame = new RectF(0, 0, 400, 300),
            RestoreState = true,
        });

        Assert.Equal(50f, info.Frame.X);
        Assert.Equal(75f, info.Frame.Y);
        Assert.Equal(900f, info.Frame.Width);
        Assert.Equal(600f, info.Frame.Height);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ZeroNativeTests_" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* ignore */ }
        }
    }
}
