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
}
