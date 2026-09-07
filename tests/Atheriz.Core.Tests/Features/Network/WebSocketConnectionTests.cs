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
    public async Task Ws_FallbackSend_IsLogged_NotSwallowed()
    {
        // Behavior: a non-System.Net.WebSockets peer gets FallbackConnection
        // whose SendCommand/Close are empty no-ops — server replies vanish
        // without a trace. The drop must at least be logged.
        using var _ = new WsScope();
        var mgr = PortedHelpers.MakeManager();
        ConnectionManager.GlobalInstance = mgr;
        try
        {
            var app = new FakeWsApp();
            new WebSocketProtocol().Setup(app);
            Assert.NotNull(app.CapturedEndpoint);
            var fake = new FakeWebsocket();
            var hold = new TaskCompletionSource<string>();
            fake.Script.Enqueue(() => Task.FromResult("[\"ping\"]"));
            fake.Script.Enqueue(() => hold.Task);
            var pump = app.CapturedEndpoint!(fake);
            BaseConnection? conn = null;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (conn == null && DateTime.UtcNow < deadline)
            {
                conn = mgr.ConnectionsSnapshot.Values.FirstOrDefault();
                if (conn == null) await Task.Delay(10);
            }
            Assert.NotNull(conn);
            string log;
            using (var cap = new CaptureAtherizLog())
            {
                conn!.SendCommand("text", new List<object?> { "hello" }, null);
                await Task.Delay(50);
                log = cap.Read();
            }
            hold.TrySetException(new InvalidOperationException("test end"));
            await pump.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains("drop", log, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            mgr.Atp.Stop(wait: false);
            ConnectionManager.GlobalInstance = null;
        }
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
