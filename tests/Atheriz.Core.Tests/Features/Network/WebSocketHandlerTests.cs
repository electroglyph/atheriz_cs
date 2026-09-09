using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using Atheriz.Core.Network;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;
using Atheriz.Server.Cli;
using Atheriz.Server.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Network;

// WebSocket message handling: per-message size gate and fragmented streaming limits.
[Collection("Ported")]
public class WebSocketHandlerTests
{
    // --- WebSocket pump via mock app (decorator pattern, no server) ---

    private sealed class FakeWsApp : IWebSocketApp
    {
        // Typed app surface: Setup registers the endpoint directly.
        public Func<IWebSocketPeer, Task>? CapturedEndpoint;
        public void WebSocket(string path, Func<IWebSocketPeer, Task> endpoint) => CapturedEndpoint += endpoint;
    }

    public sealed class FakeWsClient : IWebSocketClientInfo
    {
        public string host = "9.9.9.9";
        string? IWebSocketClientInfo.Host => host;
    }

    public sealed class FakeWebsocket : IWebSocketPeer
    {
        public readonly FakeWsClient client = new();
        public readonly Queue<Func<Task<string>>> Script = new();
        public readonly List<object?> Closes = new();
        public Task accept() => Task.CompletedTask;
        public Task<string> receive_text() => Script.Dequeue()();
        public Task close() { Closes.Add("close()"); return Task.CompletedTask; }
        public Task close(int code) { Closes.Add(code); return Task.CompletedTask; }
        public Task close(int code, string? reason) { Closes.Add((code, reason)); return Task.CompletedTask; }
        object? IWebSocketPeer.Client => client;
        Task IWebSocketPeer.AcceptAsync() => accept();
        Task<string> IWebSocketPeer.ReceiveTextAsync() => receive_text();
        Task IWebSocketPeer.CloseAsync(int code, string? reason) => close(code);
    }

    private sealed class WsScope : IDisposable
    {
        public bool PrevEnabled;
        public int PrevMax;
        public WsScope()
        {
            PrevEnabled = AtherizSettings.Global.WebsocketEnabled;
            PrevMax = AtherizSettings.Global.WebsocketMaxMessageSize;
            AtherizSettings.Global.WebsocketEnabled = true;
        }
        public void Dispose()
        {
            AtherizSettings.Global.WebsocketEnabled = PrevEnabled;
            AtherizSettings.Global.WebsocketMaxMessageSize = PrevMax;
        }
    }

    [Fact]
    public async Task Ws_OversizeMessage_Closes1009_WithoutDispatch()
    {
        // Pin documenting the per-message size gate (websocket.py:181-192):
        // an oversize message must close with 1009 and never reach dispatch.
        using var _ = new WsScope();
        AtherizSettings.Global.WebsocketMaxMessageSize = 100;
        var mgr = PortedHelpers.MakeManager();
        ConnectionManager.GlobalInstance = mgr;
        try
        {
            var app = new FakeWsApp();
            new WebSocketProtocol().Setup(app);
            Assert.NotNull(app.CapturedEndpoint);
            var fake = new FakeWebsocket();
            fake.Script.Enqueue(() => Task.FromResult(new string('x', 200)));
            await app.CapturedEndpoint!(fake);
            Assert.Contains(1009, fake.Closes);
        }
        finally
        {
            mgr.Atp.Stop(wait: false);
            ConnectionManager.GlobalInstance = null;
        }
    }

    // --- Fragmented-accumulation working set (Kestrel pump path) ---

    // Scripted System.Net.WebSockets.WebSocket fakes: HttpListener does not
    // function in this sandbox (connect hangs), while raw TCP loopback works.
    // These fakes drive the real pump/close paths deterministically.
    private abstract class ScriptedWebSocket : WebSocket
    {
        protected WebSocketState _state = WebSocketState.Open;
        protected WebSocketCloseStatus? _closeStatus;
        public override WebSocketCloseStatus? CloseStatus => _closeStatus;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;
        public override void Abort() { _state = WebSocketState.Aborted; }
        public override void Dispose() { }
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) =>
            CloseAsync(status, description, ct);
    }

    // Peer scripted with inbound fragments; records the close handshake.
    private sealed class FragmentSocket : ScriptedWebSocket
    {
        private readonly byte[] _data;
        private readonly int _fragment;
        private int _offset;
        public FragmentSocket(byte[] data, int fragment) { _data = data; _fragment = fragment; }
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken ct)
        {
            _closeStatus = status;
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }
        public override Task SendAsync(ArraySegment<byte> b, WebSocketMessageType t, bool e, CancellationToken c) =>
            Task.CompletedTask;
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct)
        {
            if (_offset >= _data.Length)
                return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
            int n = Math.Min(_fragment, _data.Length - _offset);
            Buffer.BlockCopy(_data, _offset, buffer.Array!, buffer.Offset, n);
            _offset += n;
            bool end = _offset >= _data.Length;
            return Task.FromResult(new WebSocketReceiveResult(n, WebSocketMessageType.Binary, end));
        }
    }

    private sealed class StaticWsFeature : IHttpWebSocketFeature
    {
        private readonly WebSocket _ws;
        public StaticWsFeature(WebSocket ws) => _ws = ws;
        public bool IsWebSocketRequest => true;
        public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context) => Task.FromResult(_ws);
    }

    [Fact]
    public async Task Ws_FragmentedOversize_StreamsWithoutAccumulating()
    {
        // Behavior: the size gate must stream — appending every fragment to a
        // MemoryStream and checking the size only after EndOfMessage lets a
        // 3 MB fragmented message allocate payload + ToArray + string
        // (~12 MB) before rejection. A streaming gate must reject with only
        // fragment-sized working set.
        // Drives the REAL pump with scripted fragments (no network at all);
        // the 20x margin absorbs background GC noise.
        var mgr = PortedHelpers.MakeManager();
        ConnectionManager.GlobalInstance = mgr;
        try
        {
            var payload = new byte[3_000_000];
            Array.Fill(payload, (byte)'x');
            var fake = new FragmentSocket(payload, fragment: 8192);
            var settings = new AtherizSettings { WebsocketMaxMessageSize = 100_000 };
            var http = new DefaultHttpContext();
            http.Connection.RemoteIpAddress = IPAddress.Loopback;
            http.Features.Set<IHttpWebSocketFeature>(new StaticWsFeature(fake));

            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            long before = GC.GetTotalMemory(true);
            await WebSocketHandler.HandleAsync(http, settings).WaitAsync(TimeSpan.FromSeconds(15));
            long after = GC.GetTotalMemory(true);
            // Rejection itself works (pin half): oversize closes MessageTooBig.
            Assert.Equal(WebSocketCloseStatus.MessageTooBig, fake.CloseStatus);
            Assert.True(after - before < 600_000,
                $"fragmented oversize must stream, allocated {after - before} bytes for a 3 MB message");
        }
        finally
        {
            mgr.Atp.Stop(wait: false);
            ConnectionManager.GlobalInstance = null;
        }
    }

    [Fact]
    public async Task Ws_NullGlobalManager_ClosesWithoutDispatch()
    {
        // Fail closed: with no global manager the socket is refused, never
        // served from a private throwaway world (uncounted, no broadcasts).
        ConnectionManager.GlobalInstance = null;
        try
        {
            var fake = new FragmentSocket(new byte[] { (byte)'x' }, fragment: 1);
            var settings = new AtherizSettings { WebsocketMaxMessageSize = 100_000 };
            var http = new DefaultHttpContext();
            http.Connection.RemoteIpAddress = IPAddress.Loopback;
            http.Features.Set<IHttpWebSocketFeature>(new StaticWsFeature(fake));
            await WebSocketHandler.HandleAsync(http, settings).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(WebSocketCloseStatus.InternalServerError, fake.CloseStatus);
        }
        finally
        {
            ConnectionManager.GlobalInstance = null;
        }
    }
}
