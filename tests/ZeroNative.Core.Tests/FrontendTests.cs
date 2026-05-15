using Xunit;
using ZeroNative.Frontend;
using ZeroNative.Platform;

namespace ZeroNative.Tests;

public class FrontendTests
{
    [Fact]
    public void Source_prefers_dev_url_when_env_var_is_set()
    {
        const string EnvVar = "ZERO_NATIVE_FRONTEND_URL_TEST_A";
        Environment.SetEnvironmentVariable(EnvVar, "http://127.0.0.1:5173/");
        try
        {
            var src = FrontendSources.SourceFromEnvironment(new FrontendOptions { DevUrlEnv = EnvVar });
            Assert.Equal(WebViewSourceKind.Url, src.Kind);
            Assert.Equal("http://127.0.0.1:5173/", src.Body);
        }
        finally { Environment.SetEnvironmentVariable(EnvVar, null); }
    }

    [Fact]
    public void Source_falls_back_to_production_assets_when_env_missing()
    {
        const string EnvVar = "ZERO_NATIVE_FRONTEND_URL_TEST_B";
        Environment.SetEnvironmentVariable(EnvVar, null);
        var src = FrontendSources.SourceFromEnvironment(new FrontendOptions
        {
            DevUrlEnv = EnvVar,
            Dist = "frontend/dist",
            Entry = "app.html",
        });
        Assert.Equal(WebViewSourceKind.Assets, src.Kind);
        Assert.NotNull(src.AssetOptions);
        Assert.Equal("frontend/dist", src.AssetOptions!.RootPath);
        Assert.Equal("app.html", src.AssetOptions.Entry);
    }
}
