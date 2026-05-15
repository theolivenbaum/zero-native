using ZeroNative.Security;

namespace ZeroNative.Platform;

public class PlatformException : Exception
{
    public PlatformException(string message) : base(message) { }
    public PlatformException(string message, Exception inner) : base(message, inner) { }
}

public class UnsupportedServiceException : PlatformException
{
    public UnsupportedServiceException(string message = "Service not supported on this platform") : base(message) { }
}

public class WindowNotFoundException : PlatformException
{
    public WindowNotFoundException(string message = "Window not found") : base(message) { }
}

public class DuplicateWindowException : PlatformException
{
    public DuplicateWindowException(string message) : base(message) { }
}

public class WindowLimitReachedException : PlatformException
{
    public WindowLimitReachedException() : base("Maximum window count reached") { }
}

public class MissingWindowSourceException : PlatformException
{
    public MissingWindowSourceException() : base("Window source is missing") { }
}

public class WindowSourceTooLargeException : PlatformException
{
    public WindowSourceTooLargeException() : base("Window source is too large") { }
}

public class InvalidWindowOptionsException : PlatformException
{
    public InvalidWindowOptionsException() : base("Window options are invalid") { }
}

/// <summary>
/// Services callback bag for platform implementations.
/// Implementations override the methods their backend supports;
/// callers should be ready to receive UnsupportedServiceException.
/// </summary>
public interface IPlatformServices
{
    string ReadClipboard() => throw new UnsupportedServiceException();
    void WriteClipboard(string text) => throw new UnsupportedServiceException();

    /// <summary>Reads file URLs from the system pasteboard (e.g. a Finder/Explorer drag).</summary>
    IReadOnlyList<string> ReadClipboardFiles() => throw new UnsupportedServiceException();

    /// <summary>Writes one or more file URLs to the system pasteboard.</summary>
    void WriteClipboardFiles(IReadOnlyList<string> paths) => throw new UnsupportedServiceException();

    /// <summary>Reads raw image bytes (PNG-encoded when possible) from the system pasteboard.</summary>
    byte[] ReadClipboardImage() => throw new UnsupportedServiceException();

    /// <summary>Writes raw image bytes to the system pasteboard. The data should be a complete image (PNG, TIFF, etc).</summary>
    void WriteClipboardImage(ReadOnlySpan<byte> bytes) => throw new UnsupportedServiceException();

    void LoadWebView(WebViewSource source) => LoadWindowWebView(1, source);

    void LoadWindowWebView(ulong windowId, WebViewSource source)
        => throw new UnsupportedServiceException();

    void CompleteBridge(string response) => CompleteWindowBridge(1, response);

    void CompleteWindowBridge(ulong windowId, string response)
        => throw new UnsupportedServiceException();

    WindowInfo CreateWindow(WindowOptions options) => throw new UnsupportedServiceException();
    void FocusWindow(ulong windowId) => throw new UnsupportedServiceException();
    void CloseWindow(ulong windowId) => throw new UnsupportedServiceException();

    /// <summary>
    /// Moves and resizes an existing window. Platforms that don't support
    /// programmatic geometry changes should throw <see cref="UnsupportedServiceException"/>;
    /// the runtime swallows that to keep callers source-compatible.
    /// </summary>
    void SetWindowFrame(ulong windowId, Primitives.RectF frame) => throw new UnsupportedServiceException();

    OpenDialogResult ShowOpenDialog(OpenDialogOptions options) => throw new UnsupportedServiceException();
    string? ShowSaveDialog(SaveDialogOptions options) => throw new UnsupportedServiceException();
    MessageDialogResult ShowMessageDialog(MessageDialogOptions options) => throw new UnsupportedServiceException();

    void CreateTray(TrayOptions options) => throw new UnsupportedServiceException();
    void UpdateTrayMenu(IReadOnlyList<TrayMenuItem> items) => throw new UnsupportedServiceException();
    void RemoveTray() => throw new UnsupportedServiceException();

    void ConfigureSecurityPolicy(SecurityPolicy policy) { }

    void EmitWindowEvent(ulong windowId, string eventName, string detailJson)
        => throw new UnsupportedServiceException();
}

/// <summary>
/// A complete platform integration: identity, services, and an event loop.
/// </summary>
public interface IPlatform
{
    string Name { get; }
    Surface Surface { get; }
    AppInfo AppInfo { get; }
    IPlatformServices Services { get; }

    /// <summary>
    /// Runs the platform event loop, invoking the handler for each event.
    /// The handler returns false to stop the loop.
    /// </summary>
    void Run(Action<PlatformEvent> handler);
}
