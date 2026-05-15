using System.Net;
using System.Net.Sockets;
using ZeroNative.Tooling;
using Xunit;

namespace ZeroNative.Tests;

public class DevServerTests
{
    [Fact]
    public void ParseHttpUrl_parses_vite_default()
    {
        var p = DevServer.ParseHttpUrl("http://127.0.0.1:5173/");
        Assert.Equal("127.0.0.1", p.Host);
        Assert.Equal(5173, p.Port);
        Assert.Equal("/", p.Path);
    }

    [Fact]
    public void ParseHttpUrl_parses_next_default_with_subpath()
    {
        var p = DevServer.ParseHttpUrl("http://localhost:3000/app");
        Assert.Equal("localhost", p.Host);
        Assert.Equal(3000, p.Port);
        Assert.Equal("/app", p.Path);
    }

    [Fact]
    public void ParseHttpUrl_defaults_port_for_https()
    {
        var p = DevServer.ParseHttpUrl("https://example.com/foo");
        Assert.Equal("example.com", p.Host);
        Assert.Equal(443, p.Port);
        Assert.Equal("/foo", p.Path);
    }

    [Fact]
    public void ParseHttpUrl_rejects_unsupported_scheme()
    {
        Assert.Throws<DevServerException>(() => DevServer.ParseHttpUrl("ws://localhost/"));
    }

    [Fact]
    public void ParseHttpUrl_rejects_missing_host()
    {
        Assert.Throws<DevServerException>(() => DevServer.ParseHttpUrl("http://:5173/"));
    }

    [Fact]
    public void Start_returns_when_server_is_already_ready()
    {
        using var server = MiniHttpServer.Start();
        using var handle = DevServer.Start(new DevServerOptions
        {
            Url = server.Url,
            TimeoutMs = 5_000,
        });
        Assert.Equal(server.Url, handle.Url);
    }

    [Fact]
    public void Start_throws_when_server_never_responds()
    {
        var ex = Assert.Throws<DevServerException>(() => DevServer.Start(new DevServerOptions
        {
            Url = "http://127.0.0.1:1/",   // port 1 should refuse connections.
            TimeoutMs = 250,
        }));
        Assert.Contains("did not become ready", ex.Message);
    }

    private sealed class MiniHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        public string Url { get; }

        private MiniHttpServer(TcpListener listener, string url)
        {
            _listener = listener;
            Url = url;
            _ = Task.Run(AcceptLoop);
        }

        public static MiniHttpServer Start()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            return new MiniHttpServer(listener, $"http://127.0.0.1:{port}/");
        }

        private async Task AcceptLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
                catch { return; }
                _ = Task.Run(() => HandleClient(client));
            }
        }

        private static async Task HandleClient(TcpClient client)
        {
            using (client)
            {
                using var stream = client.GetStream();
                var buf = new byte[1024];
                try { _ = await stream.ReadAsync(buf); } catch { }
                var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
                try { await stream.WriteAsync(response); } catch { }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
            _cts.Dispose();
        }
    }
}
