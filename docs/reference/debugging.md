---
order: 9
title: Debugging and Tracing
icon: bug
tags: [reference]
---

# Debugging and Tracing

The runtime emits `TraceRecord` structs through a configurable sink. The
`TraceSinks` helpers cover the common output paths without taking a hard
dependency on a logging framework.

## Configure a sink

```csharp
using ZeroNative.Debug;

var runtime = new Runtime(new RuntimeOptions
{
    Platform  = platform,
    TraceSink = TraceSinks.Console(),
});
```

The built-in helpers compose. Send everything to a JSON-lines file *and* to
the console, filtered to `Info` and above:

```csharp
var sink = TraceSinks.WithMinLevel(
    TraceLevel.Info,
    TraceSinks.Tee(
        TraceSinks.Console(),
        TraceSinks.JsonFile("/var/log/myapp/trace.ndjson")));
```

## Available sinks

| Sink | Description |
|---|---|
| `TraceSinks.Console()` | Writes a one-line summary per record to `stderr`. |
| `TraceSinks.JsonFile(path)` | Appends records as NDJSON to a file. |
| `TraceSinks.Tee(a, b, …)` | Fans out to multiple sinks. |
| `TraceSinks.WithMinLevel(level, inner)` | Drops records below `level`. |
| `TraceSinks.Null` | Discards everything. Useful as a default. |
| `TraceSinks.FromLogger(log)` | Adapter for `Microsoft.Extensions.Logging.ILogger.Log` – maps `TraceLevel` to `LogLevel`. |

`FromLogger` does **not** require a package reference: the adapter accepts a
delegate matching the `ILogger.Log` signature, so Core remains
logging-framework-agnostic.

## Trace levels

| Level | Used for |
|---|---|
| `Trace` | Internal frame timings, fine-grained bridge ledger. |
| `Debug` | Verbose lifecycle / state transitions. |
| `Info` | Routine lifecycle and event records (default). |
| `Warn` | Recoverable errors. |
| `Error` | Failures that the runtime caught. |

## Record shape

```csharp
public readonly record struct TraceRecord(
    DateTimeOffset Timestamp,
    TraceLevel     Level,
    string         Name,
    string         Message,
    IReadOnlyDictionary<string, string>? Fields = null);
```

Use the `Fields` bag for structured data (window id, command name, error
code). `TraceSinks.JsonFile` serializes it into the NDJSON output.

## Adapting `ILogger`

```csharp
using Microsoft.Extensions.Logging;

var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
var logger        = loggerFactory.CreateLogger("zero-native");

var runtime = new Runtime(new RuntimeOptions
{
    Platform  = platform,
    TraceSink = TraceSinks.FromLogger(
        (level, eventId, state, exception, formatter) =>
            logger.Log(level, eventId, state, exception, formatter)),
});
```

## Lifecycle snapshot tests

`RuntimeLifecycleSnapshotTests` (under `tests/ZeroNative.Core.Tests/`) locks
down the canonical lifecycle trace order, so a regression in trace shape
shows up as a diff against the golden file rather than a missed bug.
