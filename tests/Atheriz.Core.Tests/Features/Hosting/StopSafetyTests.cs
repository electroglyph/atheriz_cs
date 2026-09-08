using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Atheriz.Core.Globals;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Server.Cli;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Hosting;

// `stop` must never terminate an unverified process, and a refused
// graceful-shutdown request must abort instead of escalating into signals.
[Collection("Ported")]
public class StopSafetyTests
{
    private static void SetEffectiveSettings(AtherizSettings? settings)
    {
        var f = typeof(StopHandler).GetField("_effectiveCache", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(f);
        f!.SetValue(null, settings);
    }

    private static string InvokeShutdownRequest(string secretPath, int port, bool tlsOn = false)
    {
        var m = typeof(ShutdownClient).GetMethod("TryRequestShutdownAsync", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(m);
        var task = (Task)m!.Invoke(null, new object[] { port, secretPath, tlsOn })!;
        task.GetAwaiter().GetResult();
        var resultProp = task.GetType().GetProperty("Result");
        return resultProp!.GetValue(task)!.ToString()!;
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    // Minimal one-shot HTTP responder for /_internal/* admin endpoints.
    private static async Task ServeOneAdminReplyAsync(TcpListener listener, string json, int timeoutMs = 10000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        using var client = await listener.AcceptTcpClientAsync(cts.Token);
        using var stream = client.GetStream();
        var buf = new byte[8192];
        // Headers are ASCII, so the char offset of the header end equals its
        // byte offset; the body is then drained by byte count.
        using var raw = new MemoryStream();
        int headerEnd = -1;
        int contentLength = 0;
        while (headerEnd < 0)
        {
            int n = await stream.ReadAsync(buf, cts.Token);
            if (n <= 0) break;
            raw.Write(buf, 0, n);
            var s = Encoding.ASCII.GetString(raw.ToArray());
            int end = s.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (end >= 0)
            {
                headerEnd = end + 4;
                foreach (var line in s[..end].Split("\r\n"))
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(line["Content-Length:".Length..].Trim(), out var cl))
                        contentLength = cl;
            }
        }
        if (headerEnd >= 0)
        {
            int remaining = contentLength - ((int)raw.Length - headerEnd);
            while (remaining > 0)
            {
                int n = await stream.ReadAsync(buf, cts.Token);
                if (n <= 0) break;
                remaining -= n;
            }
        }
        var body = Encoding.UTF8.GetBytes(json);
        var header = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.UTF8.GetBytes(header), cts.Token);
        await stream.WriteAsync(body, cts.Token);
        await stream.FlushAsync(cts.Token);
    }

    [Fact]
    public void IsServerProcess_RejectsOwnTestHost()
    {
        // Guard the premise: /proc/self/cmdline must not name the server assembly.
        var cmdlinePath = "/proc/self/cmdline";
        if (!File.Exists(cmdlinePath)) return; // non-Linux: nothing to prove
        var cmdline = File.ReadAllText(cmdlinePath);
        Assert.DoesNotContain("Atheriz.Server.dll", cmdline);
        // The old bare-"Atheriz"-substring rule accepted the test host (its
        // command line names the test assembly); the strict rule must not.
        Assert.False(PidFile.IsServerProcess(Environment.ProcessId));
    }

    [Fact]
    public void IsServerProcess_RejectsUnrelatedProcess()
    {
        Process? sleeper = null;
        try { sleeper = Process.Start(new ProcessStartInfo { FileName = "sleep", Arguments = "30", UseShellExecute = false }); }
        catch { return; } // no sleep(1): nothing to prove
        try
        {
            Assert.NotNull(sleeper);
            Assert.False(PidFile.IsServerProcess(sleeper!.Id));
        }
        finally { try { sleeper?.Kill(); sleeper?.Dispose(); } catch { } }
    }

    [Fact]
    public void IsServerProcess_RejectsNonexistentPid()
    {
        Assert.False(PidFile.IsServerProcess(int.MaxValue));
    }

    [Fact]
    public void IsPortListening_MatchesBoundListener()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Assert.True(PidFile.IsPortListening(port));
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public void LocateServerPidFile_PrefersSaveDirFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_locate_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var pidPath = Path.Combine(dir, "server.pid");
            File.WriteAllText(pidPath, "1234");
            Assert.Equal(pidPath, PidFile.LocateServerPidFile(FreePort(), dir));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void TryRequestShutdown_UnreachableWithoutTokenOrServer()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_nosrv_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var origOut = Console.Out;
            Console.SetOut(new StringWriter());
            try { Assert.Equal("Unreachable", InvokeShutdownRequest(dir, FreePort())); }
            finally { Console.SetOut(origOut); }
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task TryRequestShutdown_AuthRejectedOnLiveRefusal()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_refuse_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "admin.token"), "wrong-token");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            var serve = ServeOneAdminReplyAsync(listener, """{"status":"error","message":"Invalid token."}""");
            var origOut = Console.Out;
            string outcome;
            Console.SetOut(new StringWriter());
            try { outcome = InvokeShutdownRequest(dir, port); }
            finally { Console.SetOut(origOut); }
            await serve;
            Assert.Equal("AuthRejected", outcome);
        }
        finally { listener.Stop(); try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task TryRequestShutdown_AcceptedOnOk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_ok_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "admin.token"), "token");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            var serve = ServeOneAdminReplyAsync(listener, """{"status":"ok","message":"Shutdown tasks queued."}""");
            var origOut = Console.Out;
            string outcome;
            Console.SetOut(new StringWriter());
            try { outcome = InvokeShutdownRequest(dir, port); }
            finally { Console.SetOut(origOut); }
            await serve;
            Assert.Equal("Accepted", outcome);
        }
        finally { listener.Stop(); try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task Create_WithLiveServerAnsweringError_DoesNotTouchDatabase()
    {
        // any HTTP answer — even {status:"error"} — proves a live server
        // owns this world: print its message and return, never create offline.
        using var env = GlobalTestEnv.Enter();
        var before = ObjectRegistry.Count;
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_crefuse_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "admin.token"), "token");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var settings = new AtherizSettings { SavePath = dir, SecretPath = dir, WebserverPort = port };
        SetEffectiveSettings(settings);
        var sb = new StringWriter();
        var origOut = Console.Out;
        Console.SetOut(sb);
        try
        {
            var serve = ServeOneAdminReplyAsync(listener, """{"status":"error","message":"account_name, char_name and password are required."}""");
            await CreateHandler.HandleCreateAsync(new[] { "liveacc", "LiveChar", "supersecret123" });
            await serve;
        }
        finally
        {
            Console.SetOut(origOut);
            SetEffectiveSettings(null);
            listener.Stop();
            try { Directory.Delete(dir, true); } catch { }
        }
        Assert.Contains("account_name, char_name and password are required.", sb.ToString());
        Assert.Equal(before, ObjectRegistry.Count);
    }

    [Fact]
    public void HandleStop_FallbackPathVerifiesIdentityBeforeKill()
    {
        // Structural pin: the no-pidfile fallback must hold a verified per-PID
        // check (identity + port hold) before signalling, and a refused
        // graceful request must abort rather than escalate.
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Cli/StopHandler.cs");
        Assert.Contains("IsServerProcess(foundPid)", src);
        Assert.Contains("IsProcessListeningOnPort(foundPid", src);
        Assert.Contains("AuthRejected", src);
    }
}
