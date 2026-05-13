using Xunit;
using ZeroNative.Security;

namespace ZeroNative.Tests;

public class SecurityTests
{
    [Fact]
    public void HasPermissions_RequiresEveryGrant()
    {
        Assert.True(SecurityPolicy.HasPermissions(
            new[] { Permissions.Window, Permissions.Filesystem },
            new[] { Permissions.Window }));
        Assert.False(SecurityPolicy.HasPermissions(
            new[] { Permissions.Window },
            new[] { Permissions.Window, Permissions.Filesystem }));
    }

    [Fact]
    public void AllowsOrigin_SupportsExactAndWildcard()
    {
        Assert.True(SecurityPolicy.AllowsOrigin(new[] { "zero://app", "zero://inline" }, "zero://inline"));
        Assert.True(SecurityPolicy.AllowsOrigin(new[] { "*" }, "https://example.invalid"));
        Assert.False(SecurityPolicy.AllowsOrigin(new[] { "zero://app" }, "https://example.invalid"));
    }

    [Fact]
    public void Policy_DefaultsAllowZeroOrigins()
    {
        var p = new SecurityPolicy();
        Assert.Contains("zero://app", p.Navigation.AllowedOrigins);
        Assert.Contains("zero://inline", p.Navigation.AllowedOrigins);
    }
}
