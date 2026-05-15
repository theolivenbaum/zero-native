using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ZeroNative.Tooling;

public sealed class DevServerException : Exception
{
    public DevServerException(string message) : base(message) { }
    public DevServerException(string message, Exception inner) : base(message, inner) { }
}

public sealed record DevServerOptions
{
    public required string Url { get; init; }
    public IReadOnlyList<string>? Command { get; init; }
    public string ReadyPath { get; init; } = "/";
    public int TimeoutMs { get; init; } = 30_000;
    public string? WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string>? EnvironmentOverrides { get; init; }
    public TextWriter? Log { get; init; }
}

public sealed record UrlParts(string Host, int Port, string Path);

/// <summary>
/// Frontend dev-server orchestration. Mirrors the Zig <c>tooling/dev.zig</c>:
/// optionally starts a <c>vite</c> / <c>next dev</c> process, waits until it
/// responds, and exposes the resolved URL so the host can point its WebView at
/// the live bundle. Designed for hot-reload during development.
/// </summary>
public static class DevServer
{
    /// <summary>
    /// Starts the dev command (when configured) and waits until <paramref name="options"/>
    /// reports ready. Returns a handle that owns the spawned process — disposing it
    /// terminates the dev server. Setting environment variables for the embedding host
    /// is left to the caller via the returned <see cref="DevServerHandle.Url"/>.
    /// </summary>
    public static DevServerHandle Start(DevServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrEmpty(options.Url)) throw new DevServerException("Url is required");

        Process? child = null;
        if (options.Command is { Count: > 0 } cmd)
        {
            var psi = new ProcessStartInfo
            {
                FileName = cmd[0],
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                WorkingDirectory = options.WorkingDirectory ?? Environment.CurrentDirectory,
            };
            for (var i = 1; i < cmd.Count; i++) psi.ArgumentList.Add(cmd[i]);
            if (options.EnvironmentOverrides is { } env)
                foreach (var (k, v) in env) psi.Environment[k] = v;

            try { child = Process.Start(psi); }
            catch (Exception ex)
            {
                throw new DevServerException($"Failed to spawn dev command '{cmd[0]}': {ex.Message}", ex);
            }
        }

        try
        {
            WaitUntilReady(options.Url, options.ReadyPath, options.TimeoutMs, options.Log);
        }
        catch
        {
            try { child?.Kill(true); } catch { /* best effort */ }
            child?.Dispose();
            throw;
        }

        return new DevServerHandle(options.Url, child);
    }

    /// <summary>
    /// Parses an HTTP(S) URL into host/port/path components. Throws
    /// <see cref="DevServerException"/> for unsupported schemes or malformed inputs.
    /// </summary>
    public static UrlParts ParseHttpUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) throw new DevServerException("Empty URL");
        int defaultPort;
        string rest;
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            defaultPort = 80;
            rest = url["http://".Length..];
        }
        else if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            defaultPort = 443;
            rest = url["https://".Length..];
        }
        else
        {
            throw new DevServerException($"Unsupported scheme: {url}");
        }

        var slash = rest.IndexOf('/');
        var hostPort = slash < 0 ? rest : rest[..slash];
        if (hostPort.Length == 0 || hostPort.StartsWith(':'))
            throw new DevServerException($"Missing host: {url}");
        var path = slash < 0 ? "/" : rest[slash..];

        var colon = hostPort.LastIndexOf(':');
        if (colon > 0 && colon < hostPort.Length - 1)
        {
            if (!int.TryParse(hostPort[(colon + 1)..], out var port) || port <= 0 || port > 65535)
                throw new DevServerException($"Invalid port in URL: {url}");
            return new UrlParts(hostPort[..colon], port, path);
        }

        return new UrlParts(hostPort, defaultPort, path);
    }

    private static void WaitUntilReady(string url, string readyPath, int timeoutMs, TextWriter? log)
    {
        var parts = ParseHttpUrl(url);
        var host = string.Equals(parts.Host, "localhost", StringComparison.OrdinalIgnoreCase) ? "127.0.0.1" : parts.Host;
        var path = !string.IsNullOrEmpty(readyPath) ? readyPath : parts.Path;
        var deadline = Environment.TickCount64 + timeoutMs;

        while (Environment.TickCount64 <= deadline)
        {
            try
            {
                using var tcp = new TcpClient();
                var connect = tcp.ConnectAsync(host, parts.Port);
                if (!connect.Wait(TimeSpan.FromMilliseconds(500)))
                {
                    Sleep(100);
                    continue;
                }
                if (HttpReady(tcp, parts.Host, path))
                {
                    log?.WriteLine($"dev server ready at {url}");
                    return;
                }
            }
            catch
            {
                // Server not up yet — keep polling.
            }
            Sleep(100);
        }

        throw new DevServerException($"Dev server at {url} did not become ready within {timeoutMs}ms");
    }

    private static bool HttpReady(TcpClient tcp, string host, string path)
    {
        try
        {
            using var stream = tcp.GetStream();
            stream.WriteTimeout = 500;
            stream.ReadTimeout = 500;
            var request = $"GET {path} HTTP/1.1\r\nHost: {host}\r\nConnection: close\r\n\r\n";
            var bytes = Encoding.ASCII.GetBytes(request);
            stream.Write(bytes, 0, bytes.Length);

            var buffer = new byte[64];
            var len = stream.Read(buffer, 0, buffer.Length);
            if (len <= 0) return false;
            var response = Encoding.ASCII.GetString(buffer, 0, len);
            return response.StartsWith("HTTP/1.1 2", StringComparison.Ordinal)
                || response.StartsWith("HTTP/1.0 2", StringComparison.Ordinal)
                || response.StartsWith("HTTP/1.1 3", StringComparison.Ordinal)
                || response.StartsWith("HTTP/1.0 3", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static void Sleep(int ms) => Thread.Sleep(ms);
}

/// <summary>
/// Live handle to a running dev server. Disposing kills the child process if one
/// was spawned. The <see cref="Url"/> property is the resolved frontend URL the
/// host should point at.
/// </summary>
public sealed class DevServerHandle : IDisposable
{
    public string Url { get; }
    public Process? Process { get; }

    internal DevServerHandle(string url, Process? process)
    {
        Url = url;
        Process = process;
    }

    public void Dispose()
    {
        if (Process is null) return;
        try
        {
            if (!Process.HasExited) Process.Kill(entireProcessTree: true);
        }
        catch { /* best effort */ }
        Process.Dispose();
    }
}
