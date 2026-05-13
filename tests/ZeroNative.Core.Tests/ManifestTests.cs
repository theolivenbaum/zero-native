using Xunit;
using ZeroNative.Manifest;

namespace ZeroNative.Tests;

public class ManifestTests
{
    [Fact]
    public void Validate_AcceptsReverseDnsId()
    {
        var manifest = new AppManifest
        {
            Identity = new AppIdentity("com.example.app", "Example"),
            Version = new AppVersion(0, 1, 0),
        };
        ManifestValidator.Validate(manifest);
    }

    [Fact]
    public void Validate_RejectsSingleSegmentId()
    {
        var manifest = new AppManifest
        {
            Identity = new AppIdentity("example", "Example"),
            Version = new AppVersion(0, 1, 0),
        };
        Assert.Throws<ManifestValidationException>(() => ManifestValidator.Validate(manifest));
    }

    [Fact]
    public void Validate_RejectsDuplicateWindowLabel()
    {
        var manifest = new AppManifest
        {
            Identity = new AppIdentity("com.example.app", "Example"),
            Version = new AppVersion(0, 1, 0),
            Windows = new[]
            {
                new ManifestWindow("main"),
                new ManifestWindow("main"),
            },
        };
        Assert.Throws<ManifestValidationException>(() => ManifestValidator.Validate(manifest));
    }

    [Fact]
    public void Permission_AsString_ReturnsExpectedValues()
    {
        Assert.Equal("window", Permission.Window().AsString());
        Assert.Equal("filesystem", Permission.Filesystem().AsString());
        Assert.Equal("my.permission", Permission.Custom("my.permission").AsString());
    }
}
