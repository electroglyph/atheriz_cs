using Microsoft.Extensions.Configuration;

namespace Atheriz.Server.Cli;

public static class StopHandler
{
    internal static AtherizSettings EffectiveSettingsValue => EffectiveSettings;
    private static AtherizSettings? _effectiveCache;
    private static AtherizSettings EffectiveSettings => _effectiveCache ??= LoadEffectiveSettings();
    internal static void InvalidateEffectiveSettings() => _effectiveCache = null;
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
            if (s is not null) return s;
        }
        catch { }
        return AtherizSettings.Global;
    }

    // Shared verified-kill funnel for the port-scan-found and pid-file stop
    // paths. Both the IsServerProcess + IsProcessListeningOnPort gates run on
    // every path (a dropped gate would silently kill a foreign process);
    // kill escalates through the shared KillProcessWithDots helper and
    // release is owner-verified. A null pidFilePath selects the scan-found
    // chatter/release shape, a non-null one the pid-file shape — every
    // user-visible string is preserved per path.
    internal static async Task KillVerifiedPidAsync(int pid, int port, string? pidFilePath)
    {
        if (pidFilePath is null)
        {
            // The finder is best-effort: hold a verified per-PID check on
            // BOTH stop paths before signalling any process.
            if (!PidFile.IsServerProcess(pid) || !PidFile.IsProcessListeningOnPort(pid, port))
            {
                Console.WriteLine($"Found process (PID: {pid}) on port {port} is not a verified server; refusing to terminate.");
                return;
            }
            string foundName = "process";
            try { using var pn = Process.GetProcessById(pid); foundName = pn.ProcessName; } catch { }
            Console.WriteLine($"Found process {foundName} (PID: {pid}) listening on port {port}...");
            try
            {
                var proc2 = Process.GetProcessById(pid);
                Console.Write($"Stopping server process with PID: {pid}...");
                // Same terminate→wait→kill escalation as the pid-file
                // path (KillProcessWithDots): timeout chatter and
                // Kill-failure swallowing live in the helper now.
                await ProcessHelper.KillProcessWithDots(proc2).ConfigureAwait(false);
                Console.WriteLine(" Done.");
                try
                {
                    var cwdL = new FileInfo($"/proc/{pid}/cwd").LinkTarget; if (!string.IsNullOrEmpty(cwdL)) { var pf = Path.Combine(cwdL, "save", "server.pid"); PidFile.ReleaseIfOwner(pf, pid); }
                }
                catch { }
                return;
            }
            catch (Exception ex) { Console.WriteLine($"Error stopping found process: {ex.Message}"); return; }
        }
        Process? proc = null;
        try { proc = Process.GetProcessById(pid); }
        catch (ArgumentException) { Console.WriteLine("Process from PID file not found; removing stale PID file."); PidFile.ReleaseIfOwner(pidFilePath, pid); return; }
        catch (Exception ex) { Console.WriteLine($"Could not inspect PID {pid}: {ex.Message}"); return; }
        // Per-PID hold only: the port being listened on by *someone* while
        // this pid is a server must never implicate this pid .
        bool listening = PidFile.IsProcessListeningOnPort(pid, port);
        if (!listening)
        {
            Console.WriteLine($"PID {pid} is not listening on port {port}; refusing to terminate an unverified process.");
            return;
        }
        bool isServer = PidFile.IsServerProcess(pid);
        if (!isServer)
        {
            // name the failed check — this branch fired on the
            // process-identity gate, not the port-listening gate above.
            Console.WriteLine($"PID {pid} is not a verified Atheriz server process; refusing to terminate an unverified process.");
            return;
        }
        Console.Write($"Stopping server process with PID: {pid}...");
        await ProcessHelper.KillProcessWithDots(proc).ConfigureAwait(false);
        Console.WriteLine(" Done.");
        if (File.Exists(pidFilePath))
        {
            try
            {
                bool stillRunning = false;
                try { stillRunning = !proc.HasExited; } catch { }
                // Owner-verified: only remove the file when it still names
                // the process just stopped — never a successor's pid file
                // across the kill/delete window (pid reuse).
                if (!stillRunning) PidFile.ReleaseIfOwner(pidFilePath, pid);
                else Console.WriteLine("\nWarning: Process still exists after kill.");
            }
            catch { }
        }
    }

    public static async Task HandleStopAsync(string[] a)
    {
        var port = ArgumentParser.ParsePort(a) ?? EffectiveSettings.WebserverPort;
        var secretPath = EffectiveSettings.SecretPath;
        var tlsOn = !string.IsNullOrEmpty(EffectiveSettings.SslCertFile);
        switch (await ShutdownClient.TryRequestShutdownAsync(port, secretPath, tlsOn).ConfigureAwait(false))
        {
            case ShutdownRequestResult.Accepted:
                Console.WriteLine("Graceful shutdown request accepted; the server will stop itself.");
                return;
            case ShutdownRequestResult.AuthRejected:
                // A live server refused us: abort here, never escalate into signals.
                return;
            default: break;
        }
        var savePath = EffectiveSettings.SavePath;
        var pidFilePath = PidFile.LocateServerPidFile(savePath);
        if (!File.Exists(pidFilePath))
        {
            Console.WriteLine($"Scanning for process listening on port {port}...");
            if (PidFile.TryFindPidListeningOnPort(port, out var foundPid))
            {
                await KillVerifiedPidAsync(foundPid, port, pidFilePath: null).ConfigureAwait(false);
                return;
            }
            Console.WriteLine("No server process found.");
            return;
        }
        int? pid = PidFile.TryReadPid(pidFilePath);
        if (pid is null) Console.WriteLine("Invalid PID file content.");
        if (pid is not null)
        {
            await KillVerifiedPidAsync(pid.Value, port, pidFilePath).ConfigureAwait(false);
        }
    }
}
