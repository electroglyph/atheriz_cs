using System.Net;
using Atheriz.Server.Hosting;

namespace Atheriz.Server.Cli;

// Background launcher for start/new/restart/reset: spawns a detached child
// (`start --foreground`) with inherited stdio (never redirected — a pipe
// would die with the parent) and waits boundedly for readiness before
// printing the operator banners itself and exiting.
public static class DaemonSpawner
{
    internal const string DaemonEnvKey = "ATHERIZ_DAEMON";

    // Bounded readiness wait: game boots with plugin discovery, database
    // setup, and DoStartup run ~46s on ordinary hardware, so a tight bound
    // expires on every healthy slow boot and orphans a running server behind
    // an exit-1 refusal. A genuinely broken child still fails fast through
    // the died branch below; Ctrl-C interrupts the wait via OnCancel.
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan PollCadence = TimeSpan.FromMilliseconds(200);
    /// <summary>
    /// Readiness poll for a spawned child: ready when the pid file names our
    /// own child (a rival starter winning the gap must never satisfy it —
    /// identity comes from the live child handle, since pid reuse is
    /// impossible while it is open) and its port answers. Dies fast when the
    /// child exits; the timeout (and Ctrl-C via <paramref name="cancel"/>)
    /// ends the wait for a hung boot. The probes are parameters so tests can
    /// drive slow, dead, and rival starters without processes.
    /// </summary>
    public static async Task<(bool Ready, bool Died)> WaitForReadyAsync(
        Func<int?> readClaim,
        int childPid,
        Func<bool> portOpen,
        Func<bool> childAlive,
        TimeSpan timeout,
        TimeSpan cadence,
        CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(readClaim);
        ArgumentNullException.ThrowIfNull(portOpen);
        ArgumentNullException.ThrowIfNull(childAlive);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        try { timeoutCts.CancelAfter(timeout); } catch { }
        while (!timeoutCts.Token.IsCancellationRequested)
        {
            bool alive;
            try { alive = childAlive(); }
            catch { return (false, true); }
            if (!alive) return (false, true);
            int? claim = null;
            try { claim = readClaim(); } catch { }
            bool claimOk = false;
            try { claimOk = claim is int c && c == childPid && childPid != -1; } catch { }
            bool open = false;
            try { open = portOpen(); } catch { }
            if (claimOk && open) return (true, false);
            try { await Task.Delay(cadence, timeoutCts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
        return (false, false);
    }

    /// <summary>
    /// The "already running" refusal for a live rival claimant, or null when
    /// there is no rival to name: no claimant, our own parent pid, or our own
    /// still-booting child. A child of ours that holds a live claim is simply
    /// not ready yet — reporting it as a rival lies to the operator and
    /// orphans a healthy server behind the refusal.
    /// </summary>
    public static string? RivalRefusal(int? rival, int parentPid, int childPid) =>
        rival switch
        {
            null => null,
            _ when rival == parentPid => null,
            _ when childPid != -1 && rival == childPid => null,
            _ => $"{PidFile.AlreadyRunningMessagePrefix} {rival}",
        };

    // Shared host gate with ServerHost.RunForegroundAsync: one rule, one
    // message shape, fail-fast exit 2.
    public static bool IsSafeHost(string? host)
        => host is null || IPAddress.TryParse(host, out _);

    public static async Task<int> SpawnStart(string gameFolderAbs, int? port, string? host, int? telnetPort)
    {
        ArgumentException.ThrowIfNullOrEmpty(gameFolderAbs);
        if (!IsSafeHost(host)) { Console.Error.WriteLine($"Invalid --host value: {host}"); return 2; }
        if (!TryResolveChildExe(out var fileName, out var dllArg, out var exeError))
        { Console.Error.WriteLine(exeError); return 1; }

        var settings = StopHandler.LoadSettingsFor(gameFolderAbs);
        var bannerSettings = WithOverrides(settings, gameFolderAbs, port, host);
        int expectedPort = bannerSettings.WebserverPort;
        bool webEnabled = bannerSettings.WebserverEnabled;
        var pidPath = Path.Combine(CombineWithGame(gameFolderAbs, settings.SavePath), "server.pid");
        var logPath = Path.Combine(CombineWithGame(gameFolderAbs, settings.SavePath), "server.log");

        if (PidFile.IsLiveClaim(pidPath, out int ownerPid))
        { Console.WriteLine($"{PidFile.AlreadyRunningMessagePrefix} {ownerPid}"); return 1; }

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = gameFolderAbs,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (dllArg is not null) psi.ArgumentList.Add(dllArg);
        psi.ArgumentList.Add("start");
        psi.ArgumentList.Add("--foreground");
        if (port is not null) { psi.ArgumentList.Add("--port"); psi.ArgumentList.Add(port.Value.ToString()); }
        if (host is not null) { psi.ArgumentList.Add("--host"); psi.ArgumentList.Add(host); }
        if (telnetPort is not null) { psi.ArgumentList.Add("--telnet-port"); psi.ArgumentList.Add(telnetPort.Value.ToString()); }
        psi.Environment[DaemonEnvKey] = "1";

        Process? child;
        try { child = Process.Start(psi); }
        catch (Exception ex) { Console.Error.WriteLine($"Failed to start server process: {ex.Message}"); return 1; }
        if (child is null) { Console.Error.WriteLine("Failed to start server process."); return 1; }
        using (child)
        {
            Console.WriteLine("Starting server...");
            int parentPid = Environment.ProcessId;
            bool interrupted = false;
            using var timeoutCts = new CancellationTokenSource(ReadinessTimeout);
            void OnCancel(object? s, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                interrupted = true;
                try { timeoutCts.Cancel(); } catch { }
            }
            Console.CancelKeyPress += OnCancel;
            try
            {
                // The readiness claim must name OUR child: a rival
                // starter winning the gap would otherwise satisfy this poll
                // and declare ready for a server we did not start.
                int childPid;
                try { childPid = child.Id; } catch { childPid = -1; }
                // Identity comes from the live child handle, not
                // IsServerProcess: pid reuse is impossible while this
                // handle is open, and IsServerProcess reads /proc
                // cmdlines so it always fails for dotnet-hosted
                // servers on Windows (dotnet.exe, no /proc) — that
                // flapped every background start into a false "did
                // not become ready" on healthy servers. The c ==
                // childPid match still refuses a rival starter's claim.
                var (ready, died) = await WaitForReadyAsync(
                    () => PidFile.TryReadPid(pidPath),
                    childPid,
                    () => !webEnabled || PidFile.IsPortListening(expectedPort),
                    () => !child.HasExited,
                    ReadinessTimeout,
                    PollCadence,
                    timeoutCts.Token).ConfigureAwait(false);

                if (ready)
                {
                    if (webEnabled) foreach (var line in ServerHost.FormatBannerLines(bannerSettings)) Console.WriteLine(line);
                    else Console.WriteLine(ServerHost.HeadlessBanner);
                    return 0;
                }
                if (interrupted)
                {
                    try { Console.Error.WriteLine($"Spawn interrupted; child process {child.Id} may still be starting."); }
                    catch { Console.Error.WriteLine("Spawn interrupted; a child process may still be starting."); }
                    return 1;
                }
                if (!died && PidFile.IsLiveClaim(pidPath, out int rival) && RivalRefusal(rival, parentPid, childPid) is string refusal)
                { Console.WriteLine(refusal); return 1; }
                int exitCode = -1;
                try { exitCode = child.HasExited ? child.ExitCode : exitCode; } catch { }
                try { Console.Error.WriteLine($"Server did not become ready (child process {child.Id}, exit {exitCode})."); }
                catch { Console.Error.WriteLine("Server did not become ready."); }
                Console.Error.WriteLine($"--- {logPath} tail ---");
                Console.Error.WriteLine(ReadLogTail(logPath));
                return 1;
            }
            finally
            {
                Console.CancelKeyPress -= OnCancel;
            }
        }
    }

    private static string CombineWithGame(string gameFolderAbs, string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) return gameFolderAbs;
        try { return Path.GetFullPath(Path.Combine(gameFolderAbs, configured)); }
        catch { return gameFolderAbs; }
    }

    // Effective settings for banners and the readiness wait: configured
    // values with CLI overrides applied, cert paths resolved against the
    // game folder (the parent may run from another directory).
    private static AtherizSettings WithOverrides(AtherizSettings settings, string gameFolderAbs, int? port, string? host)
    {
        return new AtherizSettings
        {
            WebserverInterface = host ?? settings.WebserverInterface,
            WebserverPort = port ?? settings.WebserverPort,
            SslCertFile = ResolvePath(settings.SslCertFile, gameFolderAbs),
            SslKeyFile = ResolvePath(settings.SslKeyFile, gameFolderAbs),
            AllowInsecureTlsFallback = settings.AllowInsecureTlsFallback,
            WebsocketEnabled = settings.WebsocketEnabled,
            WebserverEnabled = settings.WebserverEnabled,
        };
    }

    private static string? ResolvePath(string? configured, string gameFolderAbs)
    {
        if (string.IsNullOrWhiteSpace(configured)) return configured;
        try { return Path.IsPathRooted(configured) ? configured : Path.GetFullPath(Path.Combine(gameFolderAbs, configured)); }
        catch { return configured; }
    }

    private static string ReadLogTail(string logPath)
    {
        try
        {
            if (!File.Exists(logPath)) return "(no server.log yet)";
            return string.Join(Environment.NewLine, File.ReadAllLines(logPath).TakeLast(20));
        }
        catch (Exception ex) { return $"(could not read log: {ex.Message})"; }
    }

    // Resolve the real server exe without assuming `dotnet <dll>`: a file
    // name starting with atheriz is our own single-file publish (re-exec
    // it); otherwise the framework-dependent `dotnet` + dll fallback.
    internal static bool TryResolveChildExe(out string fileName, out string? dllArg, out string? error)
    {
        fileName = "";
        dllArg = null;
        error = null;
        string? self = null;
        string baseDir;
        try { self = Environment.ProcessPath; } catch { }
        try { baseDir = AppContext.BaseDirectory; }
        catch (Exception ex) { error = $"Cannot locate server binary: {ex.Message}"; return false; }
        var dllProbe = Path.Combine(baseDir, "Atheriz.Server.dll");
        try
        {
            if (!string.IsNullOrEmpty(self)
                && Path.GetFileName(self).StartsWith("atheriz", StringComparison.OrdinalIgnoreCase)
                && File.Exists(self))
            {
                fileName = self;
                return true;
            }
            if (File.Exists(dllProbe))
            {
                fileName = !string.IsNullOrEmpty(self) && File.Exists(self) ? self : "dotnet";
                dllArg = dllProbe;
                return true;
            }
        }
        catch (Exception ex) { error = $"Cannot resolve server binary: {ex.Message}"; return false; }
        error = $"Cannot resolve server binary (no {dllProbe}).";
        return false;
    }
}
