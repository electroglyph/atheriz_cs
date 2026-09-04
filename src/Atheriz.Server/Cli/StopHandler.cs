using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Atheriz.Core;
using Atheriz.Core.Settings;
using Atheriz.Server.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace Atheriz.Server.Cli;

public static class StopHandler
{
    internal static AtherizSettings EffectiveSettingsValue => EffectiveSettings;
    private static AtherizSettings? _effectiveCache;
    private static AtherizSettings EffectiveSettings => _effectiveCache ??= LoadEffectiveSettings();
    private static AtherizSettings LoadEffectiveSettings()
    {
        // Prefer Global if it has been initialized from host; otherwise load from appsettings.json + env
        try
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
                .AddEnvironmentVariables();
            try { builder.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true); } catch { }
            var cfg = builder.Build();
            var s = cfg.GetSection("Atheriz").Get<AtherizSettings>();
            if (s != null) return s;
        }
        catch { }
        return AtherizSettings.Global;
    }

    public static async Task HandleStopAsync(string[] a)
    {
        var port = ArgumentParser.ParsePort(a) ?? EffectiveSettings.WebserverPort;
        var secretPath = EffectiveSettings.SecretPath;
        if (await TryRequestShutdownAsync(port, secretPath))
        {
            Console.WriteLine("Graceful shutdown request accepted; the server will stop itself.");
            return;
        }
        var savePath = EffectiveSettings.SavePath;
        var pidFilePath = Path.Combine(savePath, "server.pid");
        if (!File.Exists(pidFilePath))
        {
            try
            {
                if (PidFile.TryFindPidListeningOnPort(port, out var lpid2))
                {
                    try
                    {
                        var cwdLink = new FileInfo($"/proc/{lpid2}/cwd").LinkTarget;
                        if (!string.IsNullOrEmpty(cwdLink))
                        {
                            var alt = Path.Combine(cwdLink, "save", "server.pid");
                            if (File.Exists(alt)) pidFilePath = alt;
                        }
                    }
                    catch { }
                    try
                    {
                        var psi = new ProcessStartInfo { FileName = "readlink", Arguments = $"/proc/{lpid2}/cwd", RedirectStandardOutput = true, UseShellExecute = false };
                        using var pr = Process.Start(psi);
                        if (pr != null) { var cwd2 = pr.StandardOutput.ReadToEnd().Trim(); pr.WaitForExit(300); if (!string.IsNullOrEmpty(cwd2)) { var alt2 = Path.Combine(cwd2, "save", "server.pid"); if (File.Exists(alt2)) pidFilePath = alt2; } }
                    }
                    catch { }
                }
            }
            catch { }
            if (!File.Exists(pidFilePath))
            {
                try
                {
                    var cur = new DirectoryInfo(Directory.GetCurrentDirectory());
                    for (int i = 0; i < 6 && cur != null; i++) { var p = Path.Combine(cur.FullName, "save", "server.pid"); if (File.Exists(p)) { pidFilePath = p; break; } cur = cur.Parent; }
                }
                catch { }
            }
        }
        if (!File.Exists(pidFilePath))
        {
            Console.WriteLine($"Scanning for process listening on port {port}...");
            if (PidFile.TryFindPidListeningOnPort(port, out var foundPid))
            {
                string foundName = "process";
                try { using var pn = Process.GetProcessById(foundPid); foundName = pn.ProcessName; } catch { }
                Console.WriteLine($"Found process {foundName} (PID: {foundPid}) listening on port {port}...");
                try
                {
                    var proc2 = Process.GetProcessById(foundPid);
                    Console.Write($"Stopping server process with PID: {foundPid}...");
                    RequestTerminate(proc2);
                    if (!await WaitForExitDotsAsync(proc2, 30))
                    {
                        Console.WriteLine();
                        Console.Write("Process did not stop in time. Killing...");
                        try { proc2.Kill(entireProcessTree: false); } catch (Exception ex) { Console.WriteLine($" Failed: {ex.Message}"); return; }
                        await WaitForExitDotsAsync(proc2, 30);
                    }
                    Console.WriteLine(" Done.");
                    try
                    {
                        var cwdL = new FileInfo($"/proc/{foundPid}/cwd").LinkTarget; if (!string.IsNullOrEmpty(cwdL)) { var pf = Path.Combine(cwdL, "save", "server.pid"); if (File.Exists(pf)) try { File.Delete(pf); } catch { } }
                    }
                    catch { }
                    return;
                }
                catch (Exception ex) { Console.WriteLine($"Error stopping found process: {ex.Message}"); return; }
            }
            Console.WriteLine("No server process found.");
            return;
        }
        int? pid = null;
        try { pid = int.Parse(File.ReadAllText(pidFilePath, Encoding.UTF8).Trim()); } catch { Console.WriteLine("Invalid PID file content."); }
        if (pid != null)
        {
            Process? proc = null;
            try { proc = Process.GetProcessById(pid.Value); }
            catch (ArgumentException) { Console.WriteLine("Process from PID file not found; removing stale PID file."); try { File.Delete(pidFilePath); } catch { } return; }
            catch (Exception ex) { Console.WriteLine($"Could not inspect PID {pid.Value}: {ex.Message}"); return; }
            bool listening = PidFile.IsProcessListeningOnPort(pid.Value, port);
            if (!listening) listening = IsPortListeningStatic(port) && PidFile.IsServerProcess(pid.Value);
            if (!listening)
            {
                Console.WriteLine($"PID {pid} is not listening on port {port}; refusing to terminate an unverified process.");
                return;
            }
            bool isServer = PidFile.IsServerProcess(pid.Value);
            if (!isServer)
            {
                Console.WriteLine($"PID {pid} is not listening on port {port}; refusing to terminate an unverified process.");
                return;
            }
            Console.Write($"Stopping server process with PID: {pid}...");
            RequestTerminate(proc);
            if (!await WaitForExitDotsAsync(proc, 50))
            {
                Console.Write(" Timeout! Force killing...");
                try { proc.Kill(entireProcessTree: false); } catch (Exception ex) { Console.WriteLine($" Failed: {ex.Message}"); return; }
                await WaitForExitDotsAsync(proc, 30);
            }
            Console.WriteLine(" Done.");
            if (File.Exists(pidFilePath))
            {
                try
                {
                    bool stillRunning = false;
                    try { stillRunning = !proc.HasExited; } catch { }
                    if (!stillRunning) File.Delete(pidFilePath);
                    else Console.WriteLine("\nWarning: Process still exists after kill.");
                }
                catch { }
            }
        }
    }

    // Port of atheriz.py stop_server terminate() (SIGTERM) before kill() (SIGKILL).
    private static void RequestTerminate(Process proc)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                if (NativeMethods.kill(proc.Id, 15) == 0) return;
            }
        }
        catch { }
        try { proc.Kill(entireProcessTree: false); } catch { }
    }

    private static async Task<bool> WaitForExitDotsAsync(Process proc, int tenths)
    {
        for (int i = 0; i < tenths; i++)
        {
            try { if (proc.HasExited) return true; } catch { return true; }
            await Task.Delay(100);
            Console.Write(".");
        }
        try { return proc.HasExited; } catch { return true; }
    }

    private static class NativeMethods
    {
        [DllImport("libc", SetLastError = true)]
        internal static extern int kill(int pid, int sig);
    }

    public static async Task HandleReloadAsync(string[] a)
    {
        var port = ArgumentParser.ParsePort(a) ?? EffectiveSettings.WebserverPort;
        var secretPath = EffectiveSettings.SecretPath;
        var tokenFile = Path.Combine(secretPath, "admin.token");
        if (!File.Exists(tokenFile)) { Console.WriteLine("Error: admin.token not found. Is the server running?"); return; }
        string token;
        try { token = File.ReadAllText(tokenFile, Encoding.UTF8).Trim(); } catch { Console.WriteLine("Could not read admin.token"); return; }
        var settingsTmp = EffectiveSettings;
        var tlsOn = !string.IsNullOrEmpty(settingsTmp.SslCertFile);
        var url = $"{(tlsOn ? "https" : "http")}://localhost:{port}/_internal/hot_reload";
        Console.WriteLine($"Triggering hot reload at {url}...");
        try
        {
            using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (a, b, c, d) => true };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Add("X-Admin-Token", token);
            var sw = Stopwatch.StartNew();
            var resp = await client.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            sw.Stop();
            if (resp.IsSuccessStatusCode)
            {
                try
                {
                    var doc = JsonDocument.Parse(body);
                    var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : "ok";
                    var msg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : body;
                    if (status == "ok") { Console.WriteLine($"Success! {msg}"); Console.WriteLine($"Reload took {sw.Elapsed.TotalMilliseconds:F2}ms"); }
                    else Console.WriteLine($"Failed: {msg}");
                }
                catch { Console.WriteLine($"Response: {body}"); }
            }
            else Console.WriteLine($"Failed with HTTP {(int)resp.StatusCode}: {body}");
        }
        catch (Exception ex) { Console.WriteLine($"Error connecting to server: {ex.Message}"); }
    }

    // Port of atheriz.py:1112-1161 restart: stop, wait for old PID, then start again.
    // Returns true when --foreground was given (caller falls through to foreground start).
    public static async Task<bool> HandleRestartAsync(string[] a)
    {
        var port = ArgumentParser.ParsePort(a);
        var host = ArgumentParser.ParseHost(a);
        var fg = ArgumentParser.HasFlag(a, "--foreground", "-f");
        var sw = Stopwatch.StartNew();
        await HandleStopAsync(a);
        var savePath2 = EffectiveSettings.SavePath;
        var pidPath2 = Path.Combine(savePath2, "server.pid");
        if (File.Exists(pidPath2))
        {
            try
            {
                var oldPid = int.Parse(File.ReadAllText(pidPath2, Encoding.UTF8).Trim());
                Console.Write($"Waiting for server (PID {oldPid}) to stop...");
                await ProcessHelper.WaitForPidExitAsync(oldPid);
                Console.WriteLine(" Done.");
            }
            catch { }
        }
        else await Task.Delay(500);

        if (fg) { Console.WriteLine($"Restart took {sw.Elapsed.TotalMilliseconds:F2}ms"); return true; }
        var spawnArgs = new List<string>();
        if (port != null) { spawnArgs.Add("--port"); spawnArgs.Add(port.ToString()!); }
        if (host != null) { spawnArgs.Add("--host"); spawnArgs.Add(host); }
        await SpawnDaemonAsync(spawnArgs.ToArray(), Directory.GetCurrentDirectory());
        Console.WriteLine($"Restart took {sw.Elapsed.TotalMilliseconds:F2}ms");
        return false;
    }

    public static async Task HandleResetAsync(string[] a)
    {
        bool force = ArgumentParser.HasFlag(a, "--force", "-f") || ArgumentParser.HasFlag(a, "--yes", null) || ArgumentParser.HasFlag(a, "-y", null);
        var port = ArgumentParser.ParsePort(a) ?? EffectiveSettings.WebserverPort;
        var host = ArgumentParser.ParseHost(a);
        var savePath = EffectiveSettings.SavePath;
        var pidPath = Path.Combine(savePath, "server.pid");
        bool isRunning = false;
        int? pid = null;
        if (File.Exists(pidPath))
        {
            try { pid = int.Parse(File.ReadAllText(pidPath, Encoding.UTF8).Trim()); isRunning = pid != null && PidFile.IsServerProcess(pid.Value); } catch { }
        }
        if (!force)
        {
            Console.WriteLine("WARNING: This will delete ALL game data. This action cannot be undone.");
            if (isRunning) Console.WriteLine("The server is currently running and will be stopped.");
            Console.Write("Are you sure you want to continue? [y/N] ");
            var resp = Console.ReadLine();
            if (!string.Equals(resp, "y", StringComparison.OrdinalIgnoreCase)) { Console.WriteLine("Aborted."); return; }
        }
        try
        {
            var props = IPGlobalProperties.GetIPGlobalProperties();
            var listeners = props.GetActiveTcpListeners();
            var watchPorts = new List<int> { port };
            if (EffectiveSettings.TelnetEnabled) watchPorts.Add(EffectiveSettings.TelnetPort);
            foreach (var ep in listeners)
            {
                if (watchPorts.Contains(ep.Port))
                {
                    // Our own server holds these ports; it is stopped below. Anything else aborts.
                    if (isRunning && pid != null && PidFile.IsProcessListeningOnPort(pid.Value, ep.Port)) continue;
                    Console.WriteLine($"Port {ep.Port} still listening; abort");
                    return;
                }
            }
        }
        catch { }

        if (isRunning && pid != null)
        {
            Console.WriteLine("Stopping server...");
            await HandleStopAsync(a);
            Console.Write($"Waiting for server (PID {pid}) to stop...");
            await ProcessHelper.WaitForPidExitAsync(pid.Value);
            Console.WriteLine(" Done.");
            await Task.Delay(500);
        }

        try { Atheriz.Core.Persistence.AtherizDbContext.CloseDatabase(); } catch { }
        try { Atheriz.Core.Persistence.AtherizDbContextFactory.CloseDatabase(); } catch { }

        Console.WriteLine("Deleting game data...");
        try
        {
            if (Directory.Exists(savePath)) Directory.Delete(savePath, recursive: true);
        }
        catch (Exception ex) { Console.WriteLine($"Failed to delete save: {ex.Message}"); }
        try { Atheriz.Core.Utils.PathGuards.GuardSavePath(savePath); } catch (Exception ex) { Console.WriteLine(ex.Message); return; }
        Directory.CreateDirectory(savePath);
        Atheriz.Core.Utils.FsUtil.TryChmod0700(savePath);

        try { Atheriz.Core.Persistence.AtherizDbContext.ReopenDatabase(); } catch { }
        try { Atheriz.Core.Persistence.AtherizDbContextFactory.ReopenDatabase(); } catch { }

        Console.WriteLine("Setting up new world...");
        try
        {
            // Port of atheriz.py reset: local initial_setup.do_setup() with no superuser (limbo world only).
            Atheriz.Core.InitialSetup.DoSetup(savePath);
            Console.WriteLine("Success! New world created.");
        }
        catch (Exception ex) { Console.WriteLine($"Setup failed: {ex.Message}"); return; }

        // Port of atheriz.py:1629 reset always daemonizes after setup.
        var resetSpawnArgs = new List<string> { "--port", port.ToString() };
        if (host != null) { resetSpawnArgs.Add("--host"); resetSpawnArgs.Add(host); }
        await SpawnDaemonAsync(resetSpawnArgs.ToArray(), Directory.GetCurrentDirectory());
    }

    public static async Task HandleCreateAsync(string[] a)
    {
        var port = ArgumentParser.ParsePort(a);
        var filtered = a.Where((v, i) => !(v == "--port" && i + 1 < a.Length) && !(i > 0 && a[i - 1] == "--port") && !v.StartsWith("--port=", StringComparison.Ordinal)).ToArray();
        if (filtered.Length < 3)
        {
            Console.Error.WriteLine("Usage: atheriz create <accountname> <charactername> <password> [--port N]");
            Console.Error.WriteLine("atheriz: error: the following arguments are required: accountname, charactername, password");
            Environment.Exit(2);
        }
        var accName = filtered[0]; var charName = filtered[1]; var pw = filtered[2];
        var portVal = port ?? EffectiveSettings.WebserverPort;
        var secretPath = EffectiveSettings.SecretPath;
        var tokenFile = Path.Combine(secretPath, "admin.token");
        if (File.Exists(tokenFile))
        {
            try
            {
                var token = File.ReadAllText(tokenFile, Encoding.UTF8).Trim();
                var tlsOn = !string.IsNullOrEmpty(EffectiveSettings.SslCertFile);
                var url = $"{(tlsOn ? "https" : "http")}://localhost:{portVal}/_internal/create_account";
                var payload = JsonSerializer.Serialize(new { account_name = accName, char_name = charName, password = pw });
                using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (a, b, c, d) => true };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
                var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
                req.Headers.Add("X-Admin-Token", token);
                var resp = await client.SendAsync(req);
                var body = await resp.Content.ReadAsStringAsync();
                try
                {
                    var doc = JsonDocument.Parse(body);
                    var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : "error";
                    var msg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : body;
                    Console.WriteLine(msg);
                    if (status == "ok" || status == "error") return;
                }
                catch { Console.WriteLine(body); return; }
            }
            catch { }
        }
        Console.WriteLine("No running server detected; creating directly against the database.");
        Console.WriteLine("Loading existing data...");
        var savePath = EffectiveSettings.SavePath;
        try { Atheriz.Core.Utils.PathGuards.GuardSavePath(savePath); } catch (Exception ex) { Console.WriteLine(ex.Message); return; }
        Directory.CreateDirectory(savePath);
        try
        {
            using var db = new Atheriz.Core.Persistence.AtherizDbContext(savePath);
            db.Database.EnsureCreated();
            Atheriz.Core.Globals.ObjectRegistry.LoadObjects(savePath);
        }
        catch (Exception ex) { Console.WriteLine($"Load failed: {ex.Message}"); }
        // Port of atheriz.py:1456-1459 offline path: at_char_create against the database.
        ServerEvents.AtCharCreate(accName, charName, pw);
    }

    public static async Task<bool> HandleNewAsync(string[] a)
    {
        bool overwrite = ArgumentParser.HasFlag(a, "--overwrite", null) || ArgumentParser.HasFlag(a, "--force", null);
        var filtered = a.Where((v, i) =>
            !(v == "--port" && i + 1 < a.Length) && !(i > 0 && a[i - 1] == "--port") && !v.StartsWith("--port=", StringComparison.Ordinal) &&
            !(v == "--telnet-port" && i + 1 < a.Length) && !(i > 0 && a[i - 1] == "--telnet-port") && !v.StartsWith("--telnet-port=", StringComparison.Ordinal) &&
            v != "--host" && !(i > 0 && a[i - 1] == "--host") && !v.StartsWith("--host=", StringComparison.Ordinal) &&
            v != "--foreground" && v != "-f" &&
            v != "--overwrite" && v != "--force"
        ).ToArray();
        if (filtered.Length < 1)
        {
            Console.Error.WriteLine("Usage: atheriz new <foldername> [--port N] [--host HOST] [--foreground|-f]");
            Console.Error.WriteLine("atheriz: error: the following arguments are required: foldername");
            Environment.Exit(2);
        }
        var folder = filtered[0];
        var gameName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(gameName)) gameName = folder;
        var folderAbs = Path.GetFullPath(folder);
        GameTemplateGenerator.CreateGameFolder(folderAbs, gameName, overwrite);

        Console.WriteLine($"\nChanging directory to '{folder}'...");
        try { Directory.SetCurrentDirectory(folderAbs); } catch (Exception ex) { Console.Error.WriteLine($"Failed to change directory: {ex.Message}"); return false; }

        Console.WriteLine("Starting server...");
        bool foreground = ArgumentParser.HasFlag(a, "--foreground", "-f");
        if (foreground)
        {
            return true;
        }
        else
        {
            await SpawnDaemonAsync(a, folderAbs);
            return false;
        }
    }

    // Port of atheriz.py:1285 spawn_daemon: Popen start --foreground with stdout/stderr to save/server.log.
    public static async Task SpawnDaemonAsync(string[] origArgs, string folder)
    {
        try
        {
            var dll = typeof(Program).Assembly.Location;
            var port = ArgumentParser.ParsePort(origArgs);
            var host = ArgumentParser.ParseHost(origArgs);
            var telnetPort = ArgumentParser.ParseTelnetPort(origArgs);
            var argList = new List<string> { "start", "--foreground" };
            if (port.HasValue) { argList.Add("--port"); argList.Add(port.Value.ToString()); }
            if (telnetPort.HasValue) { argList.Add("--telnet-port"); argList.Add(telnetPort.Value.ToString()); }
            if (!string.IsNullOrEmpty(host)) { argList.Add("--host"); argList.Add(host!); }
            var saveLog = Path.Combine(Path.GetFullPath(folder), "save", "server.log");
            Directory.CreateDirectory(Path.GetDirectoryName(saveLog)!);
            try
            {
                var logInfo = new FileInfo(saveLog);
                if (logInfo.Exists && logInfo.Length > 5 * 1024 * 1024)
                {
                    for (int i = 5; i >= 1; i--)
                    {
                        var src = i == 1 ? saveLog : Path.Combine(Path.GetDirectoryName(saveLog)!, $"server.log.{i - 1}");
                        var dst = Path.Combine(Path.GetDirectoryName(saveLog)!, $"server.log.{i}");
                        if (File.Exists(src)) try { File.Move(src, dst, overwrite: true); } catch { }
                    }
                }
            }
            catch { }
            var escapedDll = dll.Replace("\"", "\\\"", StringComparison.Ordinal);
            var escapedArgs = string.Join(" ", argList.Select(a => $"\"{a.Replace("\"", "\\\"", StringComparison.Ordinal)}\""));
            var escapedLog = saveLog.Replace("\"", "\\\"", StringComparison.Ordinal).Replace("$", "\\$", StringComparison.Ordinal).Replace("`", "\\`", StringComparison.Ordinal);
            Console.WriteLine($"Spawning server in background. Logging to: {saveLog}");
            var innerCmd = $"dotnet \"{escapedDll}\" {escapedArgs}";
            var shellCmd = $"nohup {innerCmd} >> \"{escapedLog}\" 2>&1 & echo $!";
            var psi = new ProcessStartInfo
            {
                FileName = "bash",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetFullPath(folder),
            };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(shellCmd);
            string pidStr = "";
            int daemonPid = -1;
            try
            {
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    pidStr = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit(2000);
                    var last = pidStr.Split(new[] { '\n', '\r', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
                    if (int.TryParse(last, out var p)) daemonPid = p;
                    if (daemonPid == -1) { foreach (var tok in pidStr.Split(new[] { '\n', '\r', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)) if (int.TryParse(tok, out p)) { daemonPid = p; break; } }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"bash spawn failed: {ex.Message}, trying direct");
                var psi2 = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetFullPath(folder),
                };
                psi2.ArgumentList.Add(escapedDll);
                foreach (var a in argList) psi2.ArgumentList.Add(a);
                try
                {
                    var logFs = new FileStream(saveLog, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    psi2.RedirectStandardOutput = false;
                    psi2.RedirectStandardError = false;
                    var p2 = Process.Start(psi2);
                    if (p2 != null) daemonPid = p2.Id;
                    logFs.Dispose();
                }
                catch { }
            }
            if (daemonPid != -1)
            {
                Console.WriteLine($"Server started with PID: {daemonPid}");
                Console.WriteLine($"Server starting in background (PID {daemonPid}), log: {saveLog}");
                var effSettings = EffectiveSettings;
                int effPort = port ?? effSettings.WebserverPort;
                string effHost = host ?? effSettings.WebserverInterface;
                string dispHost = effHost.Contains(':') ? $"[{effHost}]" : effHost;
                bool hasSsl = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ATHERIZ_SSL_CERTFILE")) || !string.IsNullOrEmpty(effSettings.SslCertFile);
                string effScheme = hasSsl ? "https" : "http";
                if (effHost == "0.0.0.0" || effHost == "::") Console.WriteLine($"Web server running on {effScheme}://localhost:{effPort}");
                else Console.WriteLine($"Web server running on {effScheme}://{dispHost}:{effPort}");
                Console.WriteLine($"Web server listening on {effScheme}://{dispHost}:{effPort}");
                if (effSettings.WebsocketEnabled)
                {
                    string wssScheme = hasSsl ? "wss" : "ws";
                    Console.WriteLine($"WebSocket server available at {wssScheme}://{dispHost}:{effPort}/ws");
                }
            }
            else Console.WriteLine("Failed to spawn server daemon.");
        }
        catch (Exception ex) { Console.Error.WriteLine($"Failed to spawn daemon: {ex.Message}"); }
        await Task.CompletedTask;
    }

    public static void HandleTest(string[] a)
    {
        // Port of atheriz.py test: leading 'core' selects the core suite (here: the solution tests).
        if (a.Length > 0 && a[0] == "core") a = a.Skip(1).ToArray();
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "test " + string.Join(" ", a.Select(x => $"\"{x}\"")),
            UseShellExecute = false,
        };
        try
        {
            var proc = Process.Start(psi);
            proc?.WaitForExit();
            Environment.Exit(proc?.ExitCode ?? 0);
        }
        catch (Exception ex) { Console.WriteLine($"Failed to run tests: {ex.Message}"); }
    }

    private static string? FindTokenFile(string secretPath, int port)
    {
        var cand = Path.Combine(secretPath, "admin.token");
        if (File.Exists(cand)) return cand;
        try
        {
            var cur = new DirectoryInfo(Directory.GetCurrentDirectory());
            for (int i = 0; i < 6 && cur != null; i++) { var p = Path.Combine(cur.FullName, "secret", "admin.token"); if (File.Exists(p)) return p; var p2 = Path.Combine(cur.FullName, "save", "..", "secret", "admin.token"); if (File.Exists(Path.GetFullPath(p2))) return Path.GetFullPath(p2); cur = cur.Parent; }
        }
        catch { }
        try
        {
            if (PidFile.TryFindPidListeningOnPort(port, out var lpid))
            {
                try
                {
                    var link = Path.Combine($"/proc/{lpid}/cwd");
                    if (Directory.Exists(link)) { }
                    var realCwd = new FileInfo($"/proc/{lpid}/cwd").LinkTarget;
                    if (!string.IsNullOrEmpty(realCwd)) { var p3 = Path.Combine(realCwd, "secret", "admin.token"); if (File.Exists(p3)) return p3; }
                }
                catch { }
                try
                {
                    var psi = new ProcessStartInfo { FileName = "readlink", Arguments = $"/proc/{lpid}/cwd", RedirectStandardOutput = true, UseShellExecute = false };
                    using var pr = Process.Start(psi);
                    if (pr != null) { var cwd2 = pr.StandardOutput.ReadToEnd().Trim(); pr.WaitForExit(500); if (!string.IsNullOrEmpty(cwd2)) { var p4 = Path.Combine(cwd2, "secret", "admin.token"); if (File.Exists(p4)) return p4; } }
                }
                catch { }
            }
        }
        catch { }
        try
        {
            foreach (var baseDir in new[] { Directory.GetCurrentDirectory(), "/tmp" })
                foreach (var f in Directory.GetFiles(baseDir, "admin.token", SearchOption.AllDirectories))
                    if (f.EndsWith("secret/admin.token", StringComparison.Ordinal)) return f;
        }
        catch { }
        return null;
    }

    private static async Task<bool> TryRequestShutdownAsync(int port, string secretPath)
    {
        var tokenFile = FindTokenFile(secretPath, port);
        if (tokenFile == null || !File.Exists(tokenFile)) return false;
        string token;
        try { token = File.ReadAllText(tokenFile, Encoding.UTF8).Trim(); } catch { return false; }
        var tlsOn = !string.IsNullOrEmpty(EffectiveSettings.SslCertFile);
        var url = $"{(tlsOn ? "https" : "http")}://localhost:{port}/_internal/shutdown";
        Console.WriteLine("Requesting graceful shutdown via internal API...");
        try
        {
            using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (a, b, c, d) => true };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Add("X-Admin-Token", token);
            var resp = await client.SendAsync(req);
            if (resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                try
                {
                    var doc = JsonDocument.Parse(body);
                    var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : "";
                    Console.WriteLine($"Internal shutdown response: {body}");
                    if (status == "ok") { Console.WriteLine("Server has completed shutdown tasks."); return true; }
                }
                catch { return false; }
            }
        }
        catch { }
        Console.WriteLine("Could not contact server for graceful shutdown (server might be hung or stopped).");
        return false;
    }

    private static bool IsPortListeningStatic(int port)
    {
        try
        {
            var props = IPGlobalProperties.GetIPGlobalProperties();
            foreach (var ep in props.GetActiveTcpListeners()) if (ep.Port == port) return true;
        }
        catch { }
        return false;
    }
}
