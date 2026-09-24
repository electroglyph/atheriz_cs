using System.Diagnostics;
using System.Reflection;
using System.Text;
using Atheriz.Core.Settings;
using Atheriz.Server.Cli;

namespace Atheriz.Core.Tests.Features.Hosting;

// `create` probed the admin API with a single scheme. On a scheme mismatch
// mismatch (settings say https, server speaks plaintext or vice versa) the
// null response fell through to the offline DB path — writing directly
// against a LIVE server. It must retry once with the flipped scheme first,
// mirroring reload.
[Collection("Ported")]
public class CreateSchemeMismatchTests
{
    private static void SetEffectiveSettings(AtherizSettings? settings)
    {
        var f = typeof(StopHandler).GetField("_effectiveCache", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(f);
        f!.SetValue(null, settings);
    }

    private static int FindFreePort()
    {
        using var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        return ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
    }

    private static async Task<string> RunProcessAsync(string fileName, string args, string? workingDir, Dictionary<string, string>? env, int timeoutMs)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDir ?? Directory.GetCurrentDirectory(),
        };
        if (env != null) foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;
        var sb = new StringBuilder();
        using var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (s, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
        proc.ErrorDataReceived += (s, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        var cts = new CancellationTokenSource(timeoutMs);
        try { await proc.WaitForExitAsync(cts.Token); } catch (OperationCanceledException) { try { proc.Kill(entireProcessTree: true); } catch { } }
        return sb.ToString();
    }

    private static async Task<bool> WaitForHealthAsync(int port, int timeoutMs)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                var body = await http.GetStringAsync($"http://localhost:{port}/health");
                if (body.Contains("ok")) return true;
            }
            catch { }
            await Task.Delay(300);
        }
        return false;
    }

    private sealed class BackgroundServer : IDisposable
    {
        public Process Proc { get; }
        public StringBuilder Output { get; } = new();
        private BackgroundServer(Process proc) => Proc = proc;
        public static BackgroundServer Start(string dll, string[] args, string workingDir, Dictionary<string, string>? env)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = workingDir,
            };
            psi.ArgumentList.Add(dll);
            foreach (var a in args) psi.ArgumentList.Add(a);
            if (env != null) foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;
            var srv = new BackgroundServer(Process.Start(psi)!);
            srv.Proc.OutputDataReceived += (s, e) => { if (e.Data != null) lock (srv.Output) srv.Output.AppendLine(e.Data); };
            srv.Proc.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (srv.Output) srv.Output.AppendLine(e.Data); };
            srv.Proc.BeginOutputReadLine();
            srv.Proc.BeginErrorReadLine();
            return srv;
        }
        public string ReadOutput() { lock (Output) return Output.ToString(); }
        public void Dispose()
        {
            try { Proc.Kill(entireProcessTree: true); } catch { }
            try { Proc.WaitForExit(5000); } catch { }
            try { Proc.Dispose(); } catch { }
        }
    }

    [Fact(Timeout = 120000)]
    public async Task Create_SchemeMismatch_FlippedRetryFindsLiveServer()
    {        if (!OperatingSystem.IsLinux()) return;
        const string repoRoot = "/home/anon/atheriz-cs";
        var dll = $"{repoRoot}/src/Atheriz.Server/bin/Release/net10.0/Atheriz.Server.dll";
        if (!File.Exists(dll)) return;
        var port = FindFreePort();
        var telnetPort = FindFreePort();
        var tmp = Path.Combine(Path.GetTempPath(), $"atheriz_createflip_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        var gameFolder = Path.Combine(tmp, "mygame");
        var env = new Dictionary<string, string>
        {
            ["ATHERIZ_SUPERUSER_USERNAME"] = "flipadmin",
            ["ATHERIZ_SUPERUSER_PASSWORD"] = "flipPass123",
            ["ATHERIZ_TELNET_PORT"] = telnetPort.ToString(),
            ["Atheriz__TelnetPort"] = telnetPort.ToString(),
        };
        var oldOut = Console.Out;
        var capture = new StringWriter();
        try
        {
            using var server = BackgroundServer.Start(dll,
                ["new", gameFolder, "--port", port.ToString(), "--telnet-port", telnetPort.ToString(), "--overwrite"],
                repoRoot, env);
            Assert.True(await WaitForHealthAsync(port, 30000), $"Server did not become healthy. Output: {server.ReadOutput()}");
            // Lie about the scheme: settings claim TLS, the live server is plaintext.
            // SecretPath points at the live game's token so only the scheme differs.
            SetEffectiveSettings(new AtherizSettings
            {
                SecretPath = Path.Combine(gameFolder, "secret"),
                SslCertFile = "/nonexistent-cert.pem",
            });
            Console.SetOut(capture);
            await CreateHandler.CreateAsync("flipacc", "flipchar", "FlipPass123", port);
            var text = capture.ToString();
            // The offline path must NOT run: its marker proves the flipped retry missed.
            Assert.DoesNotContain("No running server detected", text);
            Assert.Contains("created", text, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Console.SetOut(oldOut);
            SetEffectiveSettings(null);
            try { await RunProcessAsync("bash", $"{repoRoot}/atheriz.sh stop --port {port}", gameFolder, null, 15000); } catch { }
            try
            {
                var pf = Path.Combine(gameFolder, "save", "server.pid");
                if (File.Exists(pf) && int.TryParse(File.ReadAllText(pf).Trim(), out var pid))
                    try { Process.GetProcessById(pid).Kill(); } catch { }
            }
            catch { }
            try { await RunProcessAsync("bash", $"rm -rf \"{tmp}\"", null, null, 60000); } catch { }
            try { if (Directory.Exists(tmp)) await RunProcessAsync("bash", $"rm -rf \"{tmp}\"", null, null, 60000); } catch { }
        }
    }

    [Fact(Timeout = 120000)]
    public async Task Create_UnreachableLiveServer_RefusesOfflineWrites()
    {
        // A null admin response is "unreachable", not "not running".
        // With the token file gone (both schemes unreachable) but the
        // server live, offline direct-DB writes must be refused via the pid probe.
        if (!OperatingSystem.IsLinux()) return;
        const string repoRoot = "/home/anon/atheriz-cs";
        var dll = $"{repoRoot}/src/Atheriz.Server/bin/Release/net10.0/Atheriz.Server.dll";
        if (!File.Exists(dll)) return;
        var port = FindFreePort();
        var telnetPort = FindFreePort();
        var tmp = Path.Combine(Path.GetTempPath(), $"atheriz_createguard_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        var gameFolder = Path.Combine(tmp, "mygame");
        var env = new Dictionary<string, string>
        {
            ["ATHERIZ_SUPERUSER_USERNAME"] = "guardadmin",
            ["ATHERIZ_SUPERUSER_PASSWORD"] = "guardPass123",
            ["ATHERIZ_TELNET_PORT"] = telnetPort.ToString(),
            ["Atheriz__TelnetPort"] = telnetPort.ToString(),
        };
        var oldOut = Console.Out;
        var capture = new StringWriter();
        try
        {
            using var server = BackgroundServer.Start(dll,
                ["new", gameFolder, "--port", port.ToString(), "--telnet-port", telnetPort.ToString(), "--overwrite"],
                repoRoot, env);
            Assert.True(await WaitForHealthAsync(port, 30000), $"Server did not become healthy. Output: {server.ReadOutput()}");
            // Remove the token: both admin schemes are now unreachable, but the
            // server is live and its pid file verifies.
            var tokenFile = Path.Combine(gameFolder, "secret", "admin.token");
            Assert.True(File.Exists(tokenFile));
            File.Delete(tokenFile);
            SetEffectiveSettings(new AtherizSettings
            {
                SavePath = Path.Combine(gameFolder, "save"),
                SecretPath = Path.Combine(gameFolder, "secret"),
            });
            Console.SetOut(capture);
            await CreateHandler.CreateAsync("guardacc", "guardchar", "GuardPass123", port);
            var text = capture.ToString();
            Assert.Contains("stop it first", text);
        }
        finally
        {
            Console.SetOut(oldOut);
            SetEffectiveSettings(null);
            try { await RunProcessAsync("bash", $"{repoRoot}/atheriz.sh stop --port {port}", repoRoot, null, 15000); } catch { }
            try
            {
                var pf = Path.Combine(gameFolder, "save", "server.pid");
                if (File.Exists(pf) && int.TryParse(File.ReadAllText(pf).Trim(), out var pid))
                    try { Process.GetProcessById(pid).Kill(); } catch { }
            }
            catch { }
            try { await RunProcessAsync("bash", $"rm -rf \"{tmp}\"", null, null, 60000); } catch { }
            try { if (Directory.Exists(tmp)) await RunProcessAsync("bash", $"rm -rf \"{tmp}\"", null, null, 60000); } catch { }
        }
    }
}
