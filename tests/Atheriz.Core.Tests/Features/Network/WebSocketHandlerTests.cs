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
    [Fact]
    public async Task Ws_OversizeMessage_ClosesTooBig_WithoutDispatch()
    {
        // Size gate on the real pump: an oversize message must close with
        // MessageTooBig and never reach dispatch (no sends on the socket).
        var mgr = PortedHelpers.MakeManager();
        ConnectionManager.GlobalInstance = mgr;
        try
        {
            var payload = new byte[200];
            Array.Fill(payload, (byte)'x');
            var fake = new FragmentSocket(payload, fragment: 8192);
            var settings = new AtherizSettings { WebsocketMaxMessageSize = 100 };
            var http = new DefaultHttpContext();
            http.Connection.RemoteIpAddress = IPAddress.Loopback;
            http.Features.Set<IHttpWebSocketFeature>(new StaticWsFeature(fake));
            await WebSocketHandler.HandleAsync(http, settings).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(WebSocketCloseStatus.MessageTooBig, fake.CloseStatus);
            Assert.Empty(fake.SentBytes);
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
        public readonly List<int> SentBytes = new();
        public FragmentSocket(byte[] data, int fragment) { _data = data; _fragment = fragment; }
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken ct)
        {
            _closeStatus = status;
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }
        public override Task SendAsync(ArraySegment<byte> b, WebSocketMessageType t, bool e, CancellationToken c)
        {
            SentBytes.Add(b.Count);
            return Task.CompletedTask;
        }
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

    private sealed class DisposalTrackingSocket : ScriptedWebSocket
    {
        public bool Disposed { get; private set; }
        public override void Dispose() { Disposed = true; }
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken ct)
        {
            _closeStatus = status;
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }
        public override Task SendAsync(ArraySegment<byte> b, WebSocketMessageType t, bool e, CancellationToken c) =>
            Task.CompletedTask;
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct) =>
            Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
    }

    [Fact]
    public async Task Ws_NullGlobalManager_DisposesSocket()
    {
        // Refusal must dispose the socket, not just close it, or the
        // socket and FD leak until GC.
        ConnectionManager.GlobalInstance = null;
        try
        {
            var fake = new DisposalTrackingSocket();
            var settings = new AtherizSettings { WebsocketMaxMessageSize = 100_000 };
            var http = new DefaultHttpContext();
            http.Connection.RemoteIpAddress = IPAddress.Loopback;
            http.Features.Set<IHttpWebSocketFeature>(new StaticWsFeature(fake));
            await WebSocketHandler.HandleAsync(http, settings).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(WebSocketCloseStatus.InternalServerError, fake.CloseStatus);
            Assert.True(fake.Disposed);
        }
        finally
        {
            ConnectionManager.GlobalInstance = null;
        }
    }

    [Fact]
    public async Task Ws_CappedManager_DisposesConnection()
    {
        // Same disposal requirement on the register-refused path when the
        // connection hits the total-connection cap.
        var settings = new AtherizSettings { MaxTotalConnections = 1 };
        var mgr = PortedHelpers.MakeManager(settings);
        ConnectionManager.GlobalInstance = mgr;
        try
        {
            Assert.True(mgr.RegisterConnection("ws-cap-fill", new TestConnection("ws-cap-fill") { ClientHost = "10.0.0.1" }));
            var fake = new DisposalTrackingSocket();
            var http = new DefaultHttpContext();
            http.Connection.RemoteIpAddress = IPAddress.Loopback;
            http.Features.Set<IHttpWebSocketFeature>(new StaticWsFeature(fake));
            await WebSocketHandler.HandleAsync(http, settings).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(fake.Disposed);
        }
        finally
        {
            mgr.Atp.Stop(wait: false);
            ConnectionManager.GlobalInstance = null;
        }
    }
}
