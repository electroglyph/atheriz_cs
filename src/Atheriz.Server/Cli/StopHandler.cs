using Microsoft.Extensions.Configuration;

namespace Atheriz.Server.Cli;

public static class StopHandler
{
    internal static AtherizSettings EffectiveSettingsValue => EffectiveSettings;
    // Guarded lazy publish: `??=` could double-load or hand a stale
    // generation across an Invalidate. One lock + volatile fast path.
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

    // Config-only load scoped to a game folder (no DB touch): the parent
    // resolves the child's expected port/banners from the target game without
    // changing directory. Relative paths stay relative to the given folder.
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
            // The engine's own appsettings.json (next to the binaries) must
            // NOT override the game folder's file: configuration is
            // later-provider-wins, so a last engine source silently repoints
            // the CLI at the wrong world (wrong port, wrong token).
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
    // process on pid reuse); release is owner-verified. With force the
    // caller accepts the pid-reuse risk and the gates are skipped.
    // Returns the exit code.
    internal static async Task<int> KillVerifiedPidAsync(int pid, int port, string pidFilePath, bool force = false)
    {
        Process? proc;
        try { proc = Process.GetProcessById(pid); }
        catch (ArgumentException) { Console.WriteLine("Process from PID file not found; removing stale PID file."); PidFile.ReleaseIfOwner(pidFilePath, pid); return 0; }
        catch (Exception ex) { Console.WriteLine($"Could not inspect PID {pid}: {ex.Message}"); return 1; }
        // The claim as read on entry: the terminal re-verify and the
        // owner-verified release both compare against the identity just
        // verified, not the bare pid a successor could reuse.
        PidClaim? claim = PidFile.TryReadClaim(pidFilePath);
        if (!force)
        {
            // A pid that started after the recorded claim time is a recycled
            // number: remove the stale claim and report nothing running.
            if (claim is PidClaim recycled && !PidFile.ClaimNamesLiveServer(recycled))
            {
                Console.WriteLine("Process from PID file not found; removing stale PID file.");
                PidFile.ReleaseIfOwner(pidFilePath, recycled);
                return 0;
            }
            if (!PidFile.IsServerProcess(pid))
            {
                Console.WriteLine($"PID {pid} is not a verified Atheriz server process; refusing to terminate an unverified process. Re-run with -f/--force to kill it anyway.");
                return 1;
            }
            // Per-PID hold: someone else listening on the port must never
            // implicate this pid. Fail closed.
            if (!PidFile.IsProcessListeningOnPort(pid, port))
            {
                Console.WriteLine($"PID {pid} is not listening on port {port}; refusing to terminate an unverified process. Re-run with -f/--force to kill it anyway.");
                return 1;
            }
            // Terminal re-verify: the pid may have been recycled since the
            // checks above, so the file must still hold this exact claim.
            bool stillOurs = false;
            try { stillOurs = claim is PidClaim c && c.Pid == pid && PidFile.ClaimNamesLiveServer(c); } catch { }
            if (!stillOurs)
            {
                Console.WriteLine($"PID {pid} no longer names a verified server process; aborting kill. Re-run with -f/--force to kill it anyway.");
                return 1;
            }
        }
        else Console.WriteLine($"Force-stopping PID {pid} without verification...");
        Console.WriteLine($"Stopping server process with PID: {pid}...");
        await ProcessHelper.TerminateAsync(proc).ConfigureAwait(false);
        if (!File.Exists(pidFilePath)) return 0;
        try
        {
            bool stillRunning = false;
            try { stillRunning = !proc.HasExited; } catch { }
            if (!stillRunning)
            {
                // Owner-verified: never delete a successor's claim (pid reuse).
                if (claim is PidClaim held) PidFile.ReleaseIfOwner(pidFilePath, held);
                else PidFile.ReleaseIfOwner(pidFilePath, pid);
                Console.WriteLine("Done."); return 0;
            }
            Console.WriteLine("Warning: Process still exists after kill.");
            return 1;
        }
        catch { }
        return 0;
    }

    // Force kill for the no-pid-file path: signal every process holding a
    // LISTEN socket on the port. Only reachable via -f/--force.
    internal static async Task<int> KillPortListenersAsync(int port, Func<int, List<int>>? findPids = null)
    {
        findPids ??= PidFile.FindPidsListeningOnPort;
        var pids = findPids(port);
        if (pids.Count == 0)
        {
            Console.WriteLine($"Port {port} is listening but no owning process could be identified; nothing killed.");
            return 1;
        }
        bool allDead = true;
        foreach (var pid in pids)
        {
            Process proc;
            try { proc = Process.GetProcessById(pid); }
            catch (ArgumentException) { continue; }
            catch (Exception ex) { Console.WriteLine($"Could not inspect PID {pid}: {ex.Message}"); allDead = false; continue; }
            Console.WriteLine($"Force-stopping listener PID {pid} on port {port}...");
            await ProcessHelper.TerminateAsync(proc).ConfigureAwait(false);
            try
            {
                if (!proc.HasExited) { Console.WriteLine($"Warning: PID {pid} still exists after kill."); allDead = false; }
            }
            catch { }
        }
        if (allDead) { Console.WriteLine("Done."); return 0; }
        return 1;
    }

    public static async Task<int> StopAsync(int? portOverride, bool force = false)
    {
        // One settings generation: port/secret/savedir never mix.
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
            // No pid file, so no pid to verify: never signal by default; a
            // listening port without an owner is reported unless force accepts the guess.
            if (PidFile.IsPortListening(port))
            {
                if (!force)
                {
                    Console.WriteLine($"Port {port} is listening but no pid file names its owner; refusing to terminate an unverified process. Re-run with -f/--force to kill the listener anyway.");
                    return 1;
                }
                return await KillPortListenersAsync(port).ConfigureAwait(false);
            }
            Console.WriteLine("No server process found.");
            return 1;
        }
        int? pid = PidFile.TryReadPid(pidFilePath);
        if (pid is null) { Console.WriteLine("Invalid PID file content."); return 1; }
        return await KillVerifiedPidAsync(pid.Value, port, pidFilePath, force).ConfigureAwait(false);
    }
}
