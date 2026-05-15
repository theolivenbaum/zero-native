using ZeroNative;
using ZeroNative.Bridge;
using ZeroNative.Platform;
using ZeroNative.Primitives;
using ZeroNative.Runtime;
using ZeroNative.Security;

const string Html = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width,initial-scale=1" />
  <title>ZeroNative Sample</title>
  <style>
    body { font-family: -apple-system, system-ui, sans-serif; padding: 2rem; }
    button { padding: 0.5rem 1rem; font-size: 1rem; }
  </style>
</head>
<body>
  <h1>ZeroNative .NET 10 sample</h1>
  <p>Running on the system WebView (WebView2 / WKWebView / WebKitGTK).</p>
  <button id="ping">Ping native</button>
  <button id="windows">List windows</button>
  <button id="clipboard">Read clipboard</button>
  <pre id="output"></pre>
  <script>
    function show(label, value) {
      document.getElementById('output').textContent = label + '\n' + JSON.stringify(value, null, 2);
    }
    document.getElementById('ping').addEventListener('click', async () => {
      try { show('native.ping', await window.zero.invoke('native.ping', { from: 'browser' })); }
      catch (e) { show('error', { code: e.code, message: e.message }); }
    });
    document.getElementById('windows').addEventListener('click', async () => {
      try { show('windows', await window.zero.window.list()); }
      catch (e) { show('error', { code: e.code, message: e.message }); }
    });
    document.getElementById('clipboard').addEventListener('click', async () => {
      try { show('clipboard', await window.zero.invoke('native.clipboard', null)); }
      catch (e) { show('error', { code: e.code, message: e.message }); }
    });
  </script>
</body>
</html>
""";

var appInfo = new AppInfo
{
    AppName = "ZeroNative Sample",
    WindowTitle = "ZeroNative Sample",
    BundleId = "dev.zero_native.sample",
    MainWindow = new WindowOptions
    {
        Id = 1,
        Label = "main",
        Title = "ZeroNative Sample",
        DefaultFrame = new RectF(0, 0, 960, 600),
    },
};

var platform = WebViewPlatform.CreateForCurrentOs(appInfo);

var registry = new BridgeRegistry()
    .Register(new BridgeHandler("native.ping", invocation =>
    {
        return $"{{\"pong\":true,\"echo\":{invocation.Request.Payload}}}";
    }))
    .Register(new BridgeHandler("native.clipboard", _ =>
    {
        try { return System.Text.Json.JsonSerializer.Serialize(platform.Services.ReadClipboard()); }
        catch { return "null"; }
    }));

var dispatcher = new BridgeDispatcher
{
    Policy = new BridgePolicy(
        Enabled: true,
        Commands: new[]
        {
            new BridgeCommandPolicy("native.ping", Origins: new[] { "zero://inline", "*" }),
            new BridgeCommandPolicy("native.clipboard", Origins: new[] { "zero://inline", "*" }),
        }),
    Registry = registry,
};

var runtime = new Runtime(new RuntimeOptions
{
    Platform = platform,
    BridgeDispatcher = dispatcher,
    Security = new SecurityPolicy(
        Permissions: new[] { Permissions.Window },
        Navigation: new NavigationPolicy(AllowedOrigins: new[] { "zero://app", "zero://inline", "*" })),
    JsWindowApi = true,
    TraceSink = record =>
    {
        Console.Error.WriteLine($"[{record.Name}] {record.Message}");
    },
});

var app = new AppBuilder()
    .Named("zero-native-sample")
    .WithSource(WebViewSource.Html(Html))
    .OnStart(_ => Console.Error.WriteLine("App started"))
    .OnStop(_ => Console.Error.WriteLine("App stopped"))
    .Build();

runtime.Run(app);
