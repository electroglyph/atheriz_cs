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
            if (!string.IsNullOrEmpty(host))
            {
                if (!IsSafeHost(host!)) { Console.Error.WriteLine($"Invalid --host value: {host}"); return; }
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
                Console.Error.WriteLine($"bash spawn failed: {ex.Message}, trying direct");
                var psi2 = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetFullPath(folder),
                };
                psi2.ArgumentList.Add(dll);
                foreach (var a in argList) psi2.ArgumentList.Add(a);
                try
                {
                    // attach the log stream to the fallback child.
                    // Python passes the log fd as the daemon's stdout/stderr
                    // (atheriz.py:1403-1405); the .NET equivalent is piped
                    // redirect with a pump appending to the log. (The bash
                    // primary path above stays the durable route: its >>
                    // redirect survives spawner exit, this pump does not.)
                    psi2.RedirectStandardOutput = true;
                    psi2.RedirectStandardError = true;
                    var p2 = Process.Start(psi2);
                    if (p2 != null)
                    {
                        daemonPid = p2.Id;
                        object logGate = new();
                        void Pump(object? _, DataReceivedEventArgs e)
                        {
                            if (e.Data is null) return;
                            lock (logGate) File.AppendAllText(saveLog, e.Data + "\n");
                        }
                        p2.OutputDataReceived += Pump;
                        p2.ErrorDataReceived += Pump;
                        p2.BeginOutputReadLine();
                        p2.BeginErrorReadLine();
                    }
                }
                catch { }
            }
            if (daemonPid != -1)
            {
                Console.WriteLine($"Server started with PID: {daemonPid}");
                Console.WriteLine($"Server starting in background (PID {daemonPid}), log: {saveLog}");
                var effSettings = StopHandler.EffectiveSettingsValue;
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
}
