using Xunit;
using ZeroNative.Runtime;

namespace ZeroNative.Tests;

public class TraceSinksTests : IDisposable
{
    private readonly string _logPath;

    public TraceSinksTests()
    {
        _logPath = Path.Combine(Path.GetTempPath(), "ZeroNative.Tests.Trace." + Guid.NewGuid().ToString("N") + ".jsonl");
    }

    public void Dispose()
    {
        if (File.Exists(_logPath)) File.Delete(_logPath);
    }

    [Fact]
    public void JsonFile_WritesNdjsonLines()
    {
        var sink = TraceSinks.JsonFile(_logPath);
        sink(MakeRecord("info", "test.event", "hello", new() { ["a"] = 1, ["b"] = "v" }));
        sink(MakeRecord("warn", "test.warn", null, new()));

        var lines = File.ReadAllLines(_logPath);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"name\":\"test.event\"", lines[0]);
        Assert.Contains("\"a\":1", lines[0]);
        Assert.Contains("\"b\":\"v\"", lines[0]);
        Assert.Contains("\"level\":\"warn\"", lines[1]);
        Assert.DoesNotContain("\"fields\"", lines[1]);
    }

    [Fact]
    public void Tee_BroadcastsToAllSinks()
    {
        var seen1 = new List<string>();
        var seen2 = new List<string>();
        var sink = TraceSinks.Tee(r => seen1.Add(r.Name), r => seen2.Add(r.Name));
        sink(MakeRecord("info", "fan-out", null, new()));
        Assert.Single(seen1);
        Assert.Single(seen2);
    }

    [Fact]
    public void WithMinLevel_DropsBelowThreshold()
    {
        var seen = new List<string>();
        var sink = TraceSinks.WithMinLevel("warn", r => seen.Add(r.Level));
        sink(MakeRecord("debug", "d", null, new()));
        sink(MakeRecord("info", "i", null, new()));
        sink(MakeRecord("warn", "w", null, new()));
        sink(MakeRecord("error", "e", null, new()));
        Assert.Equal(new[] { "warn", "error" }, seen);
    }

    [Fact]
    public void Null_SilentlyDiscards()
    {
        // Just ensure it doesn't throw.
        TraceSinks.Null(MakeRecord("info", "any", null, new()));
    }

    private static TraceRecord MakeRecord(string level, string name, string? msg, Dictionary<string, object?> fields)
        => new(DateTimeOffset.UtcNow, level, name, msg, fields);
}
