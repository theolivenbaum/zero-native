using Xunit;
using ZeroNative.Bridge;

namespace ZeroNative.Tests;

public class BridgeTests
{
    [Fact]
    public void Bridge_ParsesValidRequest()
    {
        var req = Bridge.Bridge.ParseRequest("""{"id":"1","command":"native.ping","payload":{"text":"hello"}}""");
        Assert.Equal("1", req.Id);
        Assert.Equal("native.ping", req.Command);
        Assert.Contains("\"text\":\"hello\"", req.Payload);
    }

    [Fact]
    public void Bridge_RejectsMalformedRequest()
    {
        Assert.Throws<BridgeParseException>(() => Bridge.Bridge.ParseRequest("{}"));
        Assert.Throws<BridgeParseException>(() =>
            Bridge.Bridge.ParseRequest("""{"id":"","command":"native.ping"}"""));
        Assert.Throws<BridgeParseException>(() =>
            Bridge.Bridge.ParseRequest("""{"id":"1","command":"bad command"}"""));
    }

    [Fact]
    public void Bridge_WritesSuccessAndErrorResponses()
    {
        Assert.Equal("""{"id":"abc","ok":true,"result":{"pong":true}}""",
            Bridge.Bridge.WriteSuccessResponse("abc", """{"pong":true}"""));

        Assert.Equal("""{"id":"abc","ok":false,"error":{"code":"permission_denied","message":"Denied"}}""",
            Bridge.Bridge.WriteErrorResponse("abc", BridgeErrorCode.PermissionDenied, "Denied"));
    }

    [Fact]
    public void Bridge_RejectsInvalidJsonResult()
    {
        var response = Bridge.Bridge.WriteSuccessResponse("abc", """raw "user" text""");
        Assert.Contains("handler_failed", response);
    }

    [Fact]
    public void Dispatcher_RoutesToRegisteredHandler()
    {
        var calls = 0;
        var registry = new BridgeRegistry().Register(new BridgeHandler(
            "native.ping",
            inv =>
            {
                calls++;
                Assert.Equal("zero://inline", inv.Source.Origin);
                return """{"pong":true}""";
            }));
        var dispatcher = new BridgeDispatcher
        {
            Policy = new BridgePolicy(
                Enabled: true,
                Commands: new[] { new BridgeCommandPolicy("native.ping", Origins: new[] { "zero://inline" }) }),
            Registry = registry,
        };
        var response = dispatcher.Dispatch(
            """{"id":"1","command":"native.ping","payload":{"value":1}}""",
            new BridgeSource("zero://inline"));
        Assert.Equal(1, calls);
        Assert.Equal("""{"id":"1","ok":true,"result":{"pong":true}}""", response);
    }

    [Fact]
    public void Dispatcher_DeniesByOrigin()
    {
        var dispatcher = new BridgeDispatcher
        {
            Policy = new BridgePolicy(
                Enabled: true,
                Commands: new[] { new BridgeCommandPolicy("native.ping", Origins: new[] { "zero://app" }) }),
        };
        var response = dispatcher.Dispatch(
            """{"id":"1","command":"native.ping","payload":null}""",
            new BridgeSource("zero://inline"));
        Assert.Contains("permission_denied", response);
    }

    [Fact]
    public void Dispatcher_ReportsUnknownCommandAfterAllow()
    {
        var dispatcher = new BridgeDispatcher
        {
            Policy = new BridgePolicy(
                Enabled: true,
                Commands: new[] { new BridgeCommandPolicy("native.ping", Origins: new[] { "*" }) }),
        };
        var response = dispatcher.Dispatch(
            """{"id":"1","command":"native.ping","payload":null}""",
            new BridgeSource("zero://inline"));
        Assert.Contains("unknown_command", response);
    }
}
