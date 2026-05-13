using ZeroNative.Primitives;

namespace ZeroNative.Platform;

public enum WebEngine
{
    System,
    Chromium,
}

public enum WebViewSourceKind
{
    Html,
    Url,
    Assets,
}

public sealed record WebViewAssetSource(
    string RootPath,
    string Entry = "index.html",
    string Origin = "zero://app",
    bool SpaFallback = true);

public sealed record WebViewSource
{
    public required WebViewSourceKind Kind { get; init; }
    public required string Body { get; init; }
    public WebViewAssetSource? AssetOptions { get; init; }

    public static WebViewSource Html(string html) => new() { Kind = WebViewSourceKind.Html, Body = html };

    public static WebViewSource Url(string url) => new() { Kind = WebViewSourceKind.Url, Body = url };

    public static WebViewSource Assets(WebViewAssetSource options)
        => new() { Kind = WebViewSourceKind.Assets, Body = options.Origin, AssetOptions = options };
}

public static class PlatformLimits
{
    public const int MaxWindows = 16;
    public const int MaxWindowLabelBytes = 64;
    public const int MaxWindowTitleBytes = 128;
    public const int MaxWindowSourceBytes = 1024 * 1024;
    public const int MaxDialogPathBytes = 4096;
    public const int MaxDialogPathsBytes = 16 * 4096;
}

public enum WindowRestorePolicy
{
    ClampToVisibleScreen,
    CenterOnPrimary,
}

public sealed record WindowOptions
{
    public ulong Id { get; init; } = 1;
    public string Label { get; init; } = "main";
    public string Title { get; init; } = "";
    public RectF DefaultFrame { get; init; } = new(0, 0, 720, 480);
    public bool Resizable { get; init; } = true;
    public bool RestoreState { get; init; } = true;
    public WindowRestorePolicy RestorePolicy { get; init; } = WindowRestorePolicy.ClampToVisibleScreen;

    public string ResolvedTitle(string appName) => Title.Length > 0 ? Title : appName;
}

public sealed record WindowState
{
    public ulong Id { get; init; } = 1;
    public string Label { get; init; } = "main";
    public string Title { get; init; } = "";
    public RectF Frame { get; init; } = new(0, 0, 720, 480);
    public float ScaleFactor { get; init; } = 1;
    public bool Open { get; init; } = true;
    public bool Focused { get; init; } = true;
    public bool Maximized { get; init; } = false;
    public bool Fullscreen { get; init; } = false;
}

public sealed record WindowInfo
{
    public ulong Id { get; init; } = 1;
    public string Label { get; init; } = "main";
    public string Title { get; init; } = "";
    public RectF Frame { get; init; } = new(0, 0, 720, 480);
    public float ScaleFactor { get; init; } = 1;
    public bool Open { get; init; } = true;
    public bool Focused { get; init; } = false;

    public WindowState ToState() => new()
    {
        Id = Id,
        Label = Label,
        Title = Title,
        Frame = Frame,
        ScaleFactor = ScaleFactor,
        Open = Open,
        Focused = Focused,
    };
}

public sealed record WindowCreateOptions
{
    public ulong Id { get; init; } = 0;
    public string Label { get; init; } = "";
    public string Title { get; init; } = "";
    public RectF DefaultFrame { get; init; } = new(0, 0, 720, 480);
    public bool Resizable { get; init; } = true;
    public bool RestoreState { get; init; } = true;
    public WindowRestorePolicy RestorePolicy { get; init; } = WindowRestorePolicy.ClampToVisibleScreen;
    public WebViewSource? Source { get; init; }

    public WindowOptions ToWindowOptions(ulong id, string label) => new()
    {
        Id = id,
        Label = label,
        Title = Title,
        DefaultFrame = DefaultFrame,
        Resizable = Resizable,
        RestoreState = RestoreState,
        RestorePolicy = RestorePolicy,
    };
}

public sealed record AppInfo
{
    public string AppName { get; init; } = "zero-native";
    public string WindowTitle { get; init; } = "";
    public string BundleId { get; init; } = "dev.zero_native.app";
    public string IconPath { get; init; } = "";
    public WindowOptions MainWindow { get; init; } = new();
    public IReadOnlyList<WindowOptions> Windows { get; init; } = Array.Empty<WindowOptions>();

    public string ResolvedWindowTitle()
        => WindowTitle.Length > 0 ? WindowTitle : MainWindow.ResolvedTitle(AppName);

    public WindowOptions ResolvedMainWindow()
    {
        var window = MainWindow;
        if (window.Title.Length == 0)
            window = window with { Title = ResolvedWindowTitle() };
        return window;
    }

    public int StartupWindowCount() => Windows.Count > 0 ? Windows.Count : 1;

    public WindowOptions ResolvedStartupWindow(int index)
    {
        var window = Windows.Count > 0 ? Windows[index] : MainWindow;
        if (window.Id == 0 || (Windows.Count > 0 && index > 0 && window.Id == 1))
        {
            window = window with { Id = (ulong)(index + 1) };
        }
        if (window.Label.Length == 0)
            window = window with { Label = index == 0 ? "main" : "window" };
        if (window.Title.Length == 0)
            window = window with { Title = ResolvedWindowTitle() };
        return window;
    }
}

public sealed record Surface
{
    public ulong Id { get; init; } = 1;
    public SizeF Size { get; init; } = new(640, 360);
    public float ScaleFactor { get; init; } = 1;
    public IntPtr NativeHandle { get; init; } = IntPtr.Zero;
}

public sealed record BridgeMessage(string Bytes, string Origin = "", ulong WindowId = 1);

public sealed record FileFilter(string Name, IReadOnlyList<string> Extensions);

public sealed record OpenDialogOptions(
    string Title = "",
    string DefaultPath = "",
    IReadOnlyList<FileFilter>? Filters = null,
    bool AllowDirectories = false,
    bool AllowMultiple = false)
{
    public IReadOnlyList<FileFilter> Filters { get; init; } = Filters ?? Array.Empty<FileFilter>();
}

public sealed record OpenDialogResult(IReadOnlyList<string> Paths);

public sealed record SaveDialogOptions(
    string Title = "",
    string DefaultPath = "",
    string DefaultName = "",
    IReadOnlyList<FileFilter>? Filters = null)
{
    public IReadOnlyList<FileFilter> Filters { get; init; } = Filters ?? Array.Empty<FileFilter>();
}

public enum MessageDialogStyle
{
    Info = 0,
    Warning = 1,
    Critical = 2,
}

public enum MessageDialogResult
{
    Primary = 0,
    Secondary = 1,
    Tertiary = 2,
}

public sealed record MessageDialogOptions(
    MessageDialogStyle Style = MessageDialogStyle.Info,
    string Title = "",
    string Message = "",
    string InformativeText = "",
    string PrimaryButton = "OK",
    string SecondaryButton = "",
    string TertiaryButton = "");

public sealed record TrayMenuItem(uint Id = 0, string Label = "", bool Separator = false, bool Enabled = true);

public sealed record TrayOptions(
    string IconPath = "",
    string Tooltip = "",
    IReadOnlyList<TrayMenuItem>? Items = null)
{
    public IReadOnlyList<TrayMenuItem> Items { get; init; } = Items ?? Array.Empty<TrayMenuItem>();
}

public abstract record PlatformEvent
{
    public abstract string Name { get; }

    public sealed record AppStart : PlatformEvent { public override string Name => "app_start"; }
    public sealed record FrameRequested : PlatformEvent { public override string Name => "frame_requested"; }
    public sealed record AppShutdown : PlatformEvent { public override string Name => "app_shutdown"; }
    public sealed record SurfaceResized(Surface Surface) : PlatformEvent { public override string Name => "surface_resized"; }
    public sealed record WindowFrameChanged(WindowState State) : PlatformEvent { public override string Name => "window_frame_changed"; }
    public sealed record WindowFocused(ulong WindowId) : PlatformEvent { public override string Name => "window_focused"; }
    public sealed record BridgeReceived(BridgeMessage Message) : PlatformEvent { public override string Name => "bridge_message"; }
    public sealed record TrayAction(uint ItemId) : PlatformEvent { public override string Name => "tray_action"; }
}
