using Xunit;
using ZeroNative.Platform;

namespace ZeroNative.Tests;

public class NullPlatformTests
{
    [Fact]
    public void NullPlatform_EmitsDeterministicLifecycle()
    {
        var platform = new NullPlatform();
        var names = new List<string>();
        platform.Run(ev => names.Add(ev.Name));

        Assert.Equal(new[]
        {
            "app_start",
            "surface_resized",
            "window_frame_changed",
            "frame_requested",
            "app_shutdown",
        }, names);
    }

    [Fact]
    public void WebViewSource_AssetsCarriesOptions()
    {
        var source = WebViewSource.Assets(new WebViewAssetSource("dist", "index.html"));
        Assert.Equal(WebViewSourceKind.Assets, source.Kind);
        Assert.Equal("zero://app", source.Body);
        Assert.NotNull(source.AssetOptions);
        Assert.Equal("dist", source.AssetOptions!.RootPath);
        Assert.True(source.AssetOptions.SpaFallback);
    }
}
