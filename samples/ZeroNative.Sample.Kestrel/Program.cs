using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZeroNative;
using ZeroNative.Bridge;
using ZeroNative.Platform;
using ZeroNative.Primitives;
using ZeroNative.Runtime;
using ZeroNative.Security;

// ZeroNative + Kestrel sample. Demonstrates the "C# everywhere" path:
//   - Kestrel runs in-process on an ephemeral 127.0.0.1 port and serves the
//     wwwroot static files plus a handful of /api/* endpoints.
//   - The ZeroNative WebView points at that local URL, so fetch() calls from
//     the page reach managed .NET code without any IPC bridge plumbing.
//   - The framework's command bridge is still wired up for the cases where
//     fetch() is the wrong tool (sync results, platform services, etc).
//
// The HTML frontend is intentionally plain — swap it for a Tesserae/H5 build
// (or any other JS/WASM frontend) by emitting the bundle into wwwroot/.

var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
{
    ContentRootPath = AppContext.BaseDirectory,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
    Args = args,
});

builder.Logging.ClearProviders();

var web = builder.Build();
web.Urls.Clear();
web.Urls.Add("http://127.0.0.1:0");
web.UseDefaultFiles();
web.UseStaticFiles();

web.MapGet("/api/echo", (string? message)
    => Results.Ok(new { received = message ?? "", at = DateTimeOffset.UtcNow }));

web.MapPost("/api/echo", async (HttpRequest req) =>
{
    using var reader = new StreamReader(req.Body);
    var body = await reader.ReadToEndAsync();
    return Results.Content(body, "application/json");
});

web.MapGet("/api/info", () => Results.Ok(new
{
    machine = Environment.MachineName,
    user = Environment.UserName,
    os = Environment.OSVersion.ToString(),
    cpus = Environment.ProcessorCount,
    process_id = Environment.ProcessId,
}));

await web.StartAsync();

var addresses = web.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
    ?? throw new InvalidOperationException("Kestrel did not publish any addresses.");
var baseUrl = addresses.Addresses.FirstOrDefault()
    ?? throw new InvalidOperationException("Kestrel started but bound no address.");

Console.Error.WriteLine($"[kestrel] listening at {baseUrl}");
var origin = SecurityPolicy.OriginOf(baseUrl);

var appInfo = new AppInfo
{
    AppName = "ZeroNative Sample (Kestrel)",
    WindowTitle = "ZeroNative Sample (Kestrel)",
    BundleId = "dev.zero_native.sample.kestrel",
    MainWindow = new WindowOptions
    {
        Id = 1,
        Label = "main",
        Title = "ZeroNative Sample (Kestrel)",
        DefaultFrame = new RectF(0, 0, 1024, 720),
    },
};

var platform = WebViewPlatform.CreateForCurrentOs(appInfo);

var dispatcher = new BridgeDispatcher
{
    Policy = new BridgePolicy(
        Enabled: true,
        Commands: new[]
        {
            new BridgeCommandPolicy("native.clipboard", Origins: new[] { origin, "*" }),
            new BridgeCommandPolicy("native.shell.open", Origins: new[] { origin, "*" }),
        }),
    Registry = new BridgeRegistry()
        .Register(new BridgeHandler("native.clipboard", _ =>
        {
            try { return System.Text.Json.JsonSerializer.Serialize(platform.Services.ReadClipboard()); }
            catch { return "null"; }
        }))
        .Register(new BridgeHandler("native.shell.open", invocation =>
        {
            var url = invocation.Request.Payload.Trim('"');
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                return "{\"opened\":true}";
            }
            catch (Exception ex)
            {
                return $"{{\"opened\":false,\"error\":{System.Text.Json.JsonSerializer.Serialize(ex.Message)}}}";
            }
        })),
};

var runtime = new Runtime(new RuntimeOptions
{
    Platform = platform,
    BridgeDispatcher = dispatcher,
    Security = new SecurityPolicy(
        Permissions: new[] { Permissions.Window, Permissions.Clipboard },
        Navigation: new NavigationPolicy(
            AllowedOrigins: new[] { origin },
            ExternalLinks: new ExternalLinkPolicy(ExternalLinkAction.OpenSystemBrowser))),
    JsWindowApi = true,
    TraceSink = record => Console.Error.WriteLine($"[{record.Name}] {record.Message}"),
});

var app = new AppBuilder()
    .Named("zero-native-sample-kestrel")
    .WithSource(WebViewSource.Url(baseUrl))
    .OnStop(_ =>
    {
        try { web.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(); }
        catch { /* best effort */ }
    })
    .Build();

try
{
    runtime.Run(app);
}
finally
{
    await web.StopAsync(TimeSpan.FromSeconds(2));
    await web.DisposeAsync();
}
