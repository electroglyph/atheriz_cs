using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Server.Cli;

namespace Atheriz.Core.Tests.Features.Hosting;

// CLI handler failure/abort paths must record a nonzero exit signal
// (consumed by Program.cs) instead of silently succeeding.
[Collection("Ported")]
public class CliExitCodeTests
{
    private static int FindFreePort()
    {
        using var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        return ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
    }

    [Fact]
    public async Task New_RejectedFolder_SignalsFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), "atheriz_exitrej_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var origCwd = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "my-game"));
            CliExitCode.Set(0);
            var result = await NewHandler.HandleNewAsync(new[] { "my-game", "--foreground" });
            Assert.False(result);
            Assert.Equal(1, CliExitCode.Code);
        }
        finally
        {
            try { Directory.SetCurrentDirectory(origCwd); } catch { }
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public async Task New_ForegroundSuccess_SignalsZero()
    {
        var root = Path.Combine(Path.GetTempPath(), "atheriz_exitok_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var origCwd = Directory.GetCurrentDirectory();
        var oldUser = Environment.GetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME");
        var oldPass = Environment.GetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD");
        Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME", "s1exitadmin");
        Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD", "s1ExitPass123");
        try
        {
            Directory.SetCurrentDirectory(root);
            CliExitCode.Set(1);
            var result = await NewHandler.HandleNewAsync(new[] { "s1exitgame", "--foreground" });
            Assert.True(result);
            Assert.Equal(0, CliExitCode.Code);
        }
        finally
        {
            try { Directory.SetCurrentDirectory(origCwd); } catch { }
            Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME", oldUser);
            Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD", oldPass);
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public async Task Reload_NoServer_SignalsFailure()
    {
        CliExitCode.Set(0);
        await ReloadHandler.HandleReloadAsync(new[] { "--port", FindFreePort().ToString() });
        Assert.Equal(1, CliExitCode.Code);
    }

    [Fact]
    public async Task Stop_NoServer_SignalsFailure()
    {
        CliExitCode.Set(0);
        await StopHandler.HandleStopAsync(new[] { "--port", FindFreePort().ToString() });
        Assert.Equal(1, CliExitCode.Code);
    }

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
}
