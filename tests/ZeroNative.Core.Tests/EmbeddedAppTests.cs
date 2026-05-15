using Xunit;
using ZeroNative.Bridge;
using ZeroNative.Platform;
using ZeroNative.Primitives;
using ZeroNative.Runtime;

namespace ZeroNative.Tests;

public class EmbeddedAppTests
{
    [Fact]
    public void Start_resize_frame_stop_pumps_the_runtime()
    {
        var platform = new NullPlatform();
        var app = new AppBuilder()
            .Named("embedded")
            .WithSource(WebViewSource.Html("<p>hi</p>"))
            .Build();
        var embedded = new EmbeddedApp(app, platform);

        embedded.Start();
        embedded.Resize(new Surface { Size = new SizeF(800, 600), ScaleFactor = 2 });
        embedded.Frame();
        embedded.Stop();

        Assert.NotNull(platform.LoadedSource);
        Assert.Equal("<p>hi</p>", platform.LoadedSource!.Body);
        Assert.True(embedded.Runtime.FrameIndex >= 1);
    }

    [Fact]
    public void Bridge_method_forwards_messages_to_dispatcher()
    {
        var calls = 0;
        var registry = new BridgeRegistry().Register(new BridgeHandler("ping", _ =>
        {
            calls++;
            return "{\"pong\":true}";
        }));
        var platform = new NullPlatform();
        var embedded = new EmbeddedApp(
            new AppBuilder().Named("embedded").Build(),
            new RuntimeOptions
            {
                Platform = platform,
                BridgeDispatcher = new BridgeDispatcher
                {
                    Policy = new BridgePolicy(Enabled: true, Commands: new[] {
                        new BridgeCommandPolicy("ping", Origins: new[] { "zero://inline" })
                    }),
                    Registry = registry,
                },
            });

        embedded.Bridge(new BridgeMessage(
            "{\"id\":\"1\",\"command\":\"ping\",\"payload\":null}",
            "zero://inline", 1));

        Assert.Equal(1, calls);
        Assert.Contains("\"ok\":true", platform.LastBridgeResponse);
    }
}
