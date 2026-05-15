using Xunit;
using ZeroNative.Bridge;
using ZeroNative.Platform;
using ZeroNative.Primitives;
using ZeroNative.Runtime;
using ZeroNative.Security;

namespace ZeroNative.Tests;

public class RuntimeTests
{
    private static (ZeroNative.Runtime.Runtime Runtime, NullPlatform Platform) CreateHarness(
        RuntimeOptions? overrideOptions = null)
    {
        var platform = new NullPlatform();
        var opts = overrideOptions ?? new RuntimeOptions { Platform = platform };
        return (new ZeroNative.Runtime.Runtime(opts with { Platform = platform }), platform);
    }

    [Fact]
    public void Runtime_LoadsHtmlSourceIntoPlatform()
    {
        var (runtime, platform) = CreateHarness();
        var app = new AppBuilder()
            .Named("test")
            .WithSource(WebViewSource.Html("<h1>Hello</h1>"))
            .Build();

        runtime.Run(app);

        Assert.NotNull(platform.LoadedSource);
        Assert.Equal(WebViewSourceKind.Html, platform.LoadedSource!.Kind);
        Assert.Equal("<h1>Hello</h1>", platform.LoadedSource.Body);
        Assert.Equal(1ul, runtime.FrameIndex);
    }

    [Fact]
    public void Runtime_DispatchesBridgeMessagesThroughPolicy()
    {
        var calls = 0;
        var registry = new BridgeRegistry().Register(new BridgeHandler(
            "native.ping",
            inv =>
            {
                calls++;
                Assert.Equal("native.ping", inv.Request.Command);
                Assert.Equal("zero://inline", inv.Source.Origin);
                return $"{{\"pong\":true,\"calls\":{calls}}}";
            }));

        var platform = new NullPlatform();
        var runtime = new ZeroNative.Runtime.Runtime(new RuntimeOptions
        {
            Platform = platform,
            BridgeDispatcher = new BridgeDispatcher
            {
                Policy = new BridgePolicy(
                    Enabled: true,
                    Commands: new[] { new BridgeCommandPolicy("native.ping", Origins: new[] { "zero://inline" }) }),
                Registry = registry,
            },
        });

        runtime.DispatchPlatformEvent(new AppBuilder().Named("bridge").Build(),
            new PlatformEvent.BridgeReceived(new BridgeMessage(
                """{"id":"1","command":"native.ping","payload":{"source":"webview","count":1}}""",
                "zero://inline",
                4)));

        Assert.Equal(1, calls);
        Assert.Equal(4ul, platform.LastBridgeResponseWindowId);
        Assert.Contains("\"ok\":true", platform.LastBridgeResponse);
        Assert.Contains("\"calls\":1", platform.LastBridgeResponse);
    }

    [Fact]
    public void Runtime_BuiltinWindowApi_GatedByPermissionAndOrigin()
    {
        var platform = new NullPlatform();
        var runtime = new ZeroNative.Runtime.Runtime(new RuntimeOptions
        {
            Platform = platform,
            JsWindowApi = true,
            Security = new SecurityPolicy(Permissions: new[] { Permissions.Window }),
        });

        // Denied by origin (not in allowed_origins default)
        runtime.DispatchPlatformEvent(new AppBuilder().Named("origin").Build(),
            new PlatformEvent.BridgeReceived(new BridgeMessage(
                """{"id":"origin","command":"zero-native.window.list","payload":null}""",
                "https://example.invalid", 1)));
        Assert.Contains("permission_denied", platform.LastBridgeResponse);

        // Allowed from inline origin with window permission
        runtime.DispatchPlatformEvent(new AppBuilder().Named("allowed").Build(),
            new PlatformEvent.BridgeReceived(new BridgeMessage(
                """{"id":"allowed","command":"zero-native.window.list","payload":null}""",
                "zero://inline", 1)));
        Assert.Contains("\"ok\":true", platform.LastBridgeResponse);
    }

    [Fact]
    public void Runtime_CreateFocusCloseWindows()
    {
        var platform = new NullPlatform();
        var runtime = new ZeroNative.Runtime.Runtime(new RuntimeOptions { Platform = platform });
        var app = new AppBuilder()
            .Named("multi")
            .WithSource(WebViewSource.Html("<p>multi</p>"))
            .Build();
        runtime.Run(app);

        var info = runtime.CreateWindow(new WindowCreateOptions
        {
            Label = "tools",
            Title = "Tools",
        });
        Assert.True(info.Id >= 2);
        Assert.Equal("tools", info.Label);

        runtime.FocusWindow(info.Id);
        Assert.True(runtime.Windows.First(w => w.Id == info.Id).Focused);

        runtime.CloseWindow(info.Id);
        Assert.False(runtime.Windows.First(w => w.Id == info.Id).Open);
    }

    [Fact]
    public void Runtime_CreateWindow_LoadsPerWindowSource()
    {
        var platform = new NullPlatform();
        var runtime = new ZeroNative.Runtime.Runtime(new RuntimeOptions { Platform = platform });
        var app = new AppBuilder()
            .Named("multi")
            .WithSource(WebViewSource.Html("<p>primary</p>"))
            .Build();
        runtime.Run(app);

        var secondary = runtime.CreateWindow(new WindowCreateOptions
        {
            Label = "tools",
            Title = "Tools",
            Source = WebViewSource.Html("<p>tools</p>"),
        });

        // The primary window kept its source...
        Assert.Equal("<p>primary</p>", platform.WindowSources[1].Body);
        // ...and the new window received its own source via LoadWindowWebView.
        Assert.Equal("<p>tools</p>", platform.WindowSources[secondary.Id].Body);
        Assert.NotEqual(1ul, secondary.Id);
    }

    [Fact]
    public void Runtime_CreateWindow_FallsBackToLoadedSource_WhenSourceOmitted()
    {
        var platform = new NullPlatform();
        var runtime = new ZeroNative.Runtime.Runtime(new RuntimeOptions { Platform = platform });
        var app = new AppBuilder()
            .Named("multi")
            .WithSource(WebViewSource.Html("<p>shared</p>"))
            .Build();
        runtime.Run(app);

        var secondary = runtime.CreateWindow(new WindowCreateOptions
        {
            Label = "tools",
            Title = "Tools",
        });

        Assert.Equal("<p>shared</p>", platform.WindowSources[secondary.Id].Body);
    }

    [Fact]
    public void Runtime_BridgeResponse_RoutesToOriginatingWindow()
    {
        var registry = new BridgeRegistry().Register(new BridgeHandler(
            "native.echo",
            inv => $"{{\"window\":{inv.Source.WindowId}}}"));

        var platform = new NullPlatform();
        var runtime = new ZeroNative.Runtime.Runtime(new RuntimeOptions
        {
            Platform = platform,
            BridgeDispatcher = new BridgeDispatcher
            {
                Policy = new BridgePolicy(
                    Enabled: true,
                    Commands: new[] { new BridgeCommandPolicy("native.echo", Origins: new[] { "zero://inline" }) }),
                Registry = registry,
            },
        });
        runtime.Run(new AppBuilder().Named("multi").WithSource(WebViewSource.Html("<p/>")).Build());
        var secondary = runtime.CreateWindow(new WindowCreateOptions { Label = "tools", Title = "Tools" });

        runtime.DispatchPlatformEvent(new AppBuilder().Named("bridge").Build(),
            new PlatformEvent.BridgeReceived(new BridgeMessage(
                """{"id":"a","command":"native.echo","payload":{}}""",
                "zero://inline",
                WindowId: secondary.Id)));

        Assert.True(platform.WindowBridgeResponses.TryGetValue(secondary.Id, out var response));
        Assert.Contains($"\"window\":{secondary.Id}", response);
        // The primary window saw no bridge response of its own.
        Assert.False(platform.WindowBridgeResponses.ContainsKey(1));
    }

    [Fact]
    public void Runtime_EmitWindowEvent_RoutesToTargetWindow()
    {
        var platform = new NullPlatform();
        var runtime = new ZeroNative.Runtime.Runtime(new RuntimeOptions { Platform = platform });
        runtime.Run(new AppBuilder().Named("multi").WithSource(WebViewSource.Html("<p/>")).Build());
        var secondary = runtime.CreateWindow(new WindowCreateOptions { Label = "tools", Title = "Tools" });

        runtime.EmitWindowEvent(secondary.Id, "zero:tools-ready", """{"ok":true}""");
        runtime.EmitWindowEvent(1, "zero:primary-tick", "42");

        Assert.Collection(platform.WindowEvents,
            ev =>
            {
                Assert.Equal(secondary.Id, ev.WindowId);
                Assert.Equal("zero:tools-ready", ev.Name);
                Assert.Equal("""{"ok":true}""", ev.DetailJson);
            },
            ev =>
            {
                Assert.Equal(1ul, ev.WindowId);
                Assert.Equal("zero:primary-tick", ev.Name);
                Assert.Equal("42", ev.DetailJson);
            });
    }

    [Fact]
    public void Runtime_CreateWindow_RejectsDuplicateIdAndLabel()
    {
        var platform = new NullPlatform();
        var runtime = new ZeroNative.Runtime.Runtime(new RuntimeOptions { Platform = platform });
        runtime.Run(new AppBuilder().Named("multi").WithSource(WebViewSource.Html("<p/>")).Build());

        var first = runtime.CreateWindow(new WindowCreateOptions { Label = "tools", Title = "Tools" });

        Assert.Throws<DuplicateWindowException>(() => runtime.CreateWindow(new WindowCreateOptions
        {
            Id = first.Id,
            Label = "other",
        }));
        Assert.Throws<DuplicateWindowException>(() => runtime.CreateWindow(new WindowCreateOptions
        {
            Label = "tools",
        }));
    }
}
