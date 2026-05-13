using System.Text;
using Xunit;
using ZeroNative.Assets;

namespace ZeroNative.Tests;

public class AssetServerTests : IDisposable
{
    private readonly string _root;

    public AssetServerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ZeroNative.Tests.Assets." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "index.html"), "<!doctype html><title>root</title>");
        Directory.CreateDirectory(Path.Combine(_root, "static"));
        File.WriteAllText(Path.Combine(_root, "static", "app.js"), "console.log('hello');");
        File.WriteAllBytes(Path.Combine(_root, "static", "logo.png"), new byte[] { 0x89, 0x50, 0x4E, 0x47 });
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Resolve_ServesExistingFile()
    {
        var server = new AssetServer(_root);
        var resp = server.Resolve("static/app.js")!;
        Assert.Equal(200, resp.StatusCode);
        Assert.Equal("application/javascript; charset=utf-8", resp.ContentType);
        Assert.Equal("console.log('hello');", Encoding.UTF8.GetString(resp.Body.ToArray()));
    }

    [Fact]
    public void Resolve_RootRequestServesEntry()
    {
        var server = new AssetServer(_root);
        var resp = server.Resolve("")!;
        Assert.Equal(200, resp.StatusCode);
        Assert.Equal("text/html; charset=utf-8", resp.ContentType);
    }

    [Fact]
    public void Resolve_SpaFallback_ReturnsEntryForUnknownRoute()
    {
        var server = new AssetServer(_root, spaFallback: true);
        var resp = server.Resolve("/some/unknown/route")!;
        Assert.Equal(200, resp.StatusCode);
        Assert.Contains("root", Encoding.UTF8.GetString(resp.Body.ToArray()));
    }

    [Fact]
    public void Resolve_SpaFallback_DoesNotFallbackForStaticAsset()
    {
        var server = new AssetServer(_root, spaFallback: true);
        var resp = server.Resolve("/static/missing.png")!;
        Assert.Equal(404, resp.StatusCode);
    }

    [Fact]
    public void Resolve_StripsSchemeQueryFragment()
    {
        var server = new AssetServer(_root);
        var resp = server.Resolve("zero://app/static/app.js?v=1#section")!;
        Assert.Equal(200, resp.StatusCode);
        Assert.Equal("application/javascript; charset=utf-8", resp.ContentType);
    }

    [Fact]
    public void Resolve_RejectsPathEscapingRoot()
    {
        var server = new AssetServer(_root, spaFallback: false);
        var resp = server.Resolve("../../etc/passwd");
        // Must NOT successfully serve a file outside the asset root.
        Assert.NotNull(resp);
        Assert.Equal(404, resp.StatusCode);
    }

    [Fact]
    public void GuessContentType_RecognizesCommonExtensions()
    {
        Assert.Equal("text/html; charset=utf-8", AssetServer.GuessContentType("page.html"));
        Assert.Equal("application/javascript; charset=utf-8", AssetServer.GuessContentType("a.js"));
        Assert.Equal("image/png", AssetServer.GuessContentType("logo.png"));
        Assert.Equal("application/wasm", AssetServer.GuessContentType("mod.wasm"));
        Assert.Equal("application/octet-stream", AssetServer.GuessContentType("blob.bin"));
    }
}
