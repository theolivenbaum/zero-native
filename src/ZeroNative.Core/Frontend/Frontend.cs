using ZeroNative.Platform;

namespace ZeroNative.Frontend;

public sealed record FrontendOptions
{
    public string Dist { get; init; } = "dist";
    public string Entry { get; init; } = "index.html";
    public string Origin { get; init; } = "zero://app";
    public bool SpaFallback { get; init; } = true;
    public string DevUrlEnv { get; init; } = "ZERO_NATIVE_FRONTEND_URL";
}

/// <summary>
/// Picks the right <see cref="WebViewSource"/> for the current run: the dev URL
/// from <c>ZERO_NATIVE_FRONTEND_URL</c> when set (used by hot-reload flows that
/// went through <see cref="ZeroNative.Tooling.DevServer"/>), otherwise an asset
/// source pointed at the production bundle. Mirrors the Zig
/// <c>src/frontend/root.zig</c> helper so the same env var unlocks dev mode
/// across hosts.
/// </summary>
public static class FrontendSources
{
    public static WebViewSource SourceFromEnvironment(FrontendOptions? options = null)
    {
        options ??= new FrontendOptions();
        var url = Environment.GetEnvironmentVariable(options.DevUrlEnv);
        if (!string.IsNullOrEmpty(url)) return WebViewSource.Url(url);
        return ProductionSource(options);
    }

    public static WebViewSource ProductionSource(FrontendOptions? options = null)
    {
        options ??= new FrontendOptions();
        return WebViewSource.Assets(new WebViewAssetSource(
            RootPath: options.Dist,
            Entry: options.Entry,
            Origin: options.Origin,
            SpaFallback: options.SpaFallback));
    }
}
