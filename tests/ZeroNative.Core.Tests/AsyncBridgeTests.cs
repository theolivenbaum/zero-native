using Xunit;
using ZeroNative.Bridge;

namespace ZeroNative.Tests;

public class AsyncBridgeTests
{
    [Fact]
    public async Task DispatchAsync_PrefersAsyncHandlerAndReturnsAsyncOutcome()
    {
        string? responseSeen = null;
        var handler = new AsyncBridgeHandler("native.compute", async (inv, responder) =>
        {
            await Task.Yield();
            await responder.Success(inv.Request.Id, """{"value":42}""");
        });
        var dispatcher = new BridgeDispatcher
        {
            Policy = new BridgePolicy(Enabled: true,
                Commands: new[] { new BridgeCommandPolicy("native.compute", Origins: new[] { "*" }) }),
            Registry = new BridgeRegistry().Register(handler),
        };

        var outcome = await dispatcher.DispatchAsync(
            """{"id":"1","command":"native.compute","payload":null}""",
            new BridgeSource("zero://inline"),
            (source, response) => { responseSeen = response; return ValueTask.CompletedTask; });

        Assert.True(outcome.IsAsync);
        Assert.Null(outcome.Response);
        Assert.NotNull(responseSeen);
        Assert.Contains("\"value\":42", responseSeen);
        Assert.Contains("\"ok\":true", responseSeen);
    }

    [Fact]
    public async Task DispatchAsync_FallsBackToSyncWhenNoAsyncHandlerRegistered()
    {
        var dispatcher = new BridgeDispatcher
        {
            Policy = new BridgePolicy(Enabled: true,
                Commands: new[] { new BridgeCommandPolicy("native.sync", Origins: new[] { "*" }) }),
            Registry = new BridgeRegistry().Register(
                new BridgeHandler("native.sync", _ => """{"hello":true}""")),
        };

        var outcome = await dispatcher.DispatchAsync(
            """{"id":"sync","command":"native.sync","payload":null}""",
            new BridgeSource("*"));

        Assert.False(outcome.IsAsync);
        Assert.NotNull(outcome.Response);
        Assert.Contains("\"hello\":true", outcome.Response);
    }

    [Fact]
    public void Dispatch_RejectsAsyncOnlyHandlerCalledSynchronously()
    {
        var dispatcher = new BridgeDispatcher
        {
            Policy = new BridgePolicy(Enabled: true,
                Commands: new[] { new BridgeCommandPolicy("native.async", Origins: new[] { "*" }) }),
            Registry = new BridgeRegistry().Register(
                new AsyncBridgeHandler("native.async", (_, _) => ValueTask.CompletedTask)),
        };

        var response = dispatcher.Dispatch(
            """{"id":"1","command":"native.async","payload":null}""",
            new BridgeSource("*"));

        Assert.Contains("handler_failed", response);
    }
}
