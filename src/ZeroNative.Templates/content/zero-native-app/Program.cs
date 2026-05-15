using ZeroNative;
using ZeroNative.Bridge;
using ZeroNative.Platform;
using ZeroNative.Primitives;
using ZeroNative.Runtime;
using ZeroNative.Security;

var appInfo = new AppInfo
{
    AppName = "ZeroNativeApp",
    BundleId = "ZERO_NATIVE_BUNDLE_ID",
    MainWindow = new WindowOptions
    {
        Id = 1,
        Label = "main",
        Title = "ZeroNativeApp",
        DefaultFrame = new RectF(0, 0, 1024, 720),
    },
};

var platform = WebViewPlatform.CreateForCurrentOs(appInfo);

var registry = new BridgeRegistry()
    .Register(new BridgeHandler("native.ping", invocation =>
        $"{{\"pong\":true,\"echo\":{invocation.Request.Payload}}}"));

var dispatcher = new BridgeDispatcher
{
    Policy = new BridgePolicy(
        Enabled: true,
        Commands: new[]
        {
            new BridgeCommandPolicy("native.ping", Origins: new[] { "zero://inline", "*" }),
        }),
    Registry = registry,
};

var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html"));

var runtime = new Runtime(new RuntimeOptions
{
    Platform = platform,
    BridgeDispatcher = dispatcher,
    Security = new SecurityPolicy(
        Permissions: new[] { Permissions.Window },
        Navigation: new NavigationPolicy(AllowedOrigins: new[] { "zero://inline", "*" })),
    JsWindowApi = true,
    TraceSink = TraceSinks.Console,
});

var app = new AppBuilder()
    .Named("zero-native-app")
    .WithSource(WebViewSource.Html(html))
    .Build();

runtime.Run(app);
