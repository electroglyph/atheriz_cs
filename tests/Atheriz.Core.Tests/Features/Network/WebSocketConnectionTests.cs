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

// WebSocket connection behavior: fallback-peer send logging and close-handshake deadlines.
[Collection("Ported")]
public class WebSocketConnectionTests
{
    [Fact]
    public async Task Ws_SendCommand_LimiterSettlesToZero()
    {
        // Sole-accounting invariant: every reservation is released exactly
        // once, so after the async send finishes no bytes/counts linger
        // (the other half of the telnet-leak and double-subtract fixes).
        var sock = new RecordingSocket();
        var conn = new WebSocketConnection(sock, sessionId: "x", clientHost: "?");
        try
        {
            conn.SendCommand("text", new List<object?> { "hello" }, new Dictionary<string, object?> { ["k"] = "v" });
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (conn.PendingCount != 0 && DateTime.UtcNow < deadline)
                await Task.Delay(10);
            Assert.Equal(0, conn.PendingCount);
            Assert.Equal(0, conn.PendingBytes);
            Assert.NotEmpty(sock.SentBytes);
        }
        finally { try { conn.Dispose(); } catch { } }
    }

    private sealed class RecordingSocket : System.Net.WebSockets.WebSocket
    {
        public readonly List<int> SentBytes = new();
        public override System.Net.WebSockets.WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override System.Net.WebSockets.WebSocketState State => System.Net.WebSockets.WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override void Dispose() { }
        public override Task CloseAsync(System.Net.WebSockets.WebSocketCloseStatus s, string? d, CancellationToken c) => Task.CompletedTask;
        public override Task CloseOutputAsync(System.Net.WebSockets.WebSocketCloseStatus s, string? d, CancellationToken c) => Task.CompletedTask;
        public override Task SendAsync(ArraySegment<byte> b, System.Net.WebSockets.WebSocketMessageType t, bool e, CancellationToken c)
        {
            SentBytes.Add(b.Count);
            return Task.CompletedTask;
        }
        public override Task<System.Net.WebSockets.WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> b, CancellationToken c) =>
            Task.FromResult(new System.Net.WebSockets.WebSocketReceiveResult(0, System.Net.WebSockets.WebSocketMessageType.Close, true));
    }

    // --- Close handshake against a hanging peer (no network) ---

    // Scripted System.Net.WebSockets.WebSocket fakes: HttpListener does not
    // function in this sandbox (connect hangs), while raw TCP loopback works.
    // These fakes drive the real pump/close paths deterministically.
    private abstract class ScriptedWebSocket : WebSocket
    {
        protected WebSocketState _state = WebSocketState.Open;
        // No close is ever completed against these fakes in this file, so the
        // status stays null (the sibling handler tests drive real closes).
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;
        public override void Abort() { _state = WebSocketState.Aborted; }
        public override void Dispose() { }
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) =>
            CloseAsync(status, description, ct);
    }

    // Peer that stays open but never answers the close handshake.
    private sealed class BlackholeWebSocket : ScriptedWebSocket
    {
        // Mirrors a real hung peer: the task only ends when the caller gives
        // up (cancellation), surfacing OperationCanceledException like the
        // real CloseAsync does on abort.
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) =>
            Task.Delay(Timeout.InfiniteTimeSpan, ct);
        public override Task SendAsync(ArraySegment<byte> b, WebSocketMessageType t, bool e, CancellationToken c) =>
            Task.CompletedTask;
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> b, CancellationToken c) =>
            Task.Delay(Timeout.InfiniteTimeSpan, c).ContinueWith(_ => new WebSocketReceiveResult(0, WebSocketMessageType.Binary, false), TaskScheduler.Default);
    }

    [Fact]
    public void Ws_Close_ToBlackholePeer_CompletesPromptly()
    {
        // Behavior: closing toward a peer that stays open but never answers
        // the close handshake must settle promptly (bounded deadline + abort)
        // instead of hanging forever.
        var blackhole = new BlackholeWebSocket();
        var serverConn = new WebSocketConnection(blackhole, "blackhole", null, "127.0.0.1");
        try
        {
            // Close() itself is fire-and-forget by design; what must settle
            // is the socket: poll its state past the bounded deadline.
            var closeTask = Task.Run(() => serverConn.Close());
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while ((blackhole.State == WebSocketState.Open || blackhole.State == WebSocketState.CloseSent)
                   && DateTime.UtcNow < deadline)
                Thread.Sleep(20);
            // Settled = the socket left the handshake (Closed after a clean
            // handshake, Aborted past the bounded deadline — either way it
            // does not hang forever).
            bool settled = blackhole.State != WebSocketState.Open
                && blackhole.State != WebSocketState.CloseSent;
            string fault = closeTask.IsFaulted ? closeTask.Exception?.ToString() ?? "faulted" : "no-fault";
            Assert.True(settled,
                $"close to blackhole peer must settle promptly (state={blackhole.State}, fault={fault})");
        }
        finally { try { serverConn.Dispose(); } catch { } }
    }
}
