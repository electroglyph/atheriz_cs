using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Network;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;
using Atheriz.Server.Cli;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Regression;

// Audit6 batch F: F30 webclient-sync wwwroot fallback, F31 create exit code,
// F32 byte-count logs, F33 queued-vs-dropped busy signals.
[Collection("Ported")]
public sealed class Audit6BatchFTests
{
    // ---------- F30: wwwroot/webclient fallback instead of return-clean ----------

    // Engine baseline is always passed explicitly (never bare-resolved):
    // CWD is process-global and parallel tests repark it, so bare
    // ResolveEngineWeb can pick up a scaffolded game dir's web/ tree.
    // A nonexistent override mirrors exactly what the resolver returns in
    // the C# layout (contentRoot/web), exercising the same fork.
    private static readonly string NonExistentWeb = Path.Combine(Path.GetTempPath(), "atheriz_no_such_web_xyz");

    [Fact]
    public void CheckSync_WwwrootBaseline_ComparesStaticInsteadOfClean()
    {
        // C# layout: engine webclient under contentRoot/wwwroot, no web/ tree.
        // The static area must be compared against it, not reported clean.
        using var env = GlobalTestEnv.Enter();
        var game = Path.Combine(env.TempPath, "game");
        Directory.CreateDirectory(Path.Combine(game, "web", "static", "webclient"));
        Directory.CreateDirectory(Path.Combine(game, "web", "templates", "webclient"));
        File.WriteAllText(Path.Combine(game, "web", "static", "webclient", "app.js"), "game-v1");
        File.WriteAllText(Path.Combine(game, "web", "templates", "webclient", "extra.js"), "game-only");
        Directory.CreateDirectory(Path.Combine(env.TempPath, "wwwroot", "webclient"));
        File.WriteAllText(Path.Combine(env.TempPath, "wwwroot", "webclient", "app.js"), "engine-v1");
        var on = new AtherizSettings { WebclientSyncCheck = true };
        var summary = WebclientSyncChecker.CheckSync(game, env.TempPath, NonExistentWeb, on);
        Assert.NotNull(summary);
        Assert.Contains("app.js", summary!["static"]["different"]);
        // No web/ baseline: game templates have nothing to judge them against,
        // so they must not be reported as extra.
        Assert.Empty(summary["templates"]["extra"]);
    }

    [Fact]
    public void CheckSync_WwwrootBaseline_IdenticalIsClean()
    {
        // Same layout with identical bytes stays clean (null).
        using var env = GlobalTestEnv.Enter();
        var game = Path.Combine(env.TempPath, "game");
        Directory.CreateDirectory(Path.Combine(game, "web", "static", "webclient"));
        File.WriteAllText(Path.Combine(game, "web", "static", "webclient", "app.js"), "same");
        Directory.CreateDirectory(Path.Combine(env.TempPath, "wwwroot", "webclient"));
        File.WriteAllText(Path.Combine(env.TempPath, "wwwroot", "webclient", "app.js"), "same");
        var on = new AtherizSettings { WebclientSyncCheck = true };
        Assert.Null(WebclientSyncChecker.CheckSync(game, env.TempPath, NonExistentWeb, on));
    }

    [Fact]
    public void CheckSync_NoBaselineAtAll_ReturnsClean()
    {
        // Game web/ exists but there is no web/ baseline and no wwwroot
        // baseline anywhere: still clean (unchanged behavior).
        using var env = GlobalTestEnv.Enter();
        var game = Path.Combine(env.TempPath, "game");
        Directory.CreateDirectory(Path.Combine(game, "web", "static", "webclient"));
        File.WriteAllText(Path.Combine(game, "web", "static", "webclient", "app.js"), "game-v1");
        var on = new AtherizSettings { WebclientSyncCheck = true };
        Assert.Null(WebclientSyncChecker.CheckSync(game, env.TempPath, NonExistentWeb, on));
    }

    // ---------- F31: live-server verdict is the exit code ----------

    private static void SetEffectiveSettings(AtherizSettings? settings)
    {
        var f = typeof(StopHandler).GetField("_effectiveCache", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(f);
        f!.SetValue(null, settings);
    }

    // Minimal stub for one /_internal/* POST: reads headers + Content-Length
    // body bytes, answers a canned JSON payload, closes.
    private sealed class StubAdminServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly string _json;
        public int Port { get; }

        public StubAdminServer(string json)
        {
            _json = json;
            TcpListener? l = null;
            try
            {
                l = new TcpListener(IPAddress.IPv6Any, 0);
                l.Server.DualMode = true;
            }
            catch { l = null; }
            l ??= new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            _listener = l;
            Port = ((IPEndPoint)l.LocalEndpoint!).Port;
        }

        public async Task ServeOnceAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            using var stream = client.GetStream();
            var req = new List<byte>();
            var buf = new byte[4096];
            int headerEnd = -1;
            while (headerEnd < 0)
            {
                int n = await stream.ReadAsync(buf, 0, buf.Length).WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                if (n == 0) break;
                int baseIdx = req.Count;
                req.AddRange(buf[..n]);
                for (int i = Math.Max(0, baseIdx - 3); i + 3 < req.Count; i++)
                {
                    if (req[i] == 13 && req[i + 1] == 10 && req[i + 2] == 13 && req[i + 3] == 10) { headerEnd = i + 4; break; }
                }
                if (req.Count > 65536) break;
            }
            int contentLength = 0;
            if (headerEnd > 0)
            {
                foreach (var line in Encoding.ASCII.GetString(req.ToArray(), 0, headerEnd).Split("\r\n"))
                {
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(line.Substring(15).Trim(), out int cl)) contentLength = cl;
                }
                int alreadyHave = req.Count - headerEnd;
                var body = new byte[Math.Max(0, contentLength - alreadyHave)];
                int read = 0;
                while (read < body.Length)
                {
                    int n = await stream.ReadAsync(body, read, body.Length - read).WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                    if (n == 0) break;
                    read += n;
                }
            }
            byte[] payload = Encoding.UTF8.GetBytes(_json);
            byte[] head = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(head, 0, head.Length).ConfigureAwait(false);
            await stream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
        }

        public void Dispose() { try { _listener.Stop(); } catch { } }
    }

    [Fact]
    public async Task Create_LiveServerError_ExitsOne()
    {
        // A live server answering {status:"error"} must exit 1, not 0.
        using var env = GlobalTestEnv.Enter();
        using var stub = new StubAdminServer("{\"status\":\"error\",\"message\":\"Account 'stubacc' already exists.\"}");
        var serve = stub.ServeOnceAsync();
        var secret = Path.Combine(env.TempPath, "secret");
        Directory.CreateDirectory(secret);
        File.WriteAllText(Path.Combine(secret, "admin.token"), "testtoken");
        SetEffectiveSettings(new AtherizSettings { SecretPath = secret, WebserverPort = stub.Port });
        var oldOut = Console.Out;
        var capture = new StringWriter();
        try
        {
            Console.SetOut(capture);
            await CreateHandler.HandleCreateAsync(["--port", stub.Port.ToString(), "stubacc", "StubChar", "StubPass123"]);
            await serve.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Contains("already exists", capture.ToString(), StringComparison.Ordinal);
            Assert.Equal(1, CliExitCode.Code);
        }
        finally
        {
            Console.SetOut(oldOut);
            SetEffectiveSettings(null);
        }
    }

    [Fact]
    public async Task Create_LiveServerOk_ExitsZero()
    {
        // Control: {status:"ok"} still exits 0.
        using var env = GlobalTestEnv.Enter();
        using var stub = new StubAdminServer("{\"status\":\"ok\",\"message\":\"Account created.\"}");
        var serve = stub.ServeOnceAsync();
        var secret = Path.Combine(env.TempPath, "secret");
        Directory.CreateDirectory(secret);
        File.WriteAllText(Path.Combine(secret, "admin.token"), "testtoken");
        SetEffectiveSettings(new AtherizSettings { SecretPath = secret, WebserverPort = stub.Port });
        var oldOut = Console.Out;
        var capture = new StringWriter();
        try
        {
            Console.SetOut(capture);
            await CreateHandler.HandleCreateAsync(["--port", stub.Port.ToString(), "okacc", "OkChar", "OkPass123"]);
            await serve.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Contains("Account created.", capture.ToString(), StringComparison.Ordinal);
            Assert.Equal(0, CliExitCode.Code);
        }
        finally
        {
            Console.SetOut(oldOut);
            SetEffectiveSettings(null);
        }
    }

    // ---------- F32: logs report bytes, not chars ----------

    [Fact]
    public void HandleCommand_OversizeMultibyte_LogsByteCount()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = PortedHelpers.MakeManager(new AtherizSettings { WebsocketMaxMessageSize = 16 });
        try
        {
            var c = new TestConn("c-oversize", "10.30.0.1");
            // 14 chars on the wire but 24 bytes of UTF-8: over a 16-byte
            // budget without ever exceeding 16 chars.
            string raw = "[\"éééééééééé\"]";
            int bytes = Encoding.UTF8.GetByteCount(raw);
            Assert.True(raw.Length <= 16 && bytes > 16);
            string log;
            using (var cap = new CaptureAtherizLog())
            {
                mgr.HandleCommand(c, raw);
                Assert.True(PortedHelpers.WaitFor(() => cap.Read().Contains("Message too large", StringComparison.Ordinal), 5000));
                log = cap.Read();
            }
            Assert.Contains($"({bytes} bytes > 16 bytes)", log, StringComparison.Ordinal);
        }
        finally { mgr.Atp.Stop(wait: false); }
    }

    [Fact]
    public void HandleCommand_MalformedMultibyte_LogsByteCount()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = PortedHelpers.MakeManager();
        try
        {
            var c = new TestConn("c-malformed", "10.30.0.2");
            // Valid JSON object, not an array: takes the malformed path.
            // 6 chars, 7 bytes of UTF-8.
            string raw = "{\"é\":1}";
            int bytes = Encoding.UTF8.GetByteCount(raw);
            Assert.NotEqual(raw.Length, bytes);
            string log;
            using (var cap = new CaptureAtherizLog())
            {
                mgr.HandleCommand(c, raw);
                Assert.True(PortedHelpers.WaitFor(() => cap.Read().Contains("Invalid message format", StringComparison.Ordinal), 5000));
                log = cap.Read();
            }
            Assert.Contains($"({bytes} bytes)", log, StringComparison.Ordinal);
        }
        finally { mgr.Atp.Stop(wait: false); }
    }

    // ---------- F33: queued/retrying vs dropped ----------

    private static void SetInputRunning(BaseConnection c, bool v)
    {
        var f = typeof(BaseConnection).GetField("_inputRunning", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(f);
        f!.SetValue(c, v);
    }

    private static System.Collections.ICollection GetInputQueue(BaseConnection c)
    {
        var f = typeof(BaseConnection).GetField("_inputQueue", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (System.Collections.ICollection)f.GetValue(c)!;
    }

    private static bool SentContains(TestConnection c, string text)
        => c.Sent.Exists(s => s.Args.Exists(a => (a?.ToString() ?? "").Contains(text, StringComparison.Ordinal)));

    [Fact]
    public void EnqueueInput_QueueFull_ReportsDroppedNotRetry()
    {
        // The queue-full path drops the message: the client must hear
        // "dropped", never "pending retry".
        using var env = GlobalTestEnv.Enter();
        var mgr = PortedHelpers.MakeManager();
        ConnectionManager.GlobalInstance = mgr;
        var conn = new TestConnection();
        try
        {
            // Hold the drain so fills stay queued without touching the
            // throttle window or the pool.
            SetInputRunning(conn, true);
            Action<BaseConnection, List<object?>, Dictionary<string, object?>> noop = (_, _, _) => { };
            int cap = new AtherizSettings().ConnectionInputQueueLimit;
            for (int i = 0; i < cap; i++) conn.EnqueueInput(noop, [], []);
            Assert.Equal(cap, GetInputQueue(conn).Count);
            string log;
            using (var capLog = new CaptureAtherizLog())
            {
                conn.EnqueueInput(noop, [], []);
                Assert.True(PortedHelpers.WaitFor(() => capLog.Read().Contains("Input queue full", StringComparison.Ordinal), 5000));
                log = capLog.Read();
            }
            Assert.True(SentContains(conn, "Server busy; input dropped."), "queue-full must notify dropped");
            Assert.False(SentContains(conn, "retrying"), "queue-full must not promise a retry");
            Assert.Contains("input dropped", log, StringComparison.Ordinal);
            Assert.DoesNotContain("pending retry", log, StringComparison.Ordinal);
        }
        finally
        {
            conn.SetDisconnected(true);
            conn.ClearPendingInput();
            ConnectionManager.GlobalInstance = null;
            mgr.Atp.Stop(wait: false);
        }
    }
}
