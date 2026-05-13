using Xunit;
using ZeroNative.Automation;

namespace ZeroNative.Tests;

public class AutomationProtocolTests
{
    [Fact]
    public void Parse_Reload_HasNoValue()
    {
        var cmd = AutomationProtocol.Parse("reload");
        Assert.Equal(AutomationAction.Reload, cmd.Action);
        Assert.Equal("", cmd.Value);
    }

    [Fact]
    public void Parse_Wait_StripsActionAndKeepsValue()
    {
        var cmd = AutomationProtocol.Parse("wait frame");
        Assert.Equal(AutomationAction.Wait, cmd.Action);
        Assert.Equal("frame", cmd.Value);
    }

    [Fact]
    public void Parse_Wait_NoValue_LeavesValueEmpty()
    {
        var cmd = AutomationProtocol.Parse("wait");
        Assert.Equal(AutomationAction.Wait, cmd.Action);
        Assert.Equal("", cmd.Value);
    }

    [Fact]
    public void Parse_Bridge_KeepsJsonPayloadVerbatim()
    {
        const string payload = "{\"id\":\"1\",\"command\":\"native.ping\",\"payload\":{\"source\":\"smoke\"}}";
        var cmd = AutomationProtocol.Parse("bridge " + payload);
        Assert.Equal(AutomationAction.Bridge, cmd.Action);
        Assert.Equal(payload, cmd.Value);
    }

    [Fact]
    public void Parse_Bridge_WithoutPayload_Throws()
    {
        Assert.Throws<AutomationInvalidCommandException>(() => AutomationProtocol.Parse("bridge"));
    }

    [Fact]
    public void Parse_TrimsWhitespaceAndCarriageReturns()
    {
        var cmd = AutomationProtocol.Parse("  reload  \r\n");
        Assert.Equal(AutomationAction.Reload, cmd.Action);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\r\t")]
    [InlineData("unknown action")]
    public void Parse_RejectsInvalidInput(string line)
    {
        Assert.Throws<AutomationInvalidCommandException>(() => AutomationProtocol.Parse(line));
    }

    [Fact]
    public void FormatLine_ProducesExpectedFormat()
    {
        Assert.Equal("reload\n", AutomationProtocol.FormatLine("reload", ""));
        Assert.Equal("wait frame\n", AutomationProtocol.FormatLine("wait", "frame"));
    }

    [Fact]
    public void FormatLine_TooLarge_Throws()
    {
        var huge = new string('a', AutomationProtocol.MaxCommandBytes);
        Assert.Throws<AutomationCommandTooLargeException>(
            () => AutomationProtocol.FormatLine("bridge", huge));
    }
}
