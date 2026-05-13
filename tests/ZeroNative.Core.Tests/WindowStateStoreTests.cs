using Xunit;
using ZeroNative.Platform;
using ZeroNative.Primitives;
using ZeroNative.WindowStateStores;

namespace ZeroNative.Tests;

public class WindowStateStoreTests : IDisposable
{
    private readonly string _stateDir;

    public WindowStateStoreTests()
    {
        _stateDir = Path.Combine(Path.GetTempPath(), "ZeroNative.Tests.WS." + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_stateDir)) Directory.Delete(_stateDir, recursive: true);
    }

    [Fact]
    public void SaveAndLoadRoundTrip_PreservesGeometryAndFlags()
    {
        var store = new JsonWindowStateStore(_stateDir);
        var state = new WindowState
        {
            Id = 7,
            Label = "main",
            Title = "Main",
            Frame = new RectF(10, 20, 800, 600),
            ScaleFactor = 2,
            Open = true,
            Focused = true,
            Maximized = false,
            Fullscreen = false,
        };
        store.SaveWindow(state);

        var loaded = store.LoadWindow("main")!;
        Assert.Equal(7ul, loaded.Id);
        Assert.Equal("main", loaded.Label);
        Assert.Equal(new RectF(10, 20, 800, 600), loaded.Frame);
        Assert.Equal(2f, loaded.ScaleFactor);
        Assert.True(loaded.Open);
        Assert.True(loaded.Focused);
    }

    [Fact]
    public void SaveWindow_ReplacesExistingRecordByLabel()
    {
        var store = new JsonWindowStateStore(_stateDir);
        store.SaveWindow(new WindowState { Id = 1, Label = "main", Frame = new RectF(0, 0, 100, 100) });
        store.SaveWindow(new WindowState { Id = 1, Label = "main", Frame = new RectF(50, 50, 200, 200) });
        var loaded = store.LoadWindow("main")!;
        Assert.Equal(new RectF(50, 50, 200, 200), loaded.Frame);
        Assert.Single(store.LoadWindows());
    }

    [Fact]
    public void LoadWindow_ReturnsNullWhenLabelMissing()
    {
        var store = new JsonWindowStateStore(_stateDir);
        Assert.Null(store.LoadWindow("missing"));
    }

    [Fact]
    public void SaveWindow_IgnoresEmptyLabel()
    {
        var store = new JsonWindowStateStore(_stateDir);
        store.SaveWindow(new WindowState { Label = "" });
        Assert.Empty(store.LoadWindows());
    }

    [Fact]
    public void RemoveWindow_DropsRecord()
    {
        var store = new JsonWindowStateStore(_stateDir);
        store.SaveWindow(new WindowState { Id = 1, Label = "main" });
        store.SaveWindow(new WindowState { Id = 2, Label = "settings" });
        store.RemoveWindow("settings");
        Assert.Single(store.LoadWindows());
        Assert.Equal("main", store.LoadWindows()[0].Label);
    }

    [Fact]
    public void Corrupt_FileGracefullyReturnsEmpty()
    {
        Directory.CreateDirectory(_stateDir);
        File.WriteAllText(Path.Combine(_stateDir, "windows.json"), "this is not json");
        var store = new JsonWindowStateStore(_stateDir);
        Assert.Empty(store.LoadWindows());
    }
}
