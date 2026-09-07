using System.Diagnostics;

namespace Atheriz.Server.Cli;

public static class DaemonSpawner
{
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
