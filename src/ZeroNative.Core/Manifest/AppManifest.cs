using System.Text.RegularExpressions;
using ZeroNative.Security;

namespace ZeroNative.Manifest;

public enum ManifestPlatform { MacOS, Windows, Linux, IOS, Android, Web, Unknown }

public enum PackageKind { App, Cli, Library, Plugin, TestFixture }

public enum ManifestWebEngine { System, Chromium }

public enum IconPurpose { Any, Maskable, Monochrome }

public enum PermissionKind
{
    Network, Filesystem, Camera, Microphone, Location, Notifications, Clipboard, Window, Custom
}

public enum CapabilityKind
{
    NativeModule, WebView, JsBridge, Filesystem, Network, Clipboard, Custom
}

public sealed record Permission(PermissionKind Kind, string CustomName = "")
{
    public static Permission Network() => new(PermissionKind.Network);
    public static Permission Filesystem() => new(PermissionKind.Filesystem);
    public static Permission Camera() => new(PermissionKind.Camera);
    public static Permission Microphone() => new(PermissionKind.Microphone);
    public static Permission Location() => new(PermissionKind.Location);
    public static Permission Notifications() => new(PermissionKind.Notifications);
    public static Permission Clipboard() => new(PermissionKind.Clipboard);
    public static Permission Window() => new(PermissionKind.Window);
    public static Permission Custom(string name) => new(PermissionKind.Custom, name);

    public string AsString() => Kind == PermissionKind.Custom ? CustomName : Kind.ToString().ToLowerInvariant();
}

public sealed record Capability(CapabilityKind Kind, string CustomName = "")
{
    public static Capability NativeModule() => new(CapabilityKind.NativeModule);
    public static Capability WebView() => new(CapabilityKind.WebView);
    public static Capability JsBridge() => new(CapabilityKind.JsBridge);
    public static Capability Filesystem() => new(CapabilityKind.Filesystem);
    public static Capability Network() => new(CapabilityKind.Network);
    public static Capability Clipboard() => new(CapabilityKind.Clipboard);
    public static Capability Custom(string name) => new(CapabilityKind.Custom, name);
}

public sealed record AppIdentity(
    string Id,
    string Name,
    string? DisplayName = null,
    string? Organization = null,
    string? Homepage = null);

public sealed record AppVersion(
    uint Major,
    uint Minor,
    uint Patch,
    string? Pre = null,
    string? Build = null)
{
    public override string ToString()
    {
        var s = $"{Major}.{Minor}.{Patch}";
        if (!string.IsNullOrEmpty(Pre)) s += "-" + Pre;
        if (!string.IsNullOrEmpty(Build)) s += "+" + Build;
        return s;
    }
}

public sealed record Icon(string Asset, uint Size, uint Scale = 1, IconPurpose? Purpose = null);

public sealed record PlatformSettings(
    ManifestPlatform Platform,
    string? IdOverride = null,
    string? MinOsVersion = null,
    IReadOnlyList<Permission>? Permissions = null,
    string? Category = null,
    string? Entitlements = null,
    string? Profile = null)
{
    public IReadOnlyList<Permission> Permissions { get; init; } = Permissions ?? Array.Empty<Permission>();
}

public sealed record BridgeCommandManifest(
    string Name,
    IReadOnlyList<Permission>? Permissions = null,
    IReadOnlyList<string>? Origins = null)
{
    public IReadOnlyList<Permission> Permissions { get; init; } = Permissions ?? Array.Empty<Permission>();
    public IReadOnlyList<string> Origins { get; init; } = Origins ?? Array.Empty<string>();
}

public sealed record BridgeConfig(IReadOnlyList<BridgeCommandManifest>? Commands = null)
{
    public IReadOnlyList<BridgeCommandManifest> Commands { get; init; } = Commands ?? Array.Empty<BridgeCommandManifest>();
}

public sealed record SecurityConfig(NavigationPolicy? Navigation = null)
{
    public NavigationPolicy Navigation { get; init; } = Navigation ?? new NavigationPolicy();
}

public sealed record FrontendDevConfig(
    string Url,
    IReadOnlyList<string>? Command = null,
    string ReadyPath = "/",
    uint TimeoutMs = 30_000)
{
    public IReadOnlyList<string> Command { get; init; } = Command ?? Array.Empty<string>();
}

public sealed record FrontendConfig(
    string Dist = "dist",
    string Entry = "index.html",
    bool SpaFallback = true,
    FrontendDevConfig? Dev = null);

public sealed record ManifestWindow(
    string Label = "main",
    string? Title = null,
    float Width = 720,
    float Height = 480,
    float? X = null,
    float? Y = null,
    bool Resizable = true,
    bool RestoreState = true,
    Platform.WindowRestorePolicy RestorePolicy = Platform.WindowRestorePolicy.ClampToVisibleScreen);

public sealed record CefConfig(string Dir = "third_party/cef", bool AutoInstall = false);

public sealed record PackageMetadata(
    PackageKind Kind = PackageKind.App,
    ManifestWebEngine WebEngine = ManifestWebEngine.System,
    string? License = null,
    IReadOnlyList<string>? Authors = null,
    string? Repository = null,
    IReadOnlyList<string>? Keywords = null)
{
    public IReadOnlyList<string> Authors { get; init; } = Authors ?? Array.Empty<string>();
    public IReadOnlyList<string> Keywords { get; init; } = Keywords ?? Array.Empty<string>();
}

public sealed record UpdateConfig(
    string? FeedUrl = null,
    string? PublicKey = null,
    bool CheckOnStart = false);

public sealed record AppManifest
{
    public required AppIdentity Identity { get; init; }
    public required AppVersion Version { get; init; }
    public IReadOnlyList<Icon> Icons { get; init; } = Array.Empty<Icon>();
    public IReadOnlyList<Permission> Permissions { get; init; } = Array.Empty<Permission>();
    public IReadOnlyList<Capability> Capabilities { get; init; } = Array.Empty<Capability>();
    public BridgeConfig Bridge { get; init; } = new();
    public FrontendConfig? Frontend { get; init; }
    public SecurityConfig Security { get; init; } = new();
    public IReadOnlyList<PlatformSettings> Platforms { get; init; } = Array.Empty<PlatformSettings>();
    public IReadOnlyList<ManifestWindow> Windows { get; init; } = Array.Empty<ManifestWindow>();
    public CefConfig Cef { get; init; } = new();
    public PackageMetadata Package { get; init; } = new();
    public UpdateConfig Updates { get; init; } = new();
}

public class ManifestValidationException : Exception
{
    public ManifestValidationException(string message) : base(message) { }
}

public static class ManifestValidator
{
    private static readonly Regex IdSegmentRegex = new("^[a-z0-9][a-z0-9_-]*$", RegexOptions.Compiled);

    public static void Validate(AppManifest manifest)
    {
        ValidateIdentity(manifest.Identity);
        ValidateIcons(manifest.Icons);
        ValidateWindows(manifest.Windows);
    }

    public static void ValidateIdentity(AppIdentity identity)
    {
        if (string.IsNullOrEmpty(identity.Id)) throw new ManifestValidationException("Identity.Id is required");
        var segments = identity.Id.Split('.');
        if (segments.Length < 2) throw new ManifestValidationException("Identity.Id must be reverse-DNS");
        foreach (var segment in segments)
        {
            if (!IdSegmentRegex.IsMatch(segment))
                throw new ManifestValidationException($"Invalid id segment: {segment}");
        }
        if (string.IsNullOrEmpty(identity.Name)) throw new ManifestValidationException("Identity.Name is required");
        if (identity.Homepage is { Length: > 0 } home && !(home.StartsWith("http://") || home.StartsWith("https://")))
            throw new ManifestValidationException("Identity.Homepage must be http(s)");
    }

    public static void ValidateIcons(IReadOnlyList<Icon> icons)
    {
        var seen = new HashSet<(uint, uint, IconPurpose?)>();
        foreach (var icon in icons)
        {
            if (string.IsNullOrEmpty(icon.Asset))
                throw new ManifestValidationException("Icon.Asset is required");
            if (icon.Size == 0 || icon.Scale == 0)
                throw new ManifestValidationException("Icon size/scale must be > 0");
            if (!seen.Add((icon.Size, icon.Scale, icon.Purpose)))
                throw new ManifestValidationException("Duplicate icon");
        }
    }

    public static void ValidateWindows(IReadOnlyList<ManifestWindow> windows)
    {
        var labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var window in windows)
        {
            if (string.IsNullOrEmpty(window.Label))
                throw new ManifestValidationException("Window.Label is required");
            if (window.Width <= 0 || window.Height <= 0)
                throw new ManifestValidationException("Window dimensions must be > 0");
            if (!labels.Add(window.Label))
                throw new ManifestValidationException($"Duplicate window label: {window.Label}");
        }
    }
}
