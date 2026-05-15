using Xunit;
using ZeroNative.Debug;

namespace ZeroNative.Tests;

public class PanicCaptureTests
{
    [Fact]
    public void Write_persists_a_panic_file_and_appends_a_trace_record()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "zero-native-panic-test-" + Guid.NewGuid().ToString("N"));
        var paths = new PanicCapture.LogPaths(tmp,
            Path.Combine(tmp, "zero-native.jsonl"),
            Path.Combine(tmp, "last-panic.txt"));
        try
        {
            PanicCapture.Install(paths);
            PanicCapture.Write("the disk is on fire", "at frame 42");

            Assert.True(File.Exists(paths.PanicFile));
            var body = File.ReadAllText(paths.PanicFile);
            Assert.Contains("panic: the disk is on fire", body);
            Assert.Contains("at frame 42", body);

            Assert.True(File.Exists(paths.TraceFile));
            var trace = File.ReadAllText(paths.TraceFile);
            Assert.Contains("\"name\":\"panic\"", trace);
            Assert.Contains("\"level\":\"fatal\"", trace);
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { /* best effort */ }
        }
    }
}
