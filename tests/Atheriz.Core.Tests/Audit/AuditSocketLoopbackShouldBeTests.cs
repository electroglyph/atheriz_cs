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

namespace Atheriz.Core.Tests.Audit;

// Should-be tests for audit §2 B29 (WebSocket send/close fragility), B30
// (Telnet parsing/write), B32 (BaseConnection leaks/retry) and B33 (CLI/host).
// Fails while the bug is present, passes once fixed. Production untouched.
//
// Determinism note: loopback tests use localhost TCP only, every wait is
// bounded (a hang manifests as a red assertion, never a hung suite).
[Collection("Ported")]
public class AuditSocketLoopbackShouldBeTests
{
    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    // --- B29: WebSocket pump via mock app (decorator pattern, no server) ---

    private sealed class FakeWsApp
    {
        // Public dynamic surface: the pump binds via DLR + reflection
        // fallbacks, which need an accessible peer type (as with real peers).
        public Func<dynamic, Task>? CapturedEndpoint;
        public readonly Func<string, Action<Func<dynamic, Task>>> websocket;
        public FakeWsApp() { websocket = _ => ep => { CapturedEndpoint += ep; }; }
    }

    public sealed class FakeWsClient
    {
        public string host = "9.9.9.9";
    }

    public sealed class FakeWebsocket
    {
        public readonly FakeWsClient client = new();
        public readonly Queue<Func<Task<string>>> Script = new();
        public readonly List<object?> Closes = new();
        public Task accept() => Task.CompletedTask;
        public Task<string> receive_text() => Script.Dequeue()();
        public Task close() { Closes.Add("close()"); return Task.CompletedTask; }
        public Task close(int code) { Closes.Add(code); return Task.CompletedTask; }
        public Task close(int code, string? reason) { Closes.Add((code, reason)); return Task.CompletedTask; }
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

    [Fact]
    public async Task Ws_FallbackSend_IsLogged_NotSwallowed()
    {
        // audit B29: a non-System.Net.WebSockets peer gets FallbackConnection
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

    [Fact]
    public async Task Ws_Close_ToBlackholePeer_CompletesPromptly()
    {
        // audit B29: LockedSendAsync/CloseAsync use CancellationToken.None, so
        // closing toward a peer that stays open but never answers the close
        // handshake hangs forever. Close must settle (one way or another).
        var listener = new HttpListener();
        int port = FreePort();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/ws/");
        listener.Start();
        ClientWebSocket? client = null;
        WebSocket? serverWs = null;
        try
        {
            var acceptTask = listener.GetContextAsync();
            client = new ClientWebSocket();
            await client.ConnectAsync(new Uri($"http://127.0.0.1:{port}/ws/".Replace("http://", "ws://")), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            var ctx = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
            serverWs = (await ctx.AcceptWebSocketAsync(subProtocol: null).WaitAsync(TimeSpan.FromSeconds(5))).WebSocket;
            var serverConn = new WebSocketConnection(serverWs, "blackhole", null, "127.0.0.1");
            var closeTask = Task.Run(() => serverConn.Close());
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            while ((serverWs.State == WebSocketState.Open || serverWs.State == WebSocketState.CloseSent)
                   && DateTime.UtcNow < deadline)
                await Task.Delay(50);
            bool settled = closeTask.IsCompleted && serverWs.State == WebSocketState.Closed;
            Assert.True(settled,
                $"close to blackhole peer must settle promptly (taskDone={closeTask.IsCompleted}, state={serverWs.State})");
        }
        finally
        {
            try { client?.Abort(); } catch { }
            try { serverWs?.Abort(); } catch { }
            try { client?.Dispose(); } catch { }
            try { serverWs?.Dispose(); } catch { }
            try { listener.Stop(); } catch { }
        }
    }

    // --- B30: Telnet parsing/write without sockets where possible ---

    private sealed class FaultReader : System.IO.TextReader
    {
        public override Task<int> ReadAsync(char[] buffer, int index, int count) =>
            throw new IOException("boom");
    }

    [Fact]
    public async Task Telnet_FaultingReader_SurfacesError_NotCleanEof()
    {
        // audit B30: catch{read=0} converts ANY read error into clean EOF, so
        // a broken transport looks like a graceful disconnect.
        await Assert.ThrowsAsync<IOException>(async () =>
        {
            await foreach (var _ in TelnetProtocol.ReadCappedLines(new FaultReader(), 100)) { }
        });
    }

    [Fact]
    public async Task Telnet_OverlongLine_YieldsNullThenRecovers()
    {
        // Pin: an overlong line is reported as null (dropped) and the stream
        // continues with the next line.
        var lines = new List<string?>();
        await foreach (var l in TelnetProtocol.ReadCappedLines(
            new System.IO.StringReader("ok\n" + new string('y', 200) + "\nrecovered\n"), 100))
            lines.Add(l);
        Assert.Equal(new string?[] { "ok", null, "recovered" }, lines);
    }

    [Fact]
    public void Telnet_Write_ToNonReadingPeer_ReturnsPromptly()
    {
        // audit B30: synchronous _stream.Write on the game thread with no
        // timeout — a peer that never drains stalls the worker forever.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        TcpClient? server = null;
        TcpClient? client = null;
        try
        {
            client = new TcpClient();
            client.Connect(IPAddress.Loopback, port);
            server = listener.AcceptTcpClient();
            var writer = new TelnetStreamWriter(server.GetStream(), server);
            var big = new string('x', 8 << 20);
            var writeTask = Task.Run(() => writer.Write(big));
            bool done = writeTask.Wait(TimeSpan.FromSeconds(3));
            Assert.True(done, "write to a non-draining peer must not block the game thread indefinitely");
        }
        finally
        {
            try { server?.Close(); } catch { }
            try { client?.Close(); } catch { }
            try { listener.Stop(); } catch { }
        }
    }

    // --- B32: connection pool/queue lifecycle ---

    [Fact]
    public void BaseConnection_ImplementsIDisposable()
    {
        // audit B32: connections own queues/sessions (subclasses add
        // SemaphoreSlim/TcpClient/WebSocket) but nothing is disposable.
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(BaseConnection)));
    }

    [Fact]
    public void DroppedInput_IsRetriedAfterPoolFrees()
    {
        // Pin: pool-full drops schedule RetryDrain chains (50 ms) that deliver
        // once capacity returns — no silent loss on transient pressure.
        var mgr = PortedHelpers.MakeManager();
        ConnectionManager.GlobalInstance = mgr;
        try
        {
            mgr.Atp.SetQueueLimitForTesting(0);
            var conn = new TestConnection();
            var tcs = new TaskCompletionSource<bool>();
            conn.EnqueueInput(
                new Action<BaseConnection, List<object?>, Dictionary<string, object?>>((c, a, k) => tcs.TrySetResult(true)),
                new List<object?>(), new Dictionary<string, object?>());
            mgr.Atp.SetQueueLimitForTesting(10000);
            Assert.True(tcs.Task.Wait(TimeSpan.FromSeconds(2)), "dropped input was never retried");
        }
        finally
        {
            mgr.Atp.Stop(wait: false);
            ConnectionManager.GlobalInstance = null;
        }
    }

    // --- B33: CLI/host ---

    [Fact]
    public async Task KillProcess_DoesTerminate()
    {
        // audit B33: KillProcessWithDots never kills (waits ~5 s and returns).
        using var p = Process.Start("sleep", "30");
        Assert.NotNull(p);
        try
        {
            await ProcessHelper.KillProcessWithDots(p).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(p.HasExited, "KillProcessWithDots must terminate the process");
        }
        finally
        {
            try { if (!p.HasExited) p.Kill(); } catch { }
            try { p.WaitForExit(2000); } catch { }
        }
    }

    [Fact]
    public void PidFile_DetectsLoopbackListener()
    {
        // Pin: port-listen detection works both ways.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            Assert.True(PidFile.TryFindPidListeningOnPort(port, out int pid));
            Assert.Equal(Environment.ProcessId, pid);
        }
        finally { listener.Stop(); }
        Assert.False(PidFile.TryFindPidListeningOnPort(port, out _));
    }

    [Fact]
    public void GameTemplate_InvalidName_SaysCSharp_NotPython()
    {
        // audit §7: the C# generator rejects names as "not a valid Python
        // identifier". Must name the actual language.
        var sw = new StringWriter();
        var orig = Console.Out;
        Console.SetOut(sw);
        try { GameTemplateGenerator.CreateGameFolder("my-game"); }
        finally { Console.SetOut(orig); }
        var text = sw.ToString();
        Assert.DoesNotContain("Python identifier", text);
    }

    // --- B29: fragmented-accumulation working set (Kestrel pump path) ---

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
        // audit B29: HandleAsync appends every fragment to a MemoryStream and
        // checks the size only after EndOfMessage — a 3 MB fragmented message
        // allocates payload + ToArray + string (~12 MB) before rejection. A
        // streaming gate must reject with only fragment-sized working set.
        // Drives the REAL pump via DefaultHttpContext + injected socket
        // feature (no Kestrel needed); measures process heap, so the 20x
        // margin absorbs background noise.
        var listener = new HttpListener();
        int port = FreePort();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/ws/");
        listener.Start();
        var mgr = PortedHelpers.MakeManager();
        ConnectionManager.GlobalInstance = mgr;
        ClientWebSocket? client = null;
        WebSocket? serverWs = null;
        try
        {
            var acceptTask = listener.GetContextAsync();
            client = new ClientWebSocket();
            await client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws/"), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            var ctx = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
            serverWs = (await ctx.AcceptWebSocketAsync(subProtocol: null).WaitAsync(TimeSpan.FromSeconds(5))).WebSocket;

            var settings = new AtherizSettings { WebsocketMaxMessageSize = 100_000 };
            var http = new DefaultHttpContext();
            http.Connection.RemoteIpAddress = IPAddress.Loopback;
            http.Features.Set<IHttpWebSocketFeature>(new StaticWsFeature(serverWs));
            var payload = new byte[3_000_000];
            Array.Fill(payload, (byte)'x');

            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            long before = GC.GetTotalMemory(true);
            var handleTask = WebSocketHandler.HandleAsync(http, settings);
            await client.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(15));
            WebSocketReceiveResult? closeResult = null;
            try
            {
                closeResult = await client.ReceiveAsync(new ArraySegment<byte>(new byte[1024]), CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException) { }
            await handleTask.WaitAsync(TimeSpan.FromSeconds(10));
            long after = GC.GetTotalMemory(true);
            Assert.NotNull(closeResult);
            Assert.Equal(WebSocketMessageType.Close, closeResult!.MessageType);
            Assert.True(after - before < 600_000,
                $"fragmented oversize must stream, allocated {after - before} bytes for a 3 MB message");
        }
        finally
        {
            try { client?.Abort(); } catch { }
            try { serverWs?.Abort(); } catch { }
            try { client?.Dispose(); } catch { }
            try { serverWs?.Dispose(); } catch { }
            try { listener.Stop(); } catch { }
            mgr.Atp.Stop(wait: false);
            ConnectionManager.GlobalInstance = null;
        }
    }

    // --- B30: parser complexity ---

    [Fact]
    public async Task Telnet_LongLine_ParsesInLinearTime()
    {
        // audit B30: buf += chunk / Substring per line is O(n²). An 8 MB
        // single line must parse in linear time (quadratic needs ~10 s+ here;
        // linear needs < 1 s). Calibrated red: fails while quadratic.
        var input = new string('a', 8 << 20) + "\n";
        var sw = Stopwatch.StartNew();
        int count = 0;
        await foreach (var line in TelnetProtocol.ReadCappedLines(new System.IO.StringReader(input), 1 << 24))
        {
            count++;
            Assert.NotNull(line);
        }
        sw.Stop();
        Assert.Equal(1, count);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(8), $"8 MB line took {sw.Elapsed} (quadratic)");
    }

    // --- B33/host: prompted create on closed stdin ---

    [Fact]
    public void GameTemplate_PromptedCreate_CompletesOnClosedStdin()
    {
        // Pin: with stdin at EOF, the existing-folder prompt aborts instead of
        // hanging. (A real terminal with no input blocks in ReadLine — that
        // needs a pty and is environmental, not unit-testable.)
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_tmplin_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var origIn = Console.In;
        var origOut = Console.Out;
        var sb = new StringWriter();
        Console.SetIn(new StringReader(""));
        Console.SetOut(sb);
        try
        {
            var task = Task.Run(() => GameTemplateGenerator.CreateGameFolder(dir));
            Assert.True(task.Wait(TimeSpan.FromSeconds(10)), "prompted create must not hang on closed stdin");
            Assert.Contains("Aborted", sb.ToString());
        }
        finally
        {
            Console.SetIn(origIn);
            Console.SetOut(origOut);
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
