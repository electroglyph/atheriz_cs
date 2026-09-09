using Atheriz.Server.Cli;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Hosting;

// `new` must stop when the game folder was refused, not move into it.
[Collection("Ported")]
public class GameFolderRejectTests
{
    [Fact]
    public async Task New_WithRejectedFolderName_DoesNotChdirOrStartServer()
    {
        // Behavior: when folder creation is rejected (invalid identifier),
        // `new` must stop without changing directory or starting a server.
        // Today CreateGameFolder at GameTemplateGenerator.cs:19-23 only prints
        // and HandleNewAsync at StopHandler.cs:421-424 unconditionally chdirs
        // and proceeds — with an existing directory it even starts a server
        // for a folder it refused to create.
        var root = Path.Combine(Path.GetTempPath(), "atheriz_newrej_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var origCwd = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "my-game"));
            var result = await StopHandler.HandleNewAsync(new[] { "my-game", "--foreground" });
            Assert.False(result);
            Assert.Equal(root, Directory.GetCurrentDirectory());
        }
        finally
        {
            try { Directory.SetCurrentDirectory(origCwd); } catch { }
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void NewOverwrite_FailedValidation_LeavesSaveDirIntact()
    {
        // The save-leaf wipe ran BEFORE credential validation — a failed
        // failed prompt destroyed the world it then refused to build. The wipe
        // now runs after validation, so refusal leaves everything intact.
        var root = Path.Combine(Path.GetTempPath(), "atheriz_newval_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var folder = Path.Combine(root, "s1game");
        Directory.CreateDirectory(folder);
        var save = Path.Combine(folder, "save");
        Directory.CreateDirectory(save);
        var stale = Path.Combine(save, "stale.txt");
        File.WriteAllText(stale, "precious");
        var oldUser = Environment.GetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME");
        var oldPass = Environment.GetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD");
        var oldIn = Console.In;
        Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME", null);
        Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD", null);
        Console.SetIn(new StringReader(""));
        try
        {
            Assert.False(GameTemplateGenerator.CreateGameFolder(folder, overwrite: true));
            Assert.True(File.Exists(stale), "refused overwrite must not wipe the save leaf");
        }
        finally
        {
            Console.SetIn(oldIn);
            Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME", oldUser);
            Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD", oldPass);
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void NewOverwrite_StalePidFile_ProceedsWithWipe()
    {
        // Only a LIVE verified server refuses --overwrite; a stale pid file
        // (aborted setup) must stay re-creatable.
        var root = Path.Combine(Path.GetTempPath(), "atheriz_newstale_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var folder = Path.Combine(root, "s1stale");
        Directory.CreateDirectory(folder);
        var save = Path.Combine(folder, "save");
        Directory.CreateDirectory(save);
        File.WriteAllText(Path.Combine(save, "stale.txt"), "x");
        File.WriteAllText(Path.Combine(save, "server.pid"), "42424242");
        var oldUser = Environment.GetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME");
        var oldPass = Environment.GetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD");
        Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME", "s1admin");
        Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD", "s1Pass123");
        try
        {
            Assert.True(GameTemplateGenerator.CreateGameFolder(folder, overwrite: true));
            Assert.False(File.Exists(Path.Combine(save, "stale.txt")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME", oldUser);
            Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD", oldPass);
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void NewOverwrite_StaleAdminToken_WipedForRegeneration()
    {
        // A stale secret/admin.token must not survive as the "fresh" game's
        // credential: overwrite deletes it, and the token helper recreates a
        // fresh one when missing (server startup regenerates at boot).
        var root = Path.Combine(Path.GetTempPath(), "atheriz_newtok_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var folder = Path.Combine(root, "s1tok");
        Directory.CreateDirectory(folder);
        var secret = Path.Combine(folder, "secret");
        Directory.CreateDirectory(secret);
        var token = Path.Combine(secret, "admin.token");
        File.WriteAllText(token, "stale-token");
        var oldUser = Environment.GetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME");
        var oldPass = Environment.GetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD");
        Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME", "s1admin");
        Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD", "s1Pass123");
        try
        {
            Assert.True(GameTemplateGenerator.CreateGameFolder(folder, overwrite: true));
            Assert.False(File.Exists(token), "stale admin.token must not survive --overwrite");
            var fresh = AdminToken.EnsureToken(secret);
            Assert.False(string.IsNullOrEmpty(fresh));
            Assert.True(File.Exists(token), "token helper must regenerate when missing");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME", oldUser);
            Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD", oldPass);
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static int FindFreePort()
    {
        using var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        return ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
    }

    private static async Task<string> RunProcessAsync(string fileName, string args, string? workingDir, Dictionary<string, string>? env, int timeoutMs)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
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
        var sb = new System.Text.StringBuilder();
        using var proc = new System.Diagnostics.Process { StartInfo = psi };
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
        var sw = System.Diagnostics.Stopwatch.StartNew();
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

    [Fact(Timeout = 120000)]
    public async Task NewOverwrite_RefusesLiveServerFolder()
    {
        // `new --overwrite` wiped the save leaf with zero liveness probes,
        // racing a running server. A verified-live server.pid now refuses the
        // overwrite before anything is deleted.
        if (!OperatingSystem.IsLinux()) return;
        const string repoRoot = "/home/anon/atheriz-cs";
        var dll = $"{repoRoot}/src/Atheriz.Server/bin/Debug/net8.0/Atheriz.Server.dll";
        if (!File.Exists(dll)) return;
        var port = FindFreePort();
        var telnetPort = FindFreePort();
        var tmp = Path.Combine(Path.GetTempPath(), $"atheriz_newlive_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        var gameFolder = Path.Combine(tmp, "s1live");
        var env = new Dictionary<string, string>
        {
            ["ATHERIZ_SUPERUSER_USERNAME"] = "s1liveadmin",
            ["ATHERIZ_SUPERUSER_PASSWORD"] = "s1LivePass123",
            ["ATHERIZ_TELNET_PORT"] = telnetPort.ToString(),
            ["Atheriz__TelnetPort"] = telnetPort.ToString(),
        };
        var oldUser = Environment.GetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME");
        var oldPass = Environment.GetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD");
        try
        {
            var output = await RunProcessAsync("bash", $"{repoRoot}/atheriz.sh new {gameFolder} --port {port} --telnet-port {telnetPort} --overwrite", repoRoot, env, 30000);
            Assert.True(await WaitForHealthAsync(port, 15000), $"Server did not become healthy. Output: {output}");
            var pidFile = Path.Combine(gameFolder, "save", "server.pid");
            Assert.True(File.Exists(pidFile));
            var dbFile = Path.Combine(gameFolder, "save", "database.sqlite3");
            Assert.True(File.Exists(dbFile));
            // Valid credentials (wipe would proceed) — but the live server refuses first.
            Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME", "s1other");
            Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD", "s1OtherPass123");
            Assert.False(GameTemplateGenerator.CreateGameFolder(gameFolder, overwrite: true));
            Assert.True(File.Exists(pidFile), "refused overwrite must not delete the pid file");
            Assert.True(File.Exists(dbFile), "refused overwrite must not delete the database");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME", oldUser);
            Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD", oldPass);
            try { await RunProcessAsync("bash", $"{repoRoot}/atheriz.sh stop --port {port}", repoRoot, null, 15000); } catch { }
            try
            {
                var pf = Path.Combine(gameFolder, "save", "server.pid");
                if (File.Exists(pf) && int.TryParse(File.ReadAllText(pf).Trim(), out var pid))
                    try { System.Diagnostics.Process.GetProcessById(pid).Kill(); } catch { }
            }
            catch { }
            try { await RunProcessAsync("bash", $"rm -rf \"{tmp}\"", null, null, 5000); } catch { }
        }
    }
}
