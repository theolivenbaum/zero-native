using Xunit;
using ZeroNative.Bridge;

namespace ZeroNative.Tests;

public class BridgeJavascriptTests
{
    [Fact]
    public void Build_ChromeWebView_UsesChromePostMessage()
    {
        var js = BridgeJavascript.Build(BridgeJavascript.Channel.ChromeWebView);
        Assert.Contains("window.chrome.webview.postMessage", js);
        Assert.Contains("window.zero", js);
        Assert.Contains("__zero_native_bridge_response", js);
    }

    [Fact]
    public void Build_WebKitMessageHandler_UsesWebKitHandler()
    {
        var js = BridgeJavascript.Build(BridgeJavascript.Channel.WebKitMessageHandler);
        Assert.Contains($"window.webkit.messageHandlers.{BridgeJavascript.HandlerName}.postMessage", js);
    }

    [Fact]
    public void Build_GlobalFunction_UsesGlobalSendName()
    {
        var js = BridgeJavascript.Build(BridgeJavascript.Channel.GlobalFunction);
        Assert.Contains("window.__zero_native_send", js);
    }

    [Fact]
    public void Build_ExposesWindowAndDialogShortcuts()
    {
        var js = BridgeJavascript.Build(BridgeJavascript.Channel.ChromeWebView);
        Assert.Contains("zero-native.window.list", js);
        Assert.Contains("zero-native.window.create", js);
        Assert.Contains("zero-native.dialog.openFile", js);
    }
}
