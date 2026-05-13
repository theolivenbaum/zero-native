using System.Text;
using ZeroNative.Primitives;

namespace ZeroNative.Runtime;

/// <summary>
/// Pre-built <see cref="TraceRecord"/> sinks suitable for <see cref="RuntimeOptions.TraceSink"/>.
/// </summary>
public static class TraceSinks
{
    /// <summary>Discards every record.</summary>
    public static Action<TraceRecord> Null { get; } = static _ => { };

    /// <summary>Writes a human-readable single-line summary to stderr.</summary>
    public static Action<TraceRecord> Console { get; } = static record =>
    {
        var fields = FormatFields(record.Fields);
        var msg = record.Message is null ? "" : " " + record.Message;
        System.Console.Error.WriteLine($"[{record.Timestamp:HH:mm:ss.fff}] {record.Level.PadRight(5)} {record.Name}{msg}{fields}");
    };

    /// <summary>Writes records as newline-delimited JSON to the given path. Append-only.</summary>
    public static Action<TraceRecord> JsonFile(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var sync = new object();
        return record =>
        {
            var json = ToJsonLine(record);
            lock (sync)
            {
                File.AppendAllText(path, json + "\n");
            }
        };
    }

    /// <summary>Forwards records to multiple sinks.</summary>
    public static Action<TraceRecord> Tee(params Action<TraceRecord>[] sinks) =>
        record =>
        {
            foreach (var sink in sinks)
            {
                try { sink(record); }
                catch { /* sinks shouldn't take each other down */ }
            }
        };

    /// <summary>
    /// Filters records by minimum severity level. Levels are compared case-insensitively
    /// in the canonical order: trace, debug, info, warn, error.
    /// </summary>
    public static Action<TraceRecord> WithMinLevel(string minLevel, Action<TraceRecord> inner)
    {
        var threshold = LevelRank(minLevel);
        return record =>
        {
            if (LevelRank(record.Level) >= threshold) inner(record);
        };
    }

    /// <summary>
    /// Adapts an <c>ILogger</c>-style log delegate so trace records flow into an existing
    /// <c>Microsoft.Extensions.Logging</c> pipeline without taking a dependency on it.
    /// <para>
    /// The <paramref name="emit"/> signature matches <c>ILogger.Log</c> after currying the
    /// event id and exception arguments. Typical wiring from a consumer that does reference
    /// the logging package:
    /// </para>
    /// <code>
    /// var sink = TraceSinks.FromLogger((level, message, fields) =>
    ///     logger.Log((LogLevel)level, "{Message} {@Fields}", message, fields));
    /// </code>
    /// <para>
    /// The integer level matches <c>Microsoft.Extensions.Logging.LogLevel</c>:
    /// Trace=0, Debug=1, Information=2, Warning=3, Error=4, Critical=5.
    /// </para>
    /// </summary>
    public static Action<TraceRecord> FromLogger(Action<int, string, IReadOnlyDictionary<string, object?>> emit)
    {
        ArgumentNullException.ThrowIfNull(emit);
        return record =>
        {
            var msg = record.Message ?? record.Name;
            emit(LevelToLoggingLevel(record.Level), msg, record.Fields);
        };
    }

    /// <summary>Maps the canonical level name to the integer used by <c>Microsoft.Extensions.Logging.LogLevel</c>.</summary>
    public static int LevelToLoggingLevel(string level) => level.ToLowerInvariant() switch
    {
        "trace" => 0,
        "debug" => 1,
        "info" or "information" => 2,
        "warn" or "warning" => 3,
        "error" or "err" => 4,
        "critical" or "crit" or "fatal" => 5,
        _ => 2,
    };

    private static int LevelRank(string level) => level.ToLowerInvariant() switch
    {
        "trace" => 0,
        "debug" => 1,
        "info" => 2,
        "warn" or "warning" => 3,
        "error" or "err" => 4,
        _ => 2, // unknown → info
    };

    private static string FormatFields(IReadOnlyDictionary<string, object?> fields)
    {
        if (fields.Count == 0) return "";
        var sb = new StringBuilder();
        sb.Append(" {");
        var first = true;
        foreach (var (k, v) in fields)
        {
            if (!first) sb.Append(", ");
            first = false;
            sb.Append(k).Append('=').Append(v?.ToString() ?? "null");
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static string ToJsonLine(TraceRecord record)
    {
        var sb = new StringBuilder(128);
        sb.Append("{\"ts\":");
        JsonUtilities.AppendString(sb, record.Timestamp.ToString("O"));
        sb.Append(",\"level\":");
        JsonUtilities.AppendString(sb, record.Level);
        sb.Append(",\"name\":");
        JsonUtilities.AppendString(sb, record.Name);
        if (record.Message is not null)
        {
            sb.Append(",\"message\":");
            JsonUtilities.AppendString(sb, record.Message);
        }
        if (record.Fields.Count > 0)
        {
            sb.Append(",\"fields\":{");
            var first = true;
            foreach (var (k, v) in record.Fields)
            {
                if (!first) sb.Append(',');
                first = false;
                JsonUtilities.AppendString(sb, k);
                sb.Append(':');
                AppendFieldValue(sb, v);
            }
            sb.Append('}');
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static void AppendFieldValue(StringBuilder sb, object? value)
    {
        switch (value)
        {
            case null:
                sb.Append("null");
                break;
            case bool b:
                sb.Append(b ? "true" : "false");
                break;
            case string s:
                JsonUtilities.AppendString(sb, s);
                break;
            case int or long or short or byte or uint or ulong or ushort or sbyte:
                sb.Append(value);
                break;
            case float f:
                sb.Append(f.ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;
            case double d:
                sb.Append(d.ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;
            default:
                JsonUtilities.AppendString(sb, value.ToString() ?? "");
                break;
        }
    }
}
