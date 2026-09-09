using System.Diagnostics;

namespace Atheriz.Server.Cli;

public static class DaemonSpawner
{
    // Bash single-quote armor: inside '...' nothing expands ($, `, \, !
    // are all literal), so --host/--port values cannot inject commands.
    // An embedded ' ends the quote, inserts an escaped quote, and reopens.
    internal static string BashQuote(string s) => "'" + s.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    // Host charset allowlist (names, IPv4/IPv6 incl. brackets + zone id).
    // Defense in depth behind BashQuote: fail closed before spawning.
    internal static bool IsSafeHost(string h) =>
        h.Length > 0 && h.Length <= 255 && h.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' or ':' or '[' or ']' or '%');

    // Port of atheriz.py:1285 spawn_daemon: Popen start --foreground with stdout/stderr to save/server.log.
    // Returns false when nothing was spawned (invalid args, spawn failure):
    // the caller holds the pid claim and must release it on false, or the
    // pid file points at a dead CLI.
    public static async Task<bool> SpawnDaemonAsync(string[] origArgs, string folder)
    {
        bool spawned = false;
        try
        {
            var dll = typeof(Program).Assembly.Location;
            var port = ArgumentParser.ParsePort(origArgs);
            var host = ArgumentParser.ParseHost(origArgs);
            var telnetPort = ArgumentParser.ParseTelnetPort(origArgs);
            var argList = new List<string> { "start", "--foreground" };
            if (port.HasValue) { argList.Add("--port"); argList.Add(port.Value.ToString()); }
            if (telnetPort.HasValue) { argList.Add("--telnet-port"); argList.Add(telnetPort.Value.ToString()); }
            if (!string.IsNullOrEmpty(host))
            {
                if (!IsSafeHost(host!)) { Console.Error.WriteLine($"Invalid --host value: {host}"); return false; }
                argList.Add("--host"); argList.Add(host!);
            }
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
            var escapedDll = BashQuote(dll);
            var escapedArgs = string.Join(" ", argList.Select(BashQuote));
            var escapedLog = BashQuote(saveLog);
            Console.WriteLine($"Spawning server in background. Logging to: {saveLog}");
            // BashQuote already returns a fully single-quoted word — do NOT wrap
            // it in extra double quotes (that would pass literal quote chars
            // to dotnet and to the log redirect, breaking the spawn).
            var innerCmd = $"dotnet {escapedDll} {escapedArgs}";
            var shellCmd = $"nohup {innerCmd} >> {escapedLog} 2>&1 & echo $!";
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
                // Bash-less host: the bash redirect above is what keeps
                // server.log alive after the spawner exits. An in-process pump
                // would die with this process and leave the child writing to
                // a readerless pipe (block/SIGPIPE, lost log) despite
                // "Logging to:" being printed — so fail loudly instead of
                // spawning a time-bomb daemon. Use --foreground on such hosts.
                Console.Error.WriteLine($"bash spawn failed: {ex.Message}. Cannot daemonize without bash; not spawning (use --foreground instead).");
                return false;
            }
            if (daemonPid != -1)
            {
                spawned = true;
                Console.WriteLine($"Server started with PID: {daemonPid}");
                Console.WriteLine($"Server starting in background (PID {daemonPid}), log: {saveLog}");
                // Display values come from the explicit spawn flags plus the
                // shipped defaults (the child's baseline): the child runs in
                // the target folder with its own resolved config, so the
                // parent's settings must never feed these lines. Inherited
                // environment survives into the child, so the env TLS probe
                // stays valid. Anything else is in save/server.log.
                var shippedDefaults = new Atheriz.Core.Settings.AtherizSettings();
                int effPort = port ?? shippedDefaults.WebserverPort;
                string effHost = host ?? shippedDefaults.WebserverInterface;
                string dispHost = effHost.Contains(':') ? $"[{effHost}]" : effHost;
                bool hasSsl = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ATHERIZ_SSL_CERTFILE"));
                string effScheme = hasSsl ? "https" : "http";
                if (effHost == "0.0.0.0" || effHost == "::") Console.WriteLine($"Web server running on {effScheme}://localhost:{effPort}");
                else Console.WriteLine($"Web server running on {effScheme}://{dispHost}:{effPort}");
                Console.WriteLine($"Web server listening on {effScheme}://{dispHost}:{effPort}");
                if (shippedDefaults.WebsocketEnabled)
                {
                    string wssScheme = hasSsl ? "wss" : "ws";
                    Console.WriteLine($"WebSocket server available at {wssScheme}://{dispHost}:{effPort}/ws");
                }
            }
            else Console.WriteLine("Failed to spawn server daemon.");
        }
        catch (Exception ex) { Console.Error.WriteLine($"Failed to spawn daemon: {ex.Message}"); }
        await Task.CompletedTask;
        return spawned;
    }
}
