using Xilium.CefGlue;

namespace ZeroNative.Cef;

/// <summary>
/// Bridges the CEF renderer process and the browser process for the JS<->.NET bridge.
/// In CEF, JavaScript runs in a separate renderer process, so we cannot call directly
/// into our managed code from a V8 callback. Instead:
///   1. The renderer registers a V8 handler that delivers the JSON payload to the
///      browser process via <see cref="CefProcessMessage"/>.
///   2. The browser process subscribes to that message and forwards it to the runtime.
/// </summary>
internal sealed class CefRenderHandler : CefRenderProcessHandler
{
    public const string ProcessMessageName = "zero-native-bridge";
    public const string GlobalSendName = "__zero_native_send";

    protected override void OnContextCreated(CefBrowser browser, CefFrame frame, CefV8Context context)
    {
        var global = context.GetGlobal();
        var fn = CefV8Value.CreateFunction(GlobalSendName, new BridgeV8Handler(browser));
        global.SetValue(GlobalSendName, fn, CefV8PropertyAttribute.ReadOnly | CefV8PropertyAttribute.DontEnum | CefV8PropertyAttribute.DontDelete);
    }
}

/// <summary>
/// JS-side function: <c>window.__zero_native_send(payload)</c>. Payload is the JSON
/// string the bridge shim already produced; we just forward it to the browser process.
/// </summary>
internal sealed class BridgeV8Handler : CefV8Handler
{
    private readonly CefBrowser _browser;

    public BridgeV8Handler(CefBrowser browser) => _browser = browser;

    protected override bool Execute(string name, CefV8Value @object, CefV8Value[] arguments, out CefV8Value? returnValue, out string? exception)
    {
        exception = null;
        returnValue = CefV8Value.CreateUndefined();
        if (arguments.Length == 0 || !arguments[0].IsString) return true;

        using var msg = CefProcessMessage.Create(CefRenderHandler.ProcessMessageName);
        var args = msg.Arguments;
        if (args is null) return true;
        args.SetString(0, arguments[0].GetStringValue());
        var frame = _browser.GetMainFrame();
        if (frame is not null)
            frame.SendProcessMessage(CefProcessId.Browser, msg);
        return true;
    }
}
