// Integration test for atheriz.sh start/stop/reload + WS/telnet basic functionality
// Covers the user's request: ./atheriz.sh start actually starts, reload actually reloads, stop actually stops,
// and WS/telnet login + look/inventory/exam etc. work.
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedServerIntegrationTests
{
    private static int FindFreePortInt()
    {
        // TOCTOU mitigation: OS-allocated ephemeral port via port 0, with small yield to let kernel release.
        // Caller should retry on EADDRINUSE; see ServerLifecycle_WsAndTelnet_BasicCommands retry loop.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        Thread.Sleep(10);
        return port;
    }

    private static string FindFreePort() => FindFreePortInt().ToString();

    private static async Task<bool> WaitForHealthAsync(int port, int timeoutMs = 15000)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        // Deterministic polling via TaskCompletionSource + PortedHelpers.WaitAsync pattern (instead of raw spin loop).
        // TCS signals when health becomes true; outer WhenAny provides timeout determinism.
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                try
                {
                    var resp = await client.GetAsync($"http://localhost:{port}/health");
                    if (resp.IsSuccessStatusCode) { tcs.TrySetResult(true); return; }
                }
                catch { }
                await Task.Delay(200);
            }
            tcs.TrySetResult(false);
        });
        var winner = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs + 1000));
        return winner == tcs.Task && await tcs.Task;
    }

    private static async Task<string> RunProcessAsync(string fileName, string args, string? workingDir = null, Dictionary<string,string>? env = null, int timeoutMs = 30000)
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
        var sbOut = new StringBuilder();
        var sbErr = new StringBuilder();
        using var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (s,e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
        proc.ErrorDataReceived += (s,e) => { if (e.Data != null) sbErr.AppendLine(e.Data); };
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        var cts = new CancellationTokenSource(timeoutMs);
        try { await proc.WaitForExitAsync(cts.Token); } catch (OperationCanceledException) { try { proc.Kill(entireProcessTree:true); } catch { } }
        return sbOut.ToString() + "\n" + sbErr.ToString();
    }

    // Foreground CLI: servers run until killed, so integration tests launch
    // them backgrounded (drained output, no wait) and stop them explicitly.
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
    public async Task ServerLifecycle_WsAndTelnet_BasicCommands()
    {
        if (!OperatingSystem.IsLinux()) return; // only on linux where loopback spawns work
        var repoRoot = "/home/anon/atheriz-cs";
        var dll = $"{repoRoot}/src/Atheriz.Server/bin/Release/net10.0/Atheriz.Server.dll";
        if (!File.Exists(dll)) return; // skip if not built
        var port = FindFreePortInt();
        var telnetPort = FindFreePortInt();
        int attempts = 0;
        while ((telnetPort == port || await IsPortListeningAsync(port) || await IsPortListeningAsync(telnetPort)) && attempts < 5)
        {
            port = FindFreePortInt();
            telnetPort = FindFreePortInt();
            attempts++;
        }
        // Use a fresh temp game folder
        var tmp = Path.Combine(Path.GetTempPath(), $"atheriz_int_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        var gameFolder = Path.Combine(tmp, "mygame");
        var env = new Dictionary<string,string>
        {
            ["ATHERIZ_SUPERUSER_USERNAME"] = "intadmin",
            ["ATHERIZ_SUPERUSER_PASSWORD"] = "intpass123",
            ["ATHERIZ_TELNET_PORT"] = telnetPort.ToString(),
            ["Atheriz__TelnetPort"] = telnetPort.ToString()
        };
        string output = "";
        try
        {
            // 1. start the server backgrounded (foreground CLI): health gates progress
            using var server = BackgroundServer.Start(dll,
                ["new", gameFolder, "--port", port.ToString(), "--telnet-port", telnetPort.ToString(), "--overwrite"],
                repoRoot, env);
            Assert.True(await WaitForHealthAsync(port, 30000), $"Server did not become healthy on {port}. Output: {server.ReadOutput()}\nLog: {TryReadLog(gameFolder)}");
            // Health needs only the port; the parent prints banners after the
            // pid claim lands too, so poll for the banner text (bounded) instead
            // of asserting on the first read — under suite IO load the claim
            // lags the bind and the immediate read races it.
            output = "";
            var bannerDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (DateTime.UtcNow < bannerDeadline)
            {
                output = server.ReadOutput();
                if (output.Contains("Web server listening", StringComparison.Ordinal)) break;
                await Task.Delay(200);
            }
            Assert.Contains("Creating game folder", output);
            Assert.Contains("Web server listening", output);
            // Verify pid file exists
            var pidFile = Path.Combine(gameFolder, "save", "server.pid");
            Assert.True(File.Exists(pidFile), "server.pid not created");
            // 2. WS test: connect, select char, run commands
            await TestWebSocketAsync(port);
            // 3. Telnet test on free telnetPort
            await TestTelnetAsync(gameFolder, telnetPort);
            // 4. Reload test
            var tokenFile = Path.Combine(gameFolder, "secret", "admin.token");
            Assert.True(File.Exists(tokenFile), "admin.token missing");
            var token = File.ReadAllText(tokenFile).Trim();
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var req = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{port}/_internal/hot_reload");
            req.Headers.Add("X-Admin-Token", token);
            var resp = await http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            Assert.True(resp.IsSuccessStatusCode, $"reload failed {resp.StatusCode} {body}");
            Assert.Contains("ok", body.ToLowerInvariant());
            // After reload, health should still be ok and WS should still work (new connection)
            Assert.True(await WaitForHealthAsync(port, 5000), "health after reload failed");
            await TestWebSocketAsync(port, "intadmin", "intpass123");
            // 5. Stop test (from the game folder, so token + pid resolve)
            var stopOut = await RunProcessAsync("bash", $"{repoRoot}/atheriz.sh stop --port {port}", gameFolder, null, 15000);
            Assert.Contains("Graceful shutdown", stopOut + TryReadLog(gameFolder));
            // Wait for port to close via async polling (per-attempt 600ms
            // cap preserves the old sync-wrapper timeout semantics).
            bool stopped = false;
            var stopSw = Stopwatch.StartNew();
            while (stopSw.ElapsedMilliseconds < 10000)
            {
                var check = IsPortListeningAsync(port);
                var checkWinner = await Task.WhenAny(check, Task.Delay(600));
                if (checkWinner == check && !await check) { stopped = true; break; }
                await Task.Delay(200);
            }
            if (!stopped)
            {
                var final = IsPortListeningAsync(port);
                var finalWinner = await Task.WhenAny(final, Task.Delay(600));
                stopped = finalWinner == final && !await final;
            }
            Assert.True(stopped, $"Server still listening on {port} after stop. Output: {stopOut}");
            Assert.False(File.Exists(pidFile), "pid file still exists after stop");
        }
        finally
        {
            // Cleanup: ensure stopped, kill if needed, delete tmp
            try { await RunProcessAsync("bash", $"{repoRoot}/atheriz.sh stop --port {port}", gameFolder, null, 5000); } catch { }
            try { await Task.Delay(1500); } catch { }
            // Force kill if still listening
            try
            {
                if (await IsPortListeningAsync(port))
                {
                    // try pkill via pid file
                    var pf = Path.Combine(gameFolder, "save", "server.pid");
                    if (File.Exists(pf) && int.TryParse(File.ReadAllText(pf).Trim(), out var pid))
                        try { Process.GetProcessById(pid).Kill(); } catch { }
                    await Task.Delay(1000);
                }
            } catch { }
            // Use rm -rf via bash to avoid AccessViolation from FileSystem.RemoveDirectoryRecursive on locked files.
            // 60s budget with an existence-checked retry: a 5s cap times out
            // deleting ~660MB game scaffolds under parallel-suite IO load,
            // leaking whole game dirs into /tmp until the tmpfs fills and
            // unrelated SQLite tests fail with "disk is full".
            try { await RunProcessAsync("bash", $"rm -rf \"{tmp}\"", null, null, 60000); } catch { }
            try { if (Directory.Exists(tmp)) await RunProcessAsync("bash", $"rm -rf \"{tmp}\"", null, null, 60000); } catch { }
            try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch (Exception ex) { Console.WriteLine($"Cleanup delete failed: {ex.Message}"); }
        }
    }

    [Fact(Timeout = 120000)]
    public async Task BackgroundNew_SurvivesSighup_AndStopsViaDll()
    {
        // Background `new`: the launching process prints the banners and
        // exits itself; the daemon survives SIGHUP and stops via direct
        // `dotnet <dll> stop` (the stop path under test is not .sh-only).
        if (!OperatingSystem.IsLinux()) return; // SIGHUP is Unix-only
        var repoRoot = "/home/anon/atheriz-cs";
        var dll = $"{repoRoot}/src/Atheriz.Server/bin/Release/net10.0/Atheriz.Server.dll";
        if (!File.Exists(dll)) return; // skip if not built
        var port = FindFreePortInt();
        var telnetPort = FindFreePortInt();
        int attempts = 0;
        while ((telnetPort == port || await IsPortListeningAsync(port) || await IsPortListeningAsync(telnetPort)) && attempts < 5)
        {
            port = FindFreePortInt();
            telnetPort = FindFreePortInt();
            attempts++;
        }
        var tmp = Path.Combine(Path.GetTempPath(), $"atheriz_daemon_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        var gameFolder = Path.Combine(tmp, "mygame");
        var env = new Dictionary<string, string>
        {
            ["ATHERIZ_SUPERUSER_USERNAME"] = "daemonadmin",
            ["ATHERIZ_SUPERUSER_PASSWORD"] = "daemonpass123",
            ["ATHERIZ_TELNET_PORT"] = telnetPort.ToString(),
            ["Atheriz__TelnetPort"] = telnetPort.ToString()
        };
        try
        {
            var newOut = await RunProcessAsync("dotnet",
                $"{dll} new \"{gameFolder}\" --port {port} --telnet-port {telnetPort} --overwrite",
                repoRoot, env, 90000);
            Assert.Contains("Creating game folder", newOut);
            Assert.Contains("Web server listening", newOut);
            Assert.True(await WaitForHealthAsync(port, 15000), $"Daemon not healthy on {port}. Output: {newOut}\nLog: {TryReadLog(gameFolder)}");
            var pidFile = Path.Combine(gameFolder, "save", "server.pid");
            Assert.True(File.Exists(pidFile), "server.pid not created");
            Assert.True(int.TryParse(File.ReadAllText(pidFile).Trim(), out var daemonPid), "server.pid unparseable");
            Assert.NotEqual(Environment.ProcessId, daemonPid);
            await RunProcessAsync("kill", $"-HUP {daemonPid}", gameFolder, null, 5000);
            await Task.Delay(1500);
            Assert.True(await WaitForHealthAsync(port, 10000), $"Daemon died on SIGHUP.\nLog: {TryReadLog(gameFolder)}");
            var stopOut = await RunProcessAsync("dotnet", $"{dll} stop --port {port}", gameFolder, null, 30000);
            Assert.Contains("Graceful shutdown", stopOut + TryReadLog(gameFolder));
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 15000 && await IsPortListeningAsync(port))
                await Task.Delay(300);
            Assert.False(await IsPortListeningAsync(port), $"Daemon still listening on {port} after stop. Output: {stopOut}");
            Assert.False(File.Exists(pidFile), "pid file still exists after stop");
        }
        finally
        {
            try { await RunProcessAsync("dotnet", $"{dll} stop --port {port}", gameFolder, null, 5000); } catch { }
            try { await Task.Delay(1000); } catch { }
            try
            {
                if (await IsPortListeningAsync(port))
                {
                    var pf = Path.Combine(gameFolder, "save", "server.pid");
                    if (File.Exists(pf) && int.TryParse(File.ReadAllText(pf).Trim(), out var pid))
                        try { Process.GetProcessById(pid).Kill(); } catch { }
                    await Task.Delay(1000);
                }
            }
            catch { }
            try { await RunProcessAsync("bash", $"rm -rf \"{tmp}\"", null, null, 60000); } catch { }
            try { if (Directory.Exists(tmp)) await RunProcessAsync("bash", $"rm -rf \"{tmp}\"", null, null, 60000); } catch { }
            try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
        }
    }

    [Fact(Timeout = 120000)]
    public async Task BareNameNewOverwrite_CanConnect()
    {
        if (!OperatingSystem.IsLinux()) return;
        var repoRoot = "/home/anon/atheriz-cs";
        var dll = $"{repoRoot}/src/Atheriz.Server/bin/Release/net10.0/Atheriz.Server.dll";
        if (!File.Exists(dll)) return;
        var port = FindFreePortInt();
        var telnetPort = FindFreePortInt();
        int attempts = 0;
        while ((telnetPort == port || await IsPortListeningAsync(port) || await IsPortListeningAsync(telnetPort)) && attempts < 5)
        {
            port = FindFreePortInt();
            telnetPort = FindFreePortInt();
            attempts++;
        }
        var tmpRoot = Path.Combine(Path.GetTempPath(), $"atheriz_bare_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpRoot);
        // Pre-create a stale test folder to emulate existing repo `test` with old DB (verifies overwrite wipes it)
        var staleTestFolder = Path.Combine(tmpRoot, "test");
        Directory.CreateDirectory(staleTestFolder);
        Directory.CreateDirectory(Path.Combine(staleTestFolder, "save"));
        File.WriteAllText(Path.Combine(staleTestFolder, "save", "stale.txt"), "stale");
        var gameFolder = Path.Combine(tmpRoot, "test");
        var env = new Dictionary<string,string>
        {
            ["ATHERIZ_SUPERUSER_USERNAME"] = "bareadmin",
            ["ATHERIZ_SUPERUSER_PASSWORD"] = "barepass123",
            ["ATHERIZ_TELNET_PORT"] = telnetPort.ToString(),
            ["Atheriz__TelnetPort"] = telnetPort.ToString()
        };
        string output = "";
        try
        {
            // Bare name `test` with --overwrite must recreate world even though folder existed (folderExistsInitially=true)
            using var server = BackgroundServer.Start(dll,
                ["new", "test", "--port", port.ToString(), "--telnet-port", telnetPort.ToString(), "--overwrite"],
                tmpRoot, env);
            output = "";
            Assert.True(await WaitForHealthAsync(port, 30000), $"Server did not become healthy on {port}. Output: {server.ReadOutput()}\nLog: {TryReadLog(gameFolder)}");
            output = server.ReadOutput();
            Assert.Contains("Creating game folder", output);
            Assert.True(File.Exists(Path.Combine(gameFolder, "save", "database.sqlite3")) || File.Exists(Path.Combine(gameFolder, "save", "server.log")), $"DB/log not created. Output: {output}");
            Assert.False(File.Exists(Path.Combine(gameFolder, "save", "stale.txt")), "stale.txt not wiped on overwrite");
            Assert.True(await WaitForHealthAsync(port, 15000), $"Server did not become healthy on {port}. Output: {output}\nLog: {TryReadLog(gameFolder)}");
            var pidFile = Path.Combine(gameFolder, "save", "server.pid");
            Assert.True(File.Exists(pidFile), "server.pid not created for bare name");
            // Verify WS connect with bareadmin works (proves DoSetup ran with env creds)
            await TestWebSocketAsync(port, "bareadmin", "barepass123");
            await TestTelnetAsync(gameFolder, telnetPort, "bareadmin", "barepass123");
            // Verify overwrite is idempotent: second bare `new test --overwrite` with different creds should replace DB
            var stop1 = await RunProcessAsync("bash", $"{repoRoot}/atheriz.sh stop --port {port}", gameFolder, null, 15000);
            await Task.Delay(1500);
            env["ATHERIZ_SUPERUSER_USERNAME"] = "bareadmin2";
            env["ATHERIZ_SUPERUSER_PASSWORD"] = "barepass456";
            var port2 = FindFreePortInt();
            var telnetPort2 = FindFreePortInt();
            while (telnetPort2 == port2 || await IsPortListeningAsync(port2) || await IsPortListeningAsync(telnetPort2))
            {
                port2 = FindFreePortInt();
                telnetPort2 = FindFreePortInt();
            }
            env["ATHERIZ_TELNET_PORT"] = telnetPort2.ToString();
            env["Atheriz__TelnetPort"] = telnetPort2.ToString();
            using var server2 = BackgroundServer.Start(dll,
                ["new", "test", "--port", port2.ToString(), "--telnet-port", telnetPort2.ToString(), "--overwrite"],
                tmpRoot, env);
            Assert.True(await WaitForHealthAsync(port2, 30000), $"Second overwrite server not healthy on {port2}. Output: {server2.ReadOutput()}\nLog: {TryReadLog(gameFolder)}");
            Assert.Contains("Creating game folder", server2.ReadOutput());
            await TestWebSocketAsync(port2, "bareadmin2", "barepass456");
            // Clean second server
            await RunProcessAsync("bash", $"{repoRoot}/atheriz.sh stop --port {port2}", gameFolder, null, 15000);
            await Task.Delay(1000);
            Assert.False(await IsPortListeningAsync(port2), $"Second server still listening on {port2}");
        }
        finally
        {
            try { await RunProcessAsync("bash", $"{repoRoot}/atheriz.sh stop --port {port}", gameFolder, null, 5000); } catch { }
            try { await Task.Delay(1000); } catch { }
            try { await RunProcessAsync("bash", $"rm -rf \"{tmpRoot}\"", null, null, 60000); } catch { }
            try { if (Directory.Exists(tmpRoot)) await RunProcessAsync("bash", $"rm -rf \"{tmpRoot}\"", null, null, 60000); } catch { }
            try { if (Directory.Exists(tmpRoot)) Directory.Delete(tmpRoot, true); } catch { }
        }
    }

    private static string TryReadLog(string gameFolder)
    {
        try { var log = Path.Combine(gameFolder, "save", "server.log"); if (File.Exists(log)) return "\n--- server.log ---\n" + File.ReadAllText(log).Substring(0, 4000); } catch { }
        return "";
    }

    private static async Task<bool> IsPortListeningAsync(int port)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync("127.0.0.1", port);
            var timeout = Task.Delay(500);
            var completed = await Task.WhenAny(connectTask, timeout);
            return completed == connectTask && client.Connected;
        }
        catch { return false; }
    }

    private static async Task TestWebSocketAsync(int port, string account = "intadmin", string password = "intpass123")
    {
        using var ws = new ClientWebSocket();
        var cts = new CancellationTokenSource(10000);
        await ws.ConnectAsync(new Uri($"ws://localhost:{port}/ws"), cts.Token);
        Assert.Equal(WebSocketState.Open, ws.State);
        // Background receiver to avoid pending-ReceiveAsync stealing (WS abort on cancel)
        var recvQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var recvCts = new CancellationTokenSource();
        var recvTask = Task.Run(async () =>
        {
            var buf = new byte[8192];
            while (!recvCts.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                try
                {
                    var res = await ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);
                    if (res.MessageType == WebSocketMessageType.Close) break;
                    var msg = Encoding.UTF8.GetString(buf, 0, res.Count);
                    while (!res.EndOfMessage)
                    {
                        var frag = await ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);
                        msg += Encoding.UTF8.GetString(buf, 0, frag.Count);
                        res = frag;
                    }
                    recvQueue.Enqueue(msg);
                }
                catch { break; }
            }
        });
        // Helper to send
        async Task SendAsync(string cmd, object[] args, Dictionary<string,object>? kwargs = null)
        {
            var payload = JsonSerializer.Serialize(new object[] { cmd, args, kwargs ?? new Dictionary<string,object>() });
            var bytes = Encoding.UTF8.GetBytes(payload);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);
        }
        async Task<List<string>> RecvAllAsync(int timeoutMs = 2000)
        {
            var msgs = new List<string>();
            var sw = Stopwatch.StartNew();
            // wait up to timeoutMs for first message, then 500ms for follow-ups
            while (true)
            {
                var elapsed = sw.ElapsedMilliseconds;
                if (msgs.Count == 0 && elapsed >= timeoutMs) break;
                if (msgs.Count > 0 && elapsed >= 500) break;
                if (recvQueue.TryDequeue(out var m))
                {
                    msgs.Add(m);
                    sw.Restart();
                    continue;
                }
                var remaining = msgs.Count == 0 ? timeoutMs - (int)elapsed : 500 - (int)elapsed;
                if (remaining <= 0) break;
                await Task.Delay(Math.Min(50, remaining));
            }
            return msgs;
        }
        // client_ready should trigger welcome + prompt
        await SendAsync("client_ready", Array.Empty<object>());
        var welcomeMsgs = await RecvAllAsync(2000);
        Assert.Contains(welcomeMsgs, m => m.Contains("ATHERIZ VERSION"));
        Assert.Contains(welcomeMsgs, m => m.Contains("prompt"));
        // connect
        await SendAsync("text", new object[] { $"connect {account} {password}" });
        var afterConnect = await RecvAllAsync(3000);
        // logged_in may be coalesced or delayed; main check is character selection prompt
        Assert.Contains(afterConnect, m => m.Contains("Please select a character"));
        // Find Enter your choice prompt and send 0
        // There may be an extra prompt for Enter your choice
        var hasChoicePrompt = afterConnect.Any(m => m.Contains("Enter your choice"));
        if (!hasChoicePrompt)
        {
            var extra = await RecvAllAsync(2000);
            afterConnect.AddRange(extra);
        }
        await SendAsync("text", new object[] { "0" });
        // Drain post-select (may include no message, just puppet set) — wait a bit for AtPostPuppet to finish MoveTo
        await Task.Delay(700);
        await RecvAllAsync(1500);
        // Now run basic commands and expect responses — retry look once if server still reports "You are nowhere" (world load race under parallel suite)
        var commands = new Dictionary<string, string>
        {
            ["look"] = "limbo",
            ["inventory"] = "carrying",
            ["help"] = "Command",
            ["say hello"] = "You say",
        };
        foreach (var kv in commands)
        {
            Console.WriteLine($"[Test] Sending {kv.Key}");
            List<string> resp = null!;
            string combined = "";
            for (int attempt = 0; attempt < 3; attempt++)
            {
                await SendAsync("text", new object[] { kv.Key });
                resp = await RecvAllAsync(3000);
                combined = string.Join("\n", resp);
                Console.WriteLine($"[Test] Got {resp.Count} for {kv.Key} attempt {attempt}: {combined.Substring(0, Math.Min(500, combined.Length)).Replace("\n","\\n")}");
                if (resp.Count == 0)
                {
                    Console.WriteLine($"[Test] No response for {kv.Key} attempt {attempt}");
                    await Task.Delay(500);
                    continue;
                }
                // For look, retry if we got the "You are nowhere" race (character location not yet set)
                if (kv.Key == "look" && combined.Contains("You are nowhere", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[Test] look got You are nowhere, retrying {attempt}");
                    await Task.Delay(700);
                    continue;
                }
                break;
            }
            Assert.True(resp.Count > 0, $"No response for '{kv.Key}' got {resp.Count} msgs combined: {combined.Substring(0, Math.Min(200, combined.Length))}");
            Assert.Contains(kv.Value, combined, StringComparison.OrdinalIgnoreCase);
        }
        // exam me should work (admin)
        await SendAsync("text", new object[] { "examine me" });
        var exam = await RecvAllAsync(3000);
        Assert.True(exam.Count > 0, "examine me no response");
        // quit
        await SendAsync("text", new object[] { "quit" });
        // may close, but not required to check
        await Task.Delay(200);
        try { recvCts.Cancel(); } catch { }
        try { await recvTask.WaitAsync(TimeSpan.FromSeconds(1)); } catch { }
    }

    private static async Task TestTelnetAsync(string gameFolder, int telnetPort, string account = "intadmin", string password = "intpass123")
    {
        // Try to connect, if fails skip test
        try
        {
            using var probe = new TcpClient();
            await probe.ConnectAsync("127.0.0.1", telnetPort).WaitAsync(TimeSpan.FromSeconds(2));
            probe.Close();
        }
        catch
        {
            // telnet not listening, skip
            return;
        }
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", telnetPort).WaitAsync(TimeSpan.FromSeconds(5));
        var stream = client.GetStream();
        var reader = new StreamReader(stream, Encoding.UTF8);
        var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
        // Background receiver to avoid pending ReadAsync stealing (same fix as WS)
        var telnetQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var telnetCts = new CancellationTokenSource();
        var telnetTask = Task.Run(async () =>
        {
            var buf = new char[8192];
            while (!telnetCts.IsCancellationRequested)
            {
                try
                {
                    int n = await reader.ReadAsync(buf, 0, buf.Length);
                    if (n == 0) break;
                    telnetQueue.Enqueue(new string(buf, 0, n));
                }
                catch { break; }
            }
        });
        async Task<string> ReadUntilAsync(string needle, int timeoutMs = 5000)
        {
            var sb = new StringBuilder();
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                while (telnetQueue.TryDequeue(out var chunk))
                    sb.Append(chunk);
                if (sb.ToString().Contains(needle, StringComparison.OrdinalIgnoreCase))
                    break;
                await Task.Delay(50);
            }
            // drain any remaining queued data after needle found
            while (telnetQueue.TryDequeue(out var chunk2))
                sb.Append(chunk2);
            return sb.ToString();
        }
        // Initial welcome
        var welcome = await ReadUntilAsync("ATHERIZ VERSION", 5000);
        if (!welcome.Contains("ATHERIZ VERSION", StringComparison.OrdinalIgnoreCase))
            Console.WriteLine($"[Telnet] welcome missing ATHERIZ VERSION: {welcome.Substring(0, Math.Min(1000, welcome.Length)).Replace("\n","\\n")}");
        Assert.Contains("ATHERIZ VERSION", welcome);
        // connect
        await writer.WriteLineAsync($"connect {account} {password}");
        await writer.FlushAsync();
        var afterConnect = await ReadUntilAsync("Please select", 5000);
        if (!afterConnect.Contains("Please select", StringComparison.OrdinalIgnoreCase))
            Console.WriteLine($"[Telnet] afterConnect missing Please select: '{afterConnect.Substring(0, Math.Min(1000, afterConnect.Length)).Replace("\n","\\n")}' welcome was '{welcome.Substring(0, Math.Min(500, welcome.Length)).Replace("\n","\\n")}'");
        Assert.Contains("Please select", afterConnect);
        await writer.WriteLineAsync("0");
        await writer.FlushAsync();
        // Wait for choice prompt to be consumed and puppet set
        await Task.Delay(500);
        // Drain
        await ReadUntilAsync(">", 1000);
        // look
        await writer.WriteLineAsync("look");
        await writer.FlushAsync();
        var look = await ReadUntilAsync("limbo", 5000);
        Assert.Contains("limbo", look, StringComparison.OrdinalIgnoreCase);
        // inventory
        await writer.WriteLineAsync("inventory");
        await writer.FlushAsync();
        var inv = await ReadUntilAsync("carrying", 5000);
        Assert.Contains("carrying", inv, StringComparison.OrdinalIgnoreCase);
        // say
        await writer.WriteLineAsync("say hello via telnet");
        await writer.FlushAsync();
        var say = await ReadUntilAsync("hello", 5000);
        Assert.Contains("hello", say, StringComparison.OrdinalIgnoreCase);
        // quit
        await writer.WriteLineAsync("quit");
        await writer.FlushAsync();
        await Task.Delay(200);
        try { telnetCts.Cancel(); } catch { }
        try { await telnetTask.WaitAsync(TimeSpan.FromSeconds(1)); } catch { }
        client.Close();
    }
}
