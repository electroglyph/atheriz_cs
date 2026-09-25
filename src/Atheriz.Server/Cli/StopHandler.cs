using Microsoft.Extensions.Configuration;

namespace Atheriz.Server.Cli;

public static class StopHandler
{
    internal static AtherizSettings EffectiveSettingsValue => EffectiveSettings;
    // Guarded lazy publish: the old `??=` could double-load (benign
    // discard) or hand a half-published/stale generation across an
    // Invalidate, and StopAsync's port/secret/savedir triple mixed
    // generations. One lock + volatile fast path; StopAsync snapshots once.
    private static readonly Lock _effLock = new();
    private static AtherizSettings? _effectiveCache;
    private static AtherizSettings EffectiveSettings
    {
        get
        {
            var snap = Volatile.Read(ref _effectiveCache);
            if (snap is not null) return snap;
            lock (_effLock)
            {
                snap = _effectiveCache;
                if (snap is null)
                {
                    snap = LoadSettingsFor(Directory.GetCurrentDirectory());
                    Volatile.Write(ref _effectiveCache, snap);
                }
                return snap;
            }
        }
    }
    internal static void InvalidateEffectiveSettings() { lock (_effLock) { _effectiveCache = null; } }

    // Config-only load scoped to a game folder (no DB touch): the background
    // parent resolves the child's expected port/banners from the target game
    // without changing directory. Relative paths in the result stay relative
    // to the given folder — callers combine them with it explicitly.
    internal static AtherizSettings LoadSettingsFor(string gameFolder)
    {
        // Prefer Global if it has been initialized from host; otherwise load from appsettings.json + env
        try
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(gameFolder)
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
    private static AtherizSettings LoadEffectiveSettings()
    {
        return LoadSettingsFor(Directory.GetCurrentDirectory());
    }

    // Verified kill for the pid-file path: the IsServerProcess gate runs
    // before signalling anything (a dropped gate would kill a foreign
    // process on pid reuse); release is owner-verified. Returns the exit code.
    internal static async Task<int> KillVerifiedPidAsync(int pid, int port, string pidFilePath)
    {
        Process? proc;
        try { proc = Process.GetProcessById(pid); }
        catch (ArgumentException) { Console.WriteLine("Process from PID file not found; removing stale PID file."); PidFile.ReleaseIfOwner(pidFilePath, pid); return 0; }
        catch (Exception ex) { Console.WriteLine($"Could not inspect PID {pid}: {ex.Message}"); return 1; }
        if (!PidFile.IsServerProcess(pid))
        {
            Console.WriteLine($"PID {pid} is not a verified Atheriz server process; refusing to terminate an unverified process.");
            return 1;
        }
        // Per-PID hold: the port being listened on by *someone* while this
        // pid is a server must never implicate this pid. Fail closed.
        if (!PidFile.IsProcessListeningOnPort(pid, port))
        {
            Console.WriteLine($"PID {pid} is not listening on port {port}; refusing to terminate an unverified process.");
            return 1;
        }
        Console.WriteLine($"Stopping server process with PID: {pid}...");
        // Terminal re-verify (mirroring RestartHandler's escalation
        // gate): the pid may have been recycled between the checks above
        // and the kill below — confirm file and process still agree.
        bool stillOurs = false;
        try { stillOurs = PidFile.TryReadPid(pidFilePath) == pid && PidFile.IsServerProcess(pid); } catch { }
        if (!stillOurs)
        {
            Console.WriteLine($"PID {pid} no longer names a verified server process; aborting kill.");
            return 1;
        }
        await ProcessHelper.TerminateAsync(proc).ConfigureAwait(false);
        if (File.Exists(pidFilePath))
        {
            try
            {
                bool stillRunning = false;
                try { stillRunning = !proc.HasExited; } catch { }
                // Owner-verified: only remove the file when it still names
                // the process just stopped — never a successor's pid file
                // across the kill/delete window (pid reuse).
                if (!stillRunning) { PidFile.ReleaseIfOwner(pidFilePath, pid); Console.WriteLine("Done."); return 0; }
                Console.WriteLine("Warning: Process still exists after kill.");
                return 1;
            }
            catch { }
        }
        return 0;
    }

    public static async Task<int> StopAsync(int? portOverride)
    {
        // Single-generation snapshot: port/secret/savedir must come from
        // one settings load, never a mix across a concurrent invalidate.
        var effective = EffectiveSettings;
        var port = portOverride ?? effective.WebserverPort;
        var secretPath = effective.SecretPath;
        var tlsOn = !string.IsNullOrEmpty(effective.SslCertFile);
        switch (await ShutdownClient.TryRequestShutdownAsync(port, secretPath, tlsOn).ConfigureAwait(false))
        {
            case ShutdownRequestResult.Accepted:
                Console.WriteLine("Graceful shutdown request accepted; the server will stop itself.");
                return 0;
            case ShutdownRequestResult.AuthRejected:
                // A live server refused us: abort here, never escalate into signals.
                return 1;
            default: break;
        }
        var savePath = effective.SavePath;
        var pidFilePath = PidFile.LocateServerPidFile(savePath);
        if (!File.Exists(pidFilePath))
        {
            // No pid file, so no pid to verify: never signal. A listening
            // port without an owner is reported, not killed.
            if (PidFile.IsPortListening(port))
            {
                Console.WriteLine($"Port {port} is listening but no pid file names its owner; refusing to terminate an unverified process.");
                return 1;
            }
            Console.WriteLine("No server process found.");
            return 1;
        }
        int? pid = PidFile.TryReadPid(pidFilePath);
        if (pid is null) { Console.WriteLine("Invalid PID file content."); return 1; }
        return await KillVerifiedPidAsync(pid.Value, port, pidFilePath).ConfigureAwait(false);
    }
}
