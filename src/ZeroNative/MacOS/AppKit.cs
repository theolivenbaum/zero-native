using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ZeroNative.MacOS;

/// <summary>
/// AppKit / WebKit constants and a few CoreFoundation helpers used by the
/// macOS platform backend.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class AppKit
{
    // NSWindow style mask bits.
    public const nuint NSWindowStyleMaskTitled = 1 << 0;
    public const nuint NSWindowStyleMaskClosable = 1 << 1;
    public const nuint NSWindowStyleMaskMiniaturizable = 1 << 2;
    public const nuint NSWindowStyleMaskResizable = 1 << 3;
    public const nuint NSBackingStoreBuffered = 2;

    // NSApplicationActivationPolicy
    public const nint NSApplicationActivationPolicyRegular = 0;
    public const nint NSApplicationActivationPolicyAccessory = 1;

    // NSAlert styles
    public const nuint NSAlertStyleWarning = 0;
    public const nuint NSAlertStyleInformational = 1;
    public const nuint NSAlertStyleCritical = 2;

    // NSAlert button responses begin at 1000.
    public const nint NSAlertFirstButtonReturn = 1000;
    public const nint NSAlertSecondButtonReturn = 1001;
    public const nint NSAlertThirdButtonReturn = 1002;

    // NSModalResponse
    public const nint NSModalResponseOK = 1;
    public const nint NSModalResponseCancel = 0;

    // WKUserScriptInjectionTime
    public const nint WKUserScriptInjectionTimeAtDocumentStart = 0;
    public const nint WKUserScriptInjectionTimeAtDocumentEnd = 1;

    // NSEventModifierFlags - kept around in case future code wants accelerators.
    public const nuint NSEventModifierFlagCommand = 1 << 20;

    /// <summary>Loads a framework so its classes become discoverable.</summary>
    public static void EnsureFrameworksLoaded()
    {
        // dlopen is sufficient — once a framework is mapped, objc_getClass can
        // find its classes. AppKit, WebKit, and Foundation are the three we
        // depend on; Foundation auto-loads with the runtime so explicit dlopen
        // covers the optional WebKit case.
        try
        {
            NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
            NativeLibrary.Load("/System/Library/Frameworks/WebKit.framework/WebKit");
        }
        catch { /* best-effort: missing framework will surface later as a class lookup failure */ }
    }
}
