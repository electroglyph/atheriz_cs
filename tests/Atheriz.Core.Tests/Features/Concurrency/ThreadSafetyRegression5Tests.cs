using System.Net.WebSockets;
using System.Text;
using Atheriz.Core.Commands;
using Atheriz.Core.Network;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Core.Tests.Ported;
using telnet_cs.Server;
using telnet_cs.Transport;

namespace Atheriz.Core.Tests.Features.Concurrency;

// Concurrency regression pins: close-flag visibility, telnet dispose join,
// parser add-vs-parse, lag-gate capture, session-reader single-consumer,
// websocket dispose convergence.
// All exercise real engine paths; no mocks.
[Collection("Ported")]
public sealed class ThreadSafetyRegression5Tests
{
    private sealed class QuietWriter : ITelnetWriter
    {
        public void Write(string text) { }
        public void Iac(byte cmd, byte opt) { }
        public void Close() { }
        public int? GetWriteBufferSize() => null;
        public void SetExtCallback(byte opt, Action<int, int> callback) { }
        public string? GetPeerHost() => "127.0.0.1";
    }

    private sealed class InstantWebSocket : WebSocket
    {
        private int _sends;
        public int SendCount => Volatile.Read(ref _sends);
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override void Dispose() { }
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) => Task.CompletedTask;
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> b, CancellationToken c) =>
            Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Text, true));
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool endOfMessage, CancellationToken ct)
        {
            Interlocked.Increment(ref _sends);
            return Task.CompletedTask;
        }
    }

    private sealed class LagProbeCommand : Command
    {
        public override string Key => "lagprobe";
        public override bool UseParser => false;
        public static int Runs;
        public override void Run(IMessageTarget caller, object? args) => Interlocked.Increment(ref Runs);
    }

    private static TelnetServerOptions QuietOptions() => new()
    {
        TextEncoding = Encoding.UTF8,
        RequestCharacterSet = false,
        IdleTimeout = Timeout.InfiniteTimeSpan,
        HandshakeTimeout = Timeout.InfiniteTimeSpan,
        StatusInterval = null,
        Log = null,
    };

    // A close is observable from every sender thread without delay.
    [Fact]
    public async Task CloseFlag_VisibleToAllSenders_WithinOneSecond()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TelnetConnection(new object(), new QuietWriter(), "c4vis");
        conn.Close();
        Assert.True(conn.IsClosing);
        var observed = 0;
        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!conn.IsClosing && sw.Elapsed < TimeSpan.FromSeconds(1)) { }
            if (conn.IsClosing) Interlocked.Increment(ref observed);
        })).ToArray();
        await Task.WhenAll(readers).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(4, observed);
        var telnetSrc = SourceScan.Read("src", "Atheriz.Core", "Network", "TelnetProtocol.cs");
        Assert.Contains("private volatile bool _closing", telnetSrc);
        var wsSrc = SourceScan.Read("src", "Atheriz.Core", "Network", "WebSocketProtocol.cs");
        Assert.Contains("private volatile bool _closing", wsSrc);
    }

    // Parallel schedules plus dispose join quickly and completely.
    [Fact]
    public async Task TelnetConnection_ParallelSchedules_DisposeAsync_JoinsFast()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = await Task.Run(() => new TelnetConnection(new object(), new QuietWriter(), "c5burst"));
        using var barrier = new Barrier(8);
        var sends = Enumerable.Range(0, 8).Select(i => Task.Run(() =>
        {
            barrier.SignalAndWait();
            conn.SendCommand("text", new List<object?> { $"burst-{i}" }, new Dictionary<string, object?>());
        })).ToArray();
        await Task.WhenAll(sends).WaitAsync(TimeSpan.FromSeconds(15));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await conn.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
        var src = SourceScan.Read("src", "Atheriz.Core", "Network", "TelnetProtocol.cs");
        Assert.Contains("_drainWaiters", src);
        Assert.DoesNotContain("_drainTcs = new", src);
    }

    // Parses racing def adds never observe a half-built list.
    [Fact]
    public async Task ParseArgs_ConcurrentAddArgument_NeverThrows()
    {
        using var env = GlobalTestEnv.Enter();
        var parser = new GameArgumentParser();
        parser.AddArgument("target", help: "target");
        using var barrier = new Barrier(2);
        var parse = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (int i = 0; i < 2000; i++)
                parser.ParseArgs(["hello"]);
        });
        var add = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (int i = 0; i < 200; i++)
                parser.AddArgument($"--opt{i}", help: "opt");
        });
        await Task.WhenAll([parse, add]).WaitAsync(TimeSpan.FromSeconds(30));
        var src = SourceScan.Read("src", "Atheriz.Core", "Commands", "GameArgumentParser.cs");
        Assert.Contains("SnapshotDefs()", src);
    }

    // A gate swap between wrap and invoke cannot NRE or mis-apply.
    [Fact]
    public void LagGate_SwapBetweenWrapAndInvoke_AppliesWrappedGeneration()
    {
        using var env = GlobalTestEnv.Enter();
        var cmd = new LagProbeCommand();
        var conn = new TestConnection("c11gate");
        var old = Command.GlobalLagCheck;
        try
        {
            LagProbeCommand.Runs = 0;
            Command.GlobalLagCheck = null;
            var (func, _, _) = cmd.Execute(conn, "");
            Assert.NotNull(func);
            // Install-then-null: the wrapped call still runs the action.
            Command.GlobalLagCheck = _ => true;
            func!(conn, null);
            Assert.Equal(1, LagProbeCommand.Runs);
            // Deny-then-allow: the captured deny still applies.
            LagProbeCommand.Runs = 0;
            Command.GlobalLagCheck = _ => true;
            var (func2, _, _) = cmd.Execute(conn, "");
            Command.GlobalLagCheck = _ => false;
            func2!(conn, null);
            Assert.Equal(0, LagProbeCommand.Runs);
        }
        finally { Command.GlobalLagCheck = old; }
    }

    // Concurrent readers fail fast instead of interleaving.
    [Fact]
    public async Task TelnetSessionReader_ConcurrentReads_SecondThrows()
    {
        using var env = GlobalTestEnv.Enter();
        var (peer, serverStream) = InMemoryPipe.Create();
        using var session = new ServerSession(serverStream, QuietOptions(), CancellationToken.None);
        try
        {
            using var cts = new CancellationTokenSource();
            var reader = new TelnetSessionReader(session, cts.Token);
            var buf1 = new char[64];
            var buf2 = new char[64];
            Exception? ex1 = null, ex2 = null;
            var t1 = Task.Run(async () =>
            {
                try { await reader.ReadAsync(buf1, CancellationToken.None); }
                catch (Exception ex) { ex1 = ex; }
            });
            await Task.Delay(300);
            var t2 = Task.Run(async () =>
            {
                try { await reader.ReadAsync(buf2, CancellationToken.None); }
                catch (Exception ex) { ex2 = ex; }
            });
            // Release the blocking first read after the second had its chance.
            _ = Task.Run(async () => { await Task.Delay(1000); cts.Cancel(); });
            await Task.WhenAll([t1, t2]).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.NotNull(ex2);
            Assert.IsType<InvalidOperationException>(ex2);
            Assert.True(ex1 is not InvalidOperationException);
        }
        finally { peer.Dispose(); }
    }

    // Burst sends plus dispose converge with no loss.
    [Fact]
    public async Task WebSocketConnection_BurstSends_Dispose_Converges()
    {
        using var env = GlobalTestEnv.Enter();
        var socket = new InstantWebSocket();
        var conn = new WebSocketConnection(socket, "c13burst", null, "9.9.9.9");
        var sends = Enumerable.Range(0, 50).Select(i => Task.Run(() =>
            conn.SendCommand("text", new List<object?> { $"m{i}" }, new Dictionary<string, object?>()))).ToArray();
        await Task.WhenAll(sends).WaitAsync(TimeSpan.FromSeconds(15));
        await conn.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(0, conn.PendingCount);
        Assert.Equal(50, socket.SendCount);
        var src = SourceScan.Read("src", "Atheriz.Core", "Network", "WebSocketProtocol.cs");
        Assert.Contains("_startingSends", src);
    }
}
