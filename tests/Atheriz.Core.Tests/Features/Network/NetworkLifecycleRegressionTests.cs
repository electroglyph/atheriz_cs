using System.Net.WebSockets;
using System.Text;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Network;

// P1-13 lifecycle regression pins: on-loop inline delivery, dispose drain,
// failed-send limiter release, disconnect teardown wait, ReadCappedLines parity.
[Collection("Ported")]
public class NetworkLifecycleRegressionTests
{
    private sealed class GateWriter : ITelnetWriter
    {
        public readonly List<string> Writes = new();
        public readonly ManualResetEventSlim Gate = new(true);
        public string? Host = "10.13.37.1";
        public void Write(string text) { Gate.Wait(); lock (Writes) Writes.Add(text); }
        public void Iac(byte cmd, byte opt) { }
        public void Close() { }
        public int? GetWriteBufferSize() => 0;
        public void SetExtCallback(byte opt, Action<int, int> cb) { }
        public string? GetPeerHost() => Host;
    }

    private sealed class ScriptedWs : WebSocket
    {
        public readonly List<string> Sent = new();
        public bool ShouldThrow;
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override void Dispose() { }
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) => Task.CompletedTask;
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct) =>
            Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Text, true));
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool endOfMessage, CancellationToken ct)
        {
            if (ShouldThrow) throw new InvalidOperationException("boom");
            Sent.Add(Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count));
            return Task.CompletedTask;
        }
    }

    private sealed class SignallingSession : Session
    {
        public readonly ManualResetEventSlim Done = new(false);
        public SignallingSession() : base(null) { }
        public override void AtDisconnect() { Thread.Sleep(200); Done.Set(); }
    }

    private sealed class OneCharReader : TextReader
    {
        private readonly string _s;
        private int _pos;
        public OneCharReader(string s) => _s = s;
        public override int Read(char[] buffer, int index, int count)
        {
            if (_pos >= _s.Length) return 0;
            buffer[index] = _s[_pos++];
            return 1;
        }
    }

    [Fact]
    public void TelnetSend_OnLoopThread_DeliversInline()
    {
        // P1-13 T2 (reverted, tests-win): on the loop thread, sends commit inline
        // like asyncio transport.write — offload happens only on worker threads.
        using var env = GlobalTestEnv.Enter();
        var writer = new GateWriter();
        var conn = new TelnetConnection(new object(), writer) { ClientHost = "10.13.37.1" };
        try
        {
            Assert.True(conn.IsOnLoopThread(), "test thread must be the loop thread for this pin");
            conn.SendCommand("text", new List<object?> { "hello" });
            lock (writer.Writes) Assert.Single(writer.Writes);
            Assert.Equal("hello", writer.Writes[0]);
        }
        finally { conn.Dispose(); }
    }

    [Fact]
    public void TelnetDispose_DrainsInflightWrites()
    {
        // P1-13 T3: Dispose waits (bounded) for scheduled off-loop writes.
        using var env = GlobalTestEnv.Enter();
        var writer = new GateWriter();
        writer.Gate.Reset();
        var conn = new TelnetConnection(new object(), writer) { ClientHost = "10.13.37.1" };
        Task? disposeTask = null;
        try
        {
            // Send from a worker thread so the write is scheduled, not inline.
            var sendTask = Task.Run(() => conn.SendCommand("text", new List<object?> { "drain-me" }));
            var inflight = typeof(TelnetConnection).GetField("_inflight",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            Assert.True(SpinWait.SpinUntil(() => (int)inflight.GetValue(conn)! == 1, 5000),
                "off-loop write must go inflight (blocked on the gate)");
            disposeTask = Task.Run(() => conn.Dispose());
            Assert.False(disposeTask.Wait(TimeSpan.FromMilliseconds(150)),
                "Dispose must wait for inflight writes instead of tearing down");
            writer.Gate.Set();
            Assert.True(disposeTask.Wait(TimeSpan.FromSeconds(5)), "Dispose must finish after the write drains");
            Assert.True(sendTask.Wait(TimeSpan.FromSeconds(5)));
            lock (writer.Writes) Assert.Single(writer.Writes);
            Assert.Equal("drain-me", writer.Writes[0]);
        }
        finally { writer.Gate.Set(); disposeTask?.Wait(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public void WsFailedSend_ReleasesReservationAndStaysUsable()
    {
        // P1-13 W1: a failed send must not leak the limiter slot or break later sends.
        using var env = GlobalTestEnv.Enter();
        var ws = new ScriptedWs { ShouldThrow = true };
        var conn = new WebSocketConnection(ws, clientHost: "10.10.10.10");
        try
        {
            conn.SendCommand("text", new List<object?> { "boom" }); // must not throw
            Assert.True(SpinWait.SpinUntil(() => conn.PendingCount == 0, 5000),
                "failed send must release its limiter reservation");
            ws.ShouldThrow = false;
            conn.SendCommand("text", new List<object?> { "again" });
            Assert.True(SpinWait.SpinUntil(() => ws.Sent.Count == 1, 5000), "connection must stay usable");
            Assert.Contains("again", ws.Sent[0]);
        }
        finally { conn.Dispose(); }
    }

    [Fact]
    public void Disconnect_DoesNotBlockOnSessionTeardown()
    {
        // P1-13 C1 (reverted): Disconnect is fire-and-forget — it returns promptly
        // even with a slow teardown, and the scheduled teardown still runs async.
        // Pins the same contract as DisconnectDoesNotBlockOnSlowTeardown.
        using var env = GlobalTestEnv.Enter();
        var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100);
        var mgr = new ConnectionManager(pool: pool, settings: new AtherizSettings());
        try
        {
            var conn = new TelnetConnection(new object(), new GateWriter()) { ClientHost = "10.10.10.10" };
            var session = new SignallingSession();
            typeof(BaseConnection).GetField("<Session>k__BackingField",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(conn, session);
            Assert.True(mgr.RegisterConnection("test-conn", conn));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            mgr.Disconnect(conn);
            sw.Stop();
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(0.5), "Disconnect must not block on the 200ms teardown");
            Assert.True(session.Done.Wait(TimeSpan.FromSeconds(5)), "scheduled teardown must still run");
        }
        finally { pool.Stop(wait: false); }
    }

    [Fact]
    public async Task ReadCappedLines_ChunkedReads_MatchBulkReads()
    {
        // P1-13 T5: the head-offset rewrite must be byte-identical for any chunking.
        var sb = new StringBuilder();
        for (int i = 0; i < 200; i++) sb.Append("line-").Append(i.ToString("D3")).Append(i % 7 == 6 ? "\r\n" : "\n");
        string text = sb.ToString();
        var bulk = new List<string?>();
        await foreach (var line in TelnetProtocol.ReadCappedLines(new StringReader(text), 4096)) bulk.Add(line);
        var chunked = new List<string?>();
        await foreach (var line in TelnetProtocol.ReadCappedLines(new OneCharReader(text), 4096)) chunked.Add(line);
        Assert.Equal(200, bulk.Count);
        Assert.Equal(bulk, chunked);
    }
}
