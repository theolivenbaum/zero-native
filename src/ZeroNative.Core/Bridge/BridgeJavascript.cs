namespace ZeroNative.Bridge;

/// <summary>
/// Shared JavaScript snippet that each platform backend injects at document creation.
/// It exposes <c>window.zero.invoke(command, payload?)</c> returning a Promise, and
/// routes responses back via <c>window.__zero_native_bridge_response(response)</c>
/// (which the platform host evaluates from .NET land after dispatch completes).
/// </summary>
public static class BridgeJavascript
{
    /// <summary>The transport channels the host can wire up.</summary>
    public enum Channel
    {
        /// <summary>Uses <c>window.chrome.webview.postMessage</c> (WebView2).</summary>
        ChromeWebView,
        /// <summary>Uses <c>window.webkit.messageHandlers.<see cref="HandlerName"/>.postMessage</c> (WKWebView / WebKitGTK user-content manager).</summary>
        WebKitMessageHandler,
        /// <summary>Uses a global <c>window.__zero_native_send</c> function exposed by the host (CEF V8 binding).</summary>
        GlobalFunction,
    }

    /// <summary>The script-message handler name registered with WKWebView / WebKitGTK.</summary>
    public const string HandlerName = "zeroNativeBridge";

    /// <summary>Produces the JS shim. Inject this at document creation in every page.</summary>
    public static string Build(Channel channel) => $@"
(function () {{
  if (window.zero && window.zero.__installed) return;
  var pending = Object.create(null);
  var seq = 0;

  function post(message) {{
    var payload = JSON.stringify(message);
    {SendSnippet(channel)}
  }}

  function invoke(command, payload) {{
    return new Promise(function (resolve, reject) {{
      var id = 'znv-' + (++seq) + '-' + Date.now().toString(36);
      pending[id] = {{ resolve: resolve, reject: reject }};
      post({{ id: id, command: command, payload: payload === undefined ? null : payload }});
    }});
  }}

  window.__zero_native_bridge_response = function (response) {{
    if (!response || typeof response !== 'object') return;
    var entry = pending[response.id];
    if (!entry) return;
    delete pending[response.id];
    if (response.ok) entry.resolve(response.result);
    else entry.reject(Object.assign(new Error(response.error && response.error.message || 'Bridge error'), {{ code: response.error && response.error.code }}));
  }};

  window.zero = Object.freeze({{
    invoke: invoke,
    __installed: true,
    window: {{
      list: function () {{ return invoke('zero-native.window.list', null); }},
      create: function (opts) {{ return invoke('zero-native.window.create', opts); }},
      focus: function (selector) {{ return invoke('zero-native.window.focus', selector); }},
      close: function (selector) {{ return invoke('zero-native.window.close', selector); }}
    }},
    dialog: {{
      openFile: function (opts) {{ return invoke('zero-native.dialog.openFile', opts); }},
      saveFile: function (opts) {{ return invoke('zero-native.dialog.saveFile', opts); }},
      showMessage: function (opts) {{ return invoke('zero-native.dialog.showMessage', opts); }}
    }}
  }});
}})();
";

    private static string SendSnippet(Channel channel) => channel switch
    {
        Channel.ChromeWebView => "window.chrome.webview.postMessage(payload);",
        Channel.WebKitMessageHandler => $"window.webkit.messageHandlers.{HandlerName}.postMessage(payload);",
        Channel.GlobalFunction => "window.__zero_native_send(payload);",
        _ => throw new ArgumentOutOfRangeException(nameof(channel)),
    };
}
