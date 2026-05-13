namespace ZeroNative.Assets;

public sealed record AssetEntry(string Id, string SourcePath, string BundlePath, string? ContentType = null);

public sealed record AssetManifest
{
    public IReadOnlyList<AssetEntry> Assets { get; init; } = Array.Empty<AssetEntry>();

    public AssetEntry? FindById(string id)
        => Assets.FirstOrDefault(a => a.Id == id);

    public AssetEntry? FindByBundlePath(string path)
        => Assets.FirstOrDefault(a => a.BundlePath == path);
}

public sealed class AssetServer
{
    public AssetManifest Manifest { get; }
    public string RootDirectory { get; }

    public AssetServer(string rootDirectory, AssetManifest? manifest = null)
    {
        RootDirectory = rootDirectory;
        Manifest = manifest ?? new AssetManifest();
    }

    public ReadOnlyMemory<byte>? ReadAsset(string path)
    {
        var entry = Manifest.FindByBundlePath(path);
        var diskPath = entry is not null
            ? Path.Combine(RootDirectory, entry.SourcePath)
            : Path.Combine(RootDirectory, path);
        if (!File.Exists(diskPath)) return null;
        return File.ReadAllBytes(diskPath);
    }

    public static string GuessContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".js" or ".mjs" => "application/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".gif" => "image/gif",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            ".wasm" => "application/wasm",
            ".txt" => "text/plain; charset=utf-8",
            ".map" => "application/json; charset=utf-8",
            _ => "application/octet-stream",
        };
    }
}
