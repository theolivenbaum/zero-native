namespace ZeroNative.Security;

public static class Permissions
{
    public const string Window = "window";
    public const string Filesystem = "filesystem";
    public const string Clipboard = "clipboard";
    public const string Network = "network";
}

public enum ExternalLinkAction
{
    Deny = 0,
    OpenSystemBrowser = 1,
}

public sealed record ExternalLinkPolicy(
    ExternalLinkAction Action = ExternalLinkAction.Deny,
    IReadOnlyList<string>? AllowedUrls = null)
{
    public IReadOnlyList<string> AllowedUrls { get; init; } = AllowedUrls ?? Array.Empty<string>();
}

public sealed record NavigationPolicy(
    IReadOnlyList<string>? AllowedOrigins = null,
    ExternalLinkPolicy? ExternalLinks = null)
{
    public IReadOnlyList<string> AllowedOrigins { get; init; } =
        AllowedOrigins ?? new[] { "zero://app", "zero://inline" };

    public ExternalLinkPolicy ExternalLinks { get; init; } = ExternalLinks ?? new ExternalLinkPolicy();
}

public sealed record SecurityPolicy(
    IReadOnlyList<string>? Permissions = null,
    NavigationPolicy? Navigation = null)
{
    public IReadOnlyList<string> Permissions { get; init; } = Permissions ?? Array.Empty<string>();
    public NavigationPolicy Navigation { get; init; } = Navigation ?? new NavigationPolicy();

    public bool HasPermission(string permission)
        => Permissions.Any(p => p == permission);

    public bool HasPermissions(IEnumerable<string> required)
        => required.All(HasPermission);

    public bool AllowsOrigin(string origin)
        => AllowsOrigin(Navigation.AllowedOrigins, origin);

    /// <summary>
    /// Decides what should happen for a navigation to <paramref name="targetUrl"/>.
    /// In-policy origins are allowed inline; everything else flows through the configured
    /// <see cref="NavigationPolicy.ExternalLinks"/> rules.
    /// </summary>
    public NavigationDecision DecideNavigation(string targetUrl)
    {
        if (string.IsNullOrEmpty(targetUrl)) return NavigationDecision.Block;
        var origin = OriginOf(targetUrl);
        if (origin is { Length: > 0 } && AllowsOrigin(origin))
            return NavigationDecision.AllowInline;

        var ext = Navigation.ExternalLinks;
        if (ext.AllowedUrls.Count > 0 && !MatchesAllowedUrl(ext.AllowedUrls, targetUrl))
            return NavigationDecision.Block;

        return ext.Action == ExternalLinkAction.OpenSystemBrowser
            ? NavigationDecision.OpenExternally
            : NavigationDecision.Block;
    }

    /// <summary>
    /// Extracts the <c>scheme://host[:port]</c> origin from a URL. Returns the input unchanged
    /// when it already looks like a bare origin, or the empty string when the URL is unusable.
    /// </summary>
    public static string OriginOf(string url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri.IsDefaultPort
                ? $"{uri.Scheme}://{uri.Host}"
                : $"{uri.Scheme}://{uri.Host}:{uri.Port}";
        }
        // Treat schemes like "zero://app/foo" as origin "zero://app" when the URI parser balks.
        var schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0) return "";
        var rest = url[(schemeEnd + 3)..];
        var slashIdx = rest.IndexOf('/');
        var hostPart = slashIdx >= 0 ? rest[..slashIdx] : rest;
        return $"{url[..schemeEnd]}://{hostPart}";
    }

    public static bool AllowsOrigin(IEnumerable<string> allowedOrigins, string origin)
    {
        foreach (var allowed in allowedOrigins)
        {
            if (allowed == "*") return true;
            if (allowed == origin) return true;
        }
        return false;
    }

    public static bool HasPermission(IEnumerable<string> grants, string permission)
        => grants.Any(g => g == permission);

    public static bool HasPermissions(IEnumerable<string> grants, IEnumerable<string> required)
    {
        var grantSet = new HashSet<string>(grants, StringComparer.Ordinal);
        return required.All(grantSet.Contains);
    }

    private static bool MatchesAllowedUrl(IReadOnlyList<string> allowed, string target)
    {
        foreach (var pattern in allowed)
        {
            if (pattern == "*") return true;
            if (pattern == target) return true;
            // Trailing-wildcard prefix match: "https://docs.example.com/*"
            if (pattern.EndsWith("/*", StringComparison.Ordinal))
            {
                var prefix = pattern[..^1];
                if (target.StartsWith(prefix, StringComparison.Ordinal)) return true;
            }
        }
        return false;
    }
}

public enum NavigationDecision
{
    /// <summary>Allow the request to proceed inside the WebView.</summary>
    AllowInline,
    /// <summary>Reject the request entirely.</summary>
    Block,
    /// <summary>Hand the URL to the OS to open in the system browser.</summary>
    OpenExternally,
}
