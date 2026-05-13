using System.Runtime.InteropServices;
using ZeroNative.Platform;

namespace ZeroNative;

/// <summary>
/// Factory that selects the appropriate system WebView platform for the current OS:
///   - Windows: Microsoft Edge WebView2
///   - macOS: WKWebView
///   - Linux: WebKit2GTK
/// </summary>
public static class WebViewPlatform
{
    public static IPlatform CreateForCurrentOs(AppInfo? appInfo = null, Surface? surface = null)
    {
        appInfo ??= new AppInfo();
        surface ??= new Surface();

        if (OperatingSystem.IsWindows())
        {
#if ZERO_NATIVE_HAS_WEBVIEW2
            return new Windows.WebView2Platform(appInfo, surface);
#else
            throw new PlatformNotSupportedException(
                "WebView2 backend requires building against net10.0-windows. Use the windows TFM.");
#endif
        }
        if (OperatingSystem.IsMacOS())
        {
            return new MacOS.WKWebViewPlatform(appInfo, surface);
        }
        if (OperatingSystem.IsLinux())
        {
            return new Linux.WebKitGtkPlatform(appInfo, surface);
        }
        throw new PlatformNotSupportedException(
            $"No system WebView backend available for OS: {RuntimeInformation.OSDescription}");
    }
}
