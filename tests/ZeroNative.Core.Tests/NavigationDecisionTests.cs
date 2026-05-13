using Xunit;
using ZeroNative.Security;

namespace ZeroNative.Tests;

public class NavigationDecisionTests
{
    [Fact]
    public void DecideNavigation_AllowsConfiguredOrigin()
    {
        var policy = new SecurityPolicy(
            Navigation: new NavigationPolicy(AllowedOrigins: new[] { "zero://app" }));

        Assert.Equal(NavigationDecision.AllowInline, policy.DecideNavigation("zero://app/index.html"));
        Assert.Equal(NavigationDecision.AllowInline, policy.DecideNavigation("zero://app"));
    }

    [Fact]
    public void DecideNavigation_OpensExternally_WhenPolicySaysSo()
    {
        var policy = new SecurityPolicy(
            Navigation: new NavigationPolicy(
                AllowedOrigins: new[] { "zero://app" },
                ExternalLinks: new ExternalLinkPolicy(ExternalLinkAction.OpenSystemBrowser)));

        Assert.Equal(NavigationDecision.OpenExternally,
            policy.DecideNavigation("https://example.com/docs"));
    }

    [Fact]
    public void DecideNavigation_Blocks_WhenOutsideAllowedUrlList()
    {
        var policy = new SecurityPolicy(
            Navigation: new NavigationPolicy(
                AllowedOrigins: new[] { "zero://app" },
                ExternalLinks: new ExternalLinkPolicy(
                    ExternalLinkAction.OpenSystemBrowser,
                    AllowedUrls: new[] { "https://docs.example.com/*" })));

        Assert.Equal(NavigationDecision.Block,
            policy.DecideNavigation("https://evil.example.com/"));
        Assert.Equal(NavigationDecision.OpenExternally,
            policy.DecideNavigation("https://docs.example.com/guide"));
    }

    [Fact]
    public void DecideNavigation_DefaultsToBlock_ForExternalUrls()
    {
        var policy = new SecurityPolicy(
            Navigation: new NavigationPolicy(AllowedOrigins: new[] { "zero://app" }));

        Assert.Equal(NavigationDecision.Block, policy.DecideNavigation("https://example.com"));
    }

    [Fact]
    public void DecideNavigation_Wildcard_AllowsAnyOrigin()
    {
        var policy = new SecurityPolicy(
            Navigation: new NavigationPolicy(AllowedOrigins: new[] { "*" }));

        Assert.Equal(NavigationDecision.AllowInline, policy.DecideNavigation("https://anywhere.test/x"));
    }

    [Theory]
    [InlineData("zero://app/foo", "zero://app")]
    [InlineData("https://example.com/path", "https://example.com")]
    [InlineData("https://example.com:4443/path", "https://example.com:4443")]
    [InlineData("http://127.0.0.1:5173", "http://127.0.0.1:5173")]
    public void OriginOf_ExtractsExpectedOrigin(string url, string expected)
    {
        Assert.Equal(expected, SecurityPolicy.OriginOf(url));
    }

    [Fact]
    public void OriginOf_ReturnsEmpty_ForUnusableInput()
    {
        Assert.Equal("", SecurityPolicy.OriginOf(""));
        Assert.Equal("", SecurityPolicy.OriginOf("not-a-url"));
    }
}
