namespace ZeroNative.Assets;

public sealed record AssetEntry(string Id, string SourcePath, string BundlePath, string? ContentType = null);

public sealed record AssetManifest
{
    public IReadOnlyList<AssetEntry> Assets { get; init; } = Array.Empty<AssetEntry>();

    public AssetEntry? FindById(string id)
        => Assets.FirstOrDefault(a => a.Id == id);

    public AssetEntry? FindByBundlePath(string path)
        => Assets.FirstOrDefault(a => MatchPath(a.BundlePath, path));

    private static bool MatchPath(string entry, string requested)
    {
        var a = entry.Replace('\\', '/').TrimStart('/');
        var b = requested.Replace('\\', '/').TrimStart('/');
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record AssetResponse(ReadOnlyMemory<byte> Body, string ContentType, int StatusCode = 200);

/// <summary>
/// Serves the static frontend bundle from disk.
/// When <see cref="SpaFallback"/> is true and a path doesn't resolve to a file, the
/// configured <see cref="Entry"/> document is returned so client-side router routes work.
/// </summary>
public sealed class AssetServer
{
    public AssetManifest Manifest { get; }
    public string RootDirectory { get; }
    public string Entry { get; }
    public bool SpaFallback { get; }

    public AssetServer(string rootDirectory, string entry = "index.html", bool spaFallback = true, AssetManifest? manifest = null)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
        Entry = entry;
        SpaFallback = spaFallback;
        Manifest = manifest ?? new AssetManifest();
    }

    public AssetResponse? Resolve(string requestedPath)
    {
        var normalized = NormalizePath(requestedPath);

        // Manifest takes precedence if the request matches an explicit bundle path.
        var entry = Manifest.FindByBundlePath(normalized);
        if (entry is not null)
        {
            var disk = Path.GetFullPath(Path.Combine(RootDirectory, entry.SourcePath));
            if (IsInside(RootDirectory, disk) && File.Exists(disk))
                return new AssetResponse(File.ReadAllBytes(disk), entry.ContentType ?? GuessContentType(disk));
        }

        // Otherwise, try the disk path directly under the root.
        var candidate = string.IsNullOrEmpty(normalized) ? Entry : normalized;
        var diskPath = Path.GetFullPath(Path.Combine(RootDirectory, candidate));
        if (IsInside(RootDirectory, diskPath))
        {
            if (File.Exists(diskPath))
                return new AssetResponse(File.ReadAllBytes(diskPath), GuessContentType(diskPath));

            // Trailing-slash directories → look for an index file inside.
            if (Directory.Exists(diskPath))
            {
                var indexPath = Path.Combine(diskPath, Entry);
                if (File.Exists(indexPath))
                    return new AssetResponse(File.ReadAllBytes(indexPath), GuessContentType(indexPath));
            }
        }

        if (!SpaFallback)
            return new AssetResponse(ReadOnlyMemory<byte>.Empty, "text/plain; charset=utf-8", 404);

        // Don't fall back for paths that look like static asset requests
        // (have a file extension other than .html). Otherwise React-router
        // style routes get served the SPA shell.
        var ext = Path.GetExtension(normalized);
        if (!string.IsNullOrEmpty(ext) && !ext.Equals(".html", StringComparison.OrdinalIgnoreCase))
            return new AssetResponse(ReadOnlyMemory<byte>.Empty, "text/plain; charset=utf-8", 404);

        var fallbackPath = Path.GetFullPath(Path.Combine(RootDirectory, Entry));
        if (File.Exists(fallbackPath))
            return new AssetResponse(File.ReadAllBytes(fallbackPath), GuessContentType(fallbackPath));

        return new AssetResponse(ReadOnlyMemory<byte>.Empty, "text/plain; charset=utf-8", 404);
    }

    public ReadOnlyMemory<byte>? ReadAsset(string path) => Resolve(path)?.Body;

    private static string NormalizePath(string path)
    {
        var s = path.Replace('\\', '/').TrimStart('/');
        // Strip a leading scheme/host if present, e.g. "zero://app/foo" -> "foo"
        var schemeIdx = s.IndexOf("://", StringComparison.Ordinal);
        if (schemeIdx >= 0)
        {
            var rest = s[(schemeIdx + 3)..];
            var slash = rest.IndexOf('/');
            s = slash >= 0 ? rest[(slash + 1)..] : "";
        }
        // Strip query string and fragment.
        var q = s.IndexOf('?');
        if (q >= 0) s = s[..q];
        var h = s.IndexOf('#');
        if (h >= 0) s = s[..h];
        return s;
    }

    private static bool IsInside(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(normalizedRoot, StringComparison.Ordinal);
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
