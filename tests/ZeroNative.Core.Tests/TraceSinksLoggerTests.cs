using Xunit;
using ZeroNative.Runtime;

namespace ZeroNative.Tests;

public class TraceSinksLoggerTests
{
    [Fact]
    public void FromLogger_ForwardsLevelMessageAndFields()
    {
        var captured = new List<(int Level, string Message, IReadOnlyDictionary<string, object?> Fields)>();
        var sink = TraceSinks.FromLogger((level, message, fields) =>
            captured.Add((level, message, fields)));

        sink(new TraceRecord(
            DateTimeOffset.UnixEpoch,
            "warn",
            "runtime.event",
            "something happened",
            new Dictionary<string, object?> { ["frame"] = 7 }));

        var entry = Assert.Single(captured);
        Assert.Equal(3, entry.Level);
        Assert.Equal("something happened", entry.Message);
        Assert.Equal(7, entry.Fields["frame"]);
    }

    [Fact]
    public void FromLogger_FallsBackToName_WhenMessageMissing()
    {
        string? captured = null;
        var sink = TraceSinks.FromLogger((_, message, _) => captured = message);
        sink(new TraceRecord(DateTimeOffset.UtcNow, "info", "runtime.tick", null,
            new Dictionary<string, object?>()));
        Assert.Equal("runtime.tick", captured);
    }

    [Theory]
    [InlineData("trace", 0)]
    [InlineData("debug", 1)]
    [InlineData("info", 2)]
    [InlineData("information", 2)]
    [InlineData("warn", 3)]
    [InlineData("warning", 3)]
    [InlineData("error", 4)]
    [InlineData("critical", 5)]
    [InlineData("unknown", 2)]
    public void LevelToLoggingLevel_MapsCanonicalNames(string level, int expected)
    {
        Assert.Equal(expected, TraceSinks.LevelToLoggingLevel(level));
    }
}
