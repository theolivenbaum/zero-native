using System.Text.Json;
using System.Text.Json.Serialization;
using ZeroNative.Security;

namespace ZeroNative.Manifest;

public class ManifestParseException : Exception
{
    public ManifestParseException(string message) : base(message) { }
    public ManifestParseException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Loads an <see cref="AppManifest"/> from a JSON document. The JSON schema mirrors the
/// Zig <c>app.zon</c> shape with snake_case keys, so a hand-written <c>app.json</c> file
/// can declare identity, version, icons, permissions, capabilities, bridge commands,
/// frontend layout, security policy, windows, CEF settings, and update channel.
/// </summary>
public static class AppManifestJson
{
    public static AppManifest Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ManifestParseException("Manifest JSON is empty");

        ManifestDto dto;
        try
        {
            dto = JsonSerializer.Deserialize(json, ManifestJsonContext.Default.ManifestDto)
                  ?? throw new ManifestParseException("Manifest JSON deserialized to null");
        }
        catch (JsonException ex)
        {
            throw new ManifestParseException("Invalid manifest JSON: " + ex.Message, ex);
        }

        return dto.ToManifest();
    }

    public static AppManifest Load(string filePath)
    {
        if (!File.Exists(filePath))
            throw new ManifestParseException($"Manifest not found at {filePath}");
        return Parse(File.ReadAllText(filePath));
    }

    public static bool TryLoad(string filePath, out AppManifest? manifest, out string? error)
    {
        try
        {
            manifest = Load(filePath);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is ManifestParseException or ManifestValidationException)
        {
            manifest = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Parses a semver-ish version like <c>1.2.3</c>, <c>1.2.3-rc.1</c>, or <c>1.2.3+build.7</c>.
    /// </summary>
    public static AppVersion ParseVersion(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ManifestParseException("Version is empty");

        var body = raw;
        string? build = null;
        var plusIdx = body.IndexOf('+');
        if (plusIdx >= 0)
        {
            build = body[(plusIdx + 1)..];
            body = body[..plusIdx];
        }

        string? pre = null;
        var dashIdx = body.IndexOf('-');
        if (dashIdx >= 0)
        {
            pre = body[(dashIdx + 1)..];
            body = body[..dashIdx];
        }

        var parts = body.Split('.');
        if (parts.Length < 1 || parts.Length > 3)
            throw new ManifestParseException($"Invalid version '{raw}'");

        uint Parse(int idx) =>
            idx < parts.Length && uint.TryParse(parts[idx], out var v)
                ? v
                : throw new ManifestParseException($"Invalid version component '{parts[idx]}' in '{raw}'");

        return new AppVersion(Parse(0), parts.Length > 1 ? Parse(1) : 0, parts.Length > 2 ? Parse(2) : 0, pre, build);
    }

    internal sealed class ManifestDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? DisplayName { get; set; }
        public string? Organization { get; set; }
        public string? Homepage { get; set; }
        public string? Version { get; set; }
        public List<IconDto>? Icons { get; set; }
        public List<string>? Permissions { get; set; }
        public List<string>? Capabilities { get; set; }
        public BridgeDto? Bridge { get; set; }
        public FrontendDto? Frontend { get; set; }
        public SecurityDto? Security { get; set; }
        public List<PlatformDto>? Platforms { get; set; }
        public List<WindowDto>? Windows { get; set; }
        public CefDto? Cef { get; set; }
        public PackageDto? Package { get; set; }
        public UpdateDto? Updates { get; set; }
        public string? WebEngine { get; set; }

        public AppManifest ToManifest()
        {
            if (string.IsNullOrWhiteSpace(Id)) throw new ManifestParseException("Manifest.id is required");
            if (string.IsNullOrWhiteSpace(Name)) throw new ManifestParseException("Manifest.name is required");
            if (string.IsNullOrWhiteSpace(Version)) throw new ManifestParseException("Manifest.version is required");

            var package = Package?.ToPackage() ?? new PackageMetadata();
            if (WebEngine is { Length: > 0 } we)
            {
                package = package with { WebEngine = ParseWebEngine(we) };
            }

            return new AppManifest
            {
                Identity = new AppIdentity(Id!, Name!, DisplayName, Organization, Homepage),
                Version = ParseVersion(Version!),
                Icons = Icons?.Select(i => i.ToIcon()).ToList() ?? (IReadOnlyList<Icon>)Array.Empty<Icon>(),
                Permissions = Permissions?.Select(ParsePermission).ToList() ?? (IReadOnlyList<Permission>)Array.Empty<Permission>(),
                Capabilities = Capabilities?.Select(ParseCapability).ToList() ?? (IReadOnlyList<Capability>)Array.Empty<Capability>(),
                Bridge = Bridge?.ToBridgeConfig() ?? new BridgeConfig(),
                Frontend = Frontend?.ToFrontendConfig(),
                Security = Security?.ToSecurityConfig() ?? new SecurityConfig(),
                Platforms = Platforms?.Select(p => p.ToPlatformSettings()).ToList() ?? (IReadOnlyList<PlatformSettings>)Array.Empty<PlatformSettings>(),
                Windows = Windows?.Select(w => w.ToWindow()).ToList() ?? (IReadOnlyList<ManifestWindow>)Array.Empty<ManifestWindow>(),
                Cef = Cef?.ToCefConfig() ?? new CefConfig(),
                Package = package,
                Updates = Updates?.ToUpdateConfig() ?? new UpdateConfig(),
            };
        }
    }

    internal sealed class IconDto
    {
        public string? Asset { get; set; }
        public uint Size { get; set; }
        public uint Scale { get; set; } = 1;
        public string? Purpose { get; set; }

        public Icon ToIcon()
        {
            if (string.IsNullOrEmpty(Asset))
                throw new ManifestParseException("Icon.asset is required");
            return new Icon(Asset!, Size, Scale == 0 ? 1 : Scale, ParseIconPurpose(Purpose));
        }
    }

    internal sealed class BridgeDto
    {
        public List<BridgeCommandDto>? Commands { get; set; }
        public BridgeConfig ToBridgeConfig()
            => new(Commands?.Select(c => c.ToCommand()).ToList());
    }

    internal sealed class BridgeCommandDto
    {
        public string? Name { get; set; }
        public List<string>? Permissions { get; set; }
        public List<string>? Origins { get; set; }

        public BridgeCommandManifest ToCommand()
        {
            if (string.IsNullOrEmpty(Name))
                throw new ManifestParseException("Bridge command.name is required");
            return new BridgeCommandManifest(
                Name!,
                Permissions?.Select(ParsePermission).ToList(),
                Origins?.ToList());
        }
    }

    internal sealed class FrontendDto
    {
        public string? Dist { get; set; }
        public string? Entry { get; set; }
        public bool? SpaFallback { get; set; }
        public FrontendDevDto? Dev { get; set; }

        public FrontendConfig ToFrontendConfig()
            => new(
                Dist ?? "dist",
                Entry ?? "index.html",
                SpaFallback ?? true,
                Dev?.ToDevConfig());
    }

    internal sealed class FrontendDevDto
    {
        public string? Url { get; set; }
        public List<string>? Command { get; set; }
        public string? ReadyPath { get; set; }
        public uint? TimeoutMs { get; set; }

        public FrontendDevConfig ToDevConfig()
        {
            if (string.IsNullOrEmpty(Url))
                throw new ManifestParseException("frontend.dev.url is required");
            return new FrontendDevConfig(Url!, Command?.ToList(), ReadyPath ?? "/", TimeoutMs ?? 30_000);
        }
    }

    internal sealed class SecurityDto
    {
        public NavigationDto? Navigation { get; set; }
        public SecurityConfig ToSecurityConfig()
            => new(Navigation?.ToPolicy());
    }

    internal sealed class NavigationDto
    {
        public List<string>? AllowedOrigins { get; set; }
        public ExternalLinksDto? ExternalLinks { get; set; }

        public NavigationPolicy ToPolicy()
            => new(AllowedOrigins?.ToList(), ExternalLinks?.ToPolicy());
    }

    internal sealed class ExternalLinksDto
    {
        public string? Action { get; set; }
        public List<string>? AllowedUrls { get; set; }

        public ExternalLinkPolicy ToPolicy()
            => new(ParseExternalLinkAction(Action), AllowedUrls?.ToList());
    }

    internal sealed class PlatformDto
    {
        public string? Platform { get; set; }
        public string? Id { get; set; }
        public string? MinOsVersion { get; set; }
        public List<string>? Permissions { get; set; }
        public string? Category { get; set; }
        public string? Entitlements { get; set; }
        public string? Profile { get; set; }

        public PlatformSettings ToPlatformSettings()
            => new(
                ParsePlatform(Platform),
                Id,
                MinOsVersion,
                Permissions?.Select(ParsePermission).ToList(),
                Category,
                Entitlements,
                Profile);
    }

    internal sealed class WindowDto
    {
        public string? Label { get; set; }
        public string? Title { get; set; }
        public float? Width { get; set; }
        public float? Height { get; set; }
        public float? X { get; set; }
        public float? Y { get; set; }
        public bool? Resizable { get; set; }
        public bool? RestoreState { get; set; }
        public string? RestorePolicy { get; set; }

        public ManifestWindow ToWindow()
            => new(
                Label ?? "main",
                Title,
                Width ?? 720,
                Height ?? 480,
                X,
                Y,
                Resizable ?? true,
                RestoreState ?? true,
                ParseRestorePolicy(RestorePolicy));
    }

    internal sealed class CefDto
    {
        public string? Dir { get; set; }
        public bool? AutoInstall { get; set; }

        public CefConfig ToCefConfig()
            => new(Dir ?? "third_party/cef", AutoInstall ?? false);
    }

    internal sealed class PackageDto
    {
        public string? Kind { get; set; }
        public string? WebEngine { get; set; }
        public string? License { get; set; }
        public List<string>? Authors { get; set; }
        public string? Repository { get; set; }
        public List<string>? Keywords { get; set; }

        public PackageMetadata ToPackage()
            => new(
                ParsePackageKind(Kind),
                ParseWebEngine(WebEngine),
                License,
                Authors?.ToList(),
                Repository,
                Keywords?.ToList());
    }

    internal sealed class UpdateDto
    {
        public string? FeedUrl { get; set; }
        public string? PublicKey { get; set; }
        public bool? CheckOnStart { get; set; }

        public UpdateConfig ToUpdateConfig()
            => new(FeedUrl, PublicKey, CheckOnStart ?? false);
    }

    private static Permission ParsePermission(string raw)
    {
        var v = raw.Trim().ToLowerInvariant();
        return v switch
        {
            "network" => Permission.Network(),
            "filesystem" => Permission.Filesystem(),
            "camera" => Permission.Camera(),
            "microphone" => Permission.Microphone(),
            "location" => Permission.Location(),
            "notifications" => Permission.Notifications(),
            "clipboard" => Permission.Clipboard(),
            "window" => Permission.Window(),
            _ => Permission.Custom(raw),
        };
    }

    private static Capability ParseCapability(string raw)
    {
        var v = raw.Trim().ToLowerInvariant();
        return v switch
        {
            "native_module" or "nativemodule" => Capability.NativeModule(),
            "webview" or "web_view" => Capability.WebView(),
            "js_bridge" or "jsbridge" => Capability.JsBridge(),
            "filesystem" => Capability.Filesystem(),
            "network" => Capability.Network(),
            "clipboard" => Capability.Clipboard(),
            _ => Capability.Custom(raw),
        };
    }

    private static ManifestPlatform ParsePlatform(string? raw) =>
        (raw?.Trim().ToLowerInvariant()) switch
        {
            "macos" or "osx" or "darwin" => ManifestPlatform.MacOS,
            "windows" or "win" => ManifestPlatform.Windows,
            "linux" => ManifestPlatform.Linux,
            "ios" => ManifestPlatform.IOS,
            "android" => ManifestPlatform.Android,
            "web" => ManifestPlatform.Web,
            _ => ManifestPlatform.Unknown,
        };

    private static ManifestWebEngine ParseWebEngine(string? raw) =>
        (raw?.Trim().ToLowerInvariant()) switch
        {
            "chromium" or "cef" => ManifestWebEngine.Chromium,
            _ => ManifestWebEngine.System,
        };

    private static PackageKind ParsePackageKind(string? raw) =>
        (raw?.Trim().ToLowerInvariant()) switch
        {
            "cli" => PackageKind.Cli,
            "library" or "lib" => PackageKind.Library,
            "plugin" => PackageKind.Plugin,
            "test_fixture" or "testfixture" => PackageKind.TestFixture,
            _ => PackageKind.App,
        };

    private static IconPurpose? ParseIconPurpose(string? raw) =>
        (raw?.Trim().ToLowerInvariant()) switch
        {
            null or "" => null,
            "any" => IconPurpose.Any,
            "maskable" => IconPurpose.Maskable,
            "monochrome" => IconPurpose.Monochrome,
            _ => throw new ManifestParseException($"Unknown icon purpose '{raw}'"),
        };

    private static Platform.WindowRestorePolicy ParseRestorePolicy(string? raw) =>
        (raw?.Trim().ToLowerInvariant()) switch
        {
            "center" or "center_on_primary" or "centeronprimary" => Platform.WindowRestorePolicy.CenterOnPrimary,
            _ => Platform.WindowRestorePolicy.ClampToVisibleScreen,
        };

    private static ExternalLinkAction ParseExternalLinkAction(string? raw) =>
        (raw?.Trim().ToLowerInvariant()) switch
        {
            "open_system_browser" or "opensystembrowser" or "system_browser" or "browser" => ExternalLinkAction.OpenSystemBrowser,
            _ => ExternalLinkAction.Deny,
        };
}

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppManifestJson.ManifestDto))]
internal partial class ManifestJsonContext : JsonSerializerContext { }
