using Xunit;
using ZeroNative.Automation;
using ZeroNative.Platform;
using ZeroNative.Primitives;
using ZeroNative.Runtime;

namespace ZeroNative.Tests;

public class AutomationServerTests : IDisposable
{
    private readonly string _dir;

    public AutomationServerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ZeroNative.Tests.Automation." + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Publish_WritesSnapshotAccessibilityAndWindowsFiles()
    {
        var server = new AutomationServer(_dir, "Test");
        var input = new AutomationInput(
            new[] { new AutomationWindow(1, "Test", new RectF(0, 0, 800, 600)) },
            new AutomationDiagnostics(1, 0),
            WebViewSource.Html("<h1>Hi</h1>"));

        server.Publish(input);

        Assert.True(File.Exists(server.PathFor("snapshot.txt")));
        Assert.True(File.Exists(server.PathFor("accessibility.txt")));
        Assert.True(File.Exists(server.PathFor("windows.txt")));

        Assert.Contains("ready=true frame=1", File.ReadAllText(server.PathFor("snapshot.txt")));
        Assert.Contains("a11y root=@w1", File.ReadAllText(server.PathFor("accessibility.txt")));
        Assert.Contains("window @w1 \"Test\"", File.ReadAllText(server.PathFor("windows.txt")));
    }

    [Fact]
    public void PublishBridgeResponse_WritesBridgeResponseFile()
    {
        var server = new AutomationServer(_dir);
        server.PublishBridgeResponse("{\"id\":\"1\",\"ok\":true}");
        Assert.Equal("{\"id\":\"1\",\"ok\":true}", File.ReadAllText(server.PathFor("bridge-response.txt")));
    }

    [Fact]
    public void TakeCommand_ReturnsNullWhenFileMissing()
    {
        var server = new AutomationServer(_dir);
        Assert.Null(server.TakeCommand());
    }

    [Fact]
    public void TakeCommand_ConsumesCommandAndRewritesAsDone()
    {
        var server = new AutomationServer(_dir);
        Directory.CreateDirectory(_dir);
        File.WriteAllText(server.PathFor("command.txt"), "reload\n");

        var first = server.TakeCommand();
        Assert.NotNull(first);
        Assert.Equal(AutomationAction.Reload, first!.Action);

        Assert.Equal("done\n", File.ReadAllText(server.PathFor("command.txt")));
        Assert.Null(server.TakeCommand());
    }

    [Fact]
    public void TakeCommand_DoneSentinel_ReturnsNull()
    {
        var server = new AutomationServer(_dir);
        Directory.CreateDirectory(_dir);
        File.WriteAllText(server.PathFor("command.txt"), "done\n");
        Assert.Null(server.TakeCommand());
    }

    [Fact]
    public void TakeCommand_MalformedLine_ReturnsNull()
    {
        var server = new AutomationServer(_dir);
        Directory.CreateDirectory(_dir);
        File.WriteAllText(server.PathFor("command.txt"), "nonsense\n");
        Assert.Null(server.TakeCommand());
    }

    [Fact]
    public void TakeCommand_TooLarge_Throws()
    {
        var server = new AutomationServer(_dir);
        Directory.CreateDirectory(_dir);
        var huge = new string('a', AutomationProtocol.MaxCommandBytes + 1);
        File.WriteAllText(server.PathFor("command.txt"), huge);
        Assert.Throws<AutomationCommandTooLargeException>(() => server.TakeCommand());
    }

    [Fact]
    public void Runtime_BuildAutomationInput_RoundTripsThroughServer()
    {
        var platform = new NullPlatform();
        var runtime = new ZeroNative.Runtime.Runtime(new RuntimeOptions { Platform = platform });
        var app = new AppBuilder()
            .Named("test")
            .WithSource(WebViewSource.Html("<h1>Hello</h1>"))
            .Build();
        runtime.Run(app);

        var server = new AutomationServer(_dir, "Test");
        server.Publish(runtime.BuildAutomationInput());

        var snapshot = File.ReadAllText(server.PathFor("snapshot.txt"));
        Assert.Contains("ready=true", snapshot);
        Assert.Contains("window @w1", snapshot);
        Assert.Contains("source kind=html bytes=14", snapshot);
    }
}
