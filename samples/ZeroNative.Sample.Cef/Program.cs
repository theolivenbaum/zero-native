using ZeroNative.Cef;
using ZeroNative.Bridge;
using ZeroNative.Platform;
using ZeroNative.Primitives;
using ZeroNative.Runtime;
using ZeroNative.Security;

// CefGlue.Common ships the CEF binaries via the CefGlueTargetPlatform property in the .csproj.
// Override CEF_DIR if you want to use a custom CEF distribution path.
var cefDir = Environment.GetEnvironmentVariable("CEF_DIR");

var appInfo = new AppInfo
{
    AppName = "ZeroNative Sample (CEF)",
    WindowTitle = "ZeroNative Sample (CEF)",
    BundleId = "dev.zero_native.sample.cef",
    MainWindow = new WindowOptions
    {
        Id = 1,
        Label = "main",
        Title = "ZeroNative Sample (CEF)",
        DefaultFrame = new RectF(0, 0, 960, 600),
    },
};

var platform = CefPlatform.CreateForCurrentOs(appInfo, new CefPlatformOptions
{
    CefDirectory = cefDir,
});

var dispatcher = new BridgeDispatcher
{
    Policy = new BridgePolicy(
        Enabled: true,
        Commands: new[] { new BridgeCommandPolicy("native.ping", Origins: new[] { "*" }) }),
    Registry = new BridgeRegistry()
        .Register(new BridgeHandler("native.ping", invocation =>
            $"{{\"pong\":true,\"echo\":{invocation.Request.Payload}}}")),
};

var runtime = new Runtime(new RuntimeOptions
{
    Platform = platform,
    BridgeDispatcher = dispatcher,
    Security = new SecurityPolicy(
        Navigation: new NavigationPolicy(AllowedOrigins: new[] { "*" })),
    TraceSink = record => Console.Error.WriteLine($"[{record.Name}] {record.Message}"),
});

var app = new AppBuilder()
    .Named("zero-native-sample-cef")
    .WithSource(WebViewSource.Url("https://example.org/"))
    .Build();

runtime.Run(app);
