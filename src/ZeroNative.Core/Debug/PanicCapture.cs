namespace ZeroNative.Debug;

/// <summary>
/// Captures unhandled exceptions to <c>last-panic.txt</c> and appends a
/// corresponding ndjson record to the trace log. Mirrors the Zig
/// <c>src/debug/root.zig</c> panic-capture pipeline.
/// </summary>
public static class PanicCapture
{
    private static string? s_logDir;
    private static string? s_panicFile;
    private static string? s_traceFile;
    private static bool s_installed;
    private static readonly object s_lock = new();

    public sealed record LogPaths(string LogDir, string TraceFile, string PanicFile);

    /// <summary>
    /// Resolves log paths for <paramref name="appName"/> under the per-OS log root.
    /// Honors <c>ZERO_NATIVE_LOG_DIR</c> override via <see cref="AppDirs.GetLogDirectory"/>.
    /// </summary>
    public static LogPaths Resolve(string appName)
    {
        var dir = AppDirs.GetLogDirectory(appName);
        return new LogPaths(dir, Path.Combine(dir, "zero-native.jsonl"), Path.Combine(dir, "last-panic.txt"));
    }

    /// <summary>
    /// Subscribes to <see cref="AppDomain.UnhandledException"/> so the next
    /// crash writes <c>last-panic.txt</c> and an ndjson trace record. Idempotent.
    /// </summary>
    public static LogPaths Install(string appName)
    {
        var paths = Resolve(appName);
        Install(paths);
        return paths;
    }

    public static void Install(LogPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        lock (s_lock)
        {
            s_logDir = paths.LogDir;
            s_panicFile = paths.PanicFile;
            s_traceFile = paths.TraceFile;
            if (s_installed) return;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            s_installed = true;
        }
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is not Exception ex) return;
        try { Write(ex.Message, ex.StackTrace); } catch { /* best effort */ }
    }

    public static void Write(string message, string? stackTrace)
    {
        var dir = s_logDir;
        var panic = s_panicFile;
        var trace = s_traceFile;
        if (dir is null || panic is null || trace is null) return;
        try
        {
            Directory.CreateDirectory(dir);
            using (var w = new StreamWriter(panic, append: false))
            {
                w.Write("panic: ");
                w.WriteLine(message);
                if (!string.IsNullOrEmpty(stackTrace))
                {
                    w.WriteLine();
                    w.WriteLine(stackTrace);
                }
            }
            // ndjson trace line, format identical to TraceSinks.JsonFile.
            var json = "{\"ts\":\"" + DateTimeOffset.UtcNow.ToString("O")
                + "\",\"level\":\"fatal\",\"name\":\"panic\",\"message\":"
                + System.Text.Json.JsonSerializer.Serialize(message) + "}\n";
            File.AppendAllText(trace, json);
        }
        catch
        {
            // Never throw from inside a panic handler.
        }
    }
}
