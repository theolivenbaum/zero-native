using Xunit;
using ZeroNative.Manifest;
using ZeroNative.Security;

namespace ZeroNative.Tests;

public class AppManifestJsonTests
{
    [Fact]
    public void Parse_MinimalManifest_PopulatesIdentityAndVersion()
    {
        const string json = """
        {
          "id": "com.example.app",
          "name": "Example",
          "version": "1.2.3"
        }
        """;

        var manifest = AppManifestJson.Parse(json);

        Assert.Equal("com.example.app", manifest.Identity.Id);
        Assert.Equal("Example", manifest.Identity.Name);
        Assert.Equal(1u, manifest.Version.Major);
        Assert.Equal(2u, manifest.Version.Minor);
        Assert.Equal(3u, manifest.Version.Patch);
    }

    [Fact]
    public void Parse_RichManifest_RoundTripsBridgeAndSecurity()
    {
        const string json = """
        {
          "id": "dev.zero_native",
          "name": "zero-native",
          "display_name": "Zero Native",
          "version": "0.1.0-rc.1+build.7",
          "permissions": ["window", "clipboard", "my.custom"],
          "capabilities": ["webview", "js_bridge", "native_module"],
          "bridge": {
            "commands": [
              { "name": "native.ping", "origins": ["zero://app", "zero://inline"] },
              { "name": "zero-native.window.list", "permissions": ["window"], "origins": ["zero://app"] }
            ]
          },
          "security": {
            "navigation": {
              "allowed_origins": ["zero://app", "http://127.0.0.1:5173"],
              "external_links": { "action": "open_system_browser", "allowed_urls": ["https://docs.example.com/*"] }
            }
          },
          "frontend": {
            "dist": "public",
            "entry": "shell.html",
            "spa_fallback": false,
            "dev": { "url": "http://localhost:5173", "command": ["npm", "run", "dev"], "ready_path": "/health", "timeout_ms": 5000 }
          },
          "windows": [ { "label": "main", "title": "Main", "width": 800, "height": 600, "restore_state": true, "restore_policy": "center" } ],
          "cef": { "dir": "third_party/cef-mac", "auto_install": true },
          "package": { "kind": "app", "web_engine": "chromium", "license": "MIT", "authors": ["alice"], "keywords": ["desktop"] },
          "updates": { "feed_url": "https://updates.example.com/feed", "check_on_start": true }
        }
        """;

        var manifest = AppManifestJson.Parse(json);

        Assert.Equal("Zero Native", manifest.Identity.DisplayName);
        Assert.Equal("rc.1", manifest.Version.Pre);
        Assert.Equal("build.7", manifest.Version.Build);

        Assert.Contains(manifest.Permissions, p => p.Kind == PermissionKind.Window);
        Assert.Contains(manifest.Permissions, p => p.Kind == PermissionKind.Custom && p.CustomName == "my.custom");
        Assert.Contains(manifest.Capabilities, c => c.Kind == CapabilityKind.JsBridge);

        Assert.Equal(2, manifest.Bridge.Commands.Count);
        Assert.Equal("native.ping", manifest.Bridge.Commands[0].Name);
        Assert.Single(manifest.Bridge.Commands[1].Permissions);

        Assert.Contains("http://127.0.0.1:5173", manifest.Security.Navigation.AllowedOrigins);
        Assert.Equal(ExternalLinkAction.OpenSystemBrowser, manifest.Security.Navigation.ExternalLinks.Action);
        Assert.Single(manifest.Security.Navigation.ExternalLinks.AllowedUrls);

        Assert.NotNull(manifest.Frontend);
        Assert.Equal("public", manifest.Frontend!.Dist);
        Assert.False(manifest.Frontend.SpaFallback);
        Assert.Equal("http://localhost:5173", manifest.Frontend.Dev!.Url);
        Assert.Equal(5000u, manifest.Frontend.Dev.TimeoutMs);

        Assert.Single(manifest.Windows);
        Assert.Equal(Platform.WindowRestorePolicy.CenterOnPrimary, manifest.Windows[0].RestorePolicy);

        Assert.Equal(ManifestWebEngine.Chromium, manifest.Package.WebEngine);
        Assert.Equal("third_party/cef-mac", manifest.Cef.Dir);
        Assert.True(manifest.Cef.AutoInstall);
        Assert.True(manifest.Updates.CheckOnStart);

        ManifestValidator.Validate(manifest);
    }

    [Fact]
    public void Parse_RejectsMissingRequiredFields()
    {
        Assert.Throws<ManifestParseException>(() =>
            AppManifestJson.Parse("""{"name":"x","version":"1.0.0"}"""));
        Assert.Throws<ManifestParseException>(() =>
            AppManifestJson.Parse("""{"id":"a.b","version":"1.0.0"}"""));
        Assert.Throws<ManifestParseException>(() =>
            AppManifestJson.Parse("""{"id":"a.b","name":"x"}"""));
    }

    [Fact]
    public void Parse_RejectsMalformedJson()
    {
        Assert.Throws<ManifestParseException>(() => AppManifestJson.Parse("not json"));
        Assert.Throws<ManifestParseException>(() => AppManifestJson.Parse(""));
    }

    [Fact]
    public void ParseVersion_ParsesEachComponent()
    {
        var v1 = AppManifestJson.ParseVersion("1");
        Assert.Equal((1u, 0u, 0u), (v1.Major, v1.Minor, v1.Patch));

        var v2 = AppManifestJson.ParseVersion("2.3");
        Assert.Equal((2u, 3u, 0u), (v2.Major, v2.Minor, v2.Patch));

        var v3 = AppManifestJson.ParseVersion("4.5.6-beta+meta");
        Assert.Equal((4u, 5u, 6u), (v3.Major, v3.Minor, v3.Patch));
        Assert.Equal("beta", v3.Pre);
        Assert.Equal("meta", v3.Build);
    }

    [Fact]
    public void Load_FromFile_ReadsAndParses()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """{"id":"com.example.x","name":"X","version":"0.1.0"}""");
            var manifest = AppManifestJson.Load(path);
            Assert.Equal("X", manifest.Identity.Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryLoad_ReturnsFalse_OnMissingFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "ZeroNativeManifest_" + Guid.NewGuid() + ".json");
        var ok = AppManifestJson.TryLoad(path, out var manifest, out var error);
        Assert.False(ok);
        Assert.Null(manifest);
        Assert.False(string.IsNullOrEmpty(error));
    }
}
