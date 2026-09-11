using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Atheriz.Server.Infrastructure;

/// <summary>
/// Faithful port of PID handling at <c>atheriz/atheriz.py:475-555</c> and <c>spawn_daemon:1154-1236</c>.
/// Uses atomic <c>FileStream(FileMode.CreateNew)</c> mirroring <c>open(pid_file, "x")</c> / <c>os.open O_EXCL</c>,
/// and <c>UnixFileMode 0o600</c> via <c>File.SetUnixFileMode</c> where platform supports.
/// </summary>
public sealed class PidFile : IDisposable
{
    public string PidPath { get; }
    private readonly int _ownPid;
    private bool _acquired;
    private bool _disposed;

    private PidFile(string pidPath, bool acquired, int ownPid)
    {
        PidPath = pidPath;
        _acquired = acquired;
        _ownPid = ownPid;
    }

    /// <summary>
    /// Whether <paramref name="pid"/> is one of this engine's server processes.
    /// A bare <c>Contains("dotnet")</c> name match is NOT enough: it also matches the
    /// test host (which loads <c>Atheriz.Server.dll</c>). For dotnet-hosted processes we
    /// additionally require the command line to mention Atheriz, so `stop` can never
    /// terminate an unverified process on pid reuse.
    /// Also guards zombie via HasExited (nearest C# equiv).
    /// </summary>
    public static bool IsServerProcess(int pid)
    {
        try
        {
            var proc = Process.GetProcessById(pid);
            try
            {
                if (proc.HasExited) return false; // mirrors psutil.STATUS_ZOMBIE
            }
            catch { }
            string name;
            try { name = proc.ProcessName ?? ""; }
            catch { return false; }
            var lower = name.ToLowerInvariant();
            // Only this engine's processes are ever servers, so only its
            // shapes are trusted: single-file publishes (module filename
            // below) and `dotnet Atheriz.Server.dll` (command line below).
            // Bare process-name prefixes (python*/atheriz*) are NOT trusted:
            // a stale pid reused by an unrelated process would otherwise pass
            // the stop gates straight to SIGTERM/SIGKILL.
            // Fallback: check main module filename if available (helps when process name truncated)
            try
            {
                var mod = proc.MainModule?.FileName ?? "";
                var fileName = Path.GetFileName(mod).ToLowerInvariant();
                if (fileName.StartsWith("atheriz", StringComparison.Ordinal))
                    return true;
                // dotnet host: only trust when the command line names the server
                // assembly itself. A bare "Atheriz" substring is NOT enough: the
                // test host loads Atheriz.Server.dll but its command line names
                // the test assembly, so `stop` can never terminate a test run
                // on pid reuse.
                if (fileName.Contains("dotnet") || lower.Contains("dotnet"))
                    return HasServerCmdline(pid);
            }
        catch { }
        return false;
        }
        catch (ArgumentException)
        {
            // pid not found — mirrors psutil.pid_exists false
            return false;
        }
        catch (InvalidOperationException) { return false; }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// Reads <c>/proc/{pid}/cmdline</c> and reports whether it names the server
    /// assembly (<c>Atheriz.Server.dll</c>).
    /// Non-Linux or unreadable → false (fail closed).
    /// </summary>
    private static bool HasServerCmdline(int pid)
    {
        try
        {
            var cmdline = $"/proc/{pid}/cmdline";
            if (!File.Exists(cmdline)) return false;
            var text = File.ReadAllText(cmdline);
            return text.Contains("Atheriz.Server.dll", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>
    /// Check if any TCP listener is on port — approximates <c>atheriz/atheriz.py:782-800 _process_listening_by_port</c> + <c>psutil.net_connections</c>.
    /// Uses <c>IPGlobalProperties.GetActiveTcpListeners</c> (process-agnostic) as fallback.
    /// If port is not listening at all, we consider it safe to overwrite stale PID.
    /// Public: single home for the port-listening check (StopHandler used to dup it).
    /// </summary>
    public static bool IsPortListening(int port)
    {
        try
        {
            var props = IPGlobalProperties.GetIPGlobalProperties();
            var listeners = props.GetActiveTcpListeners();
            foreach (var ep in listeners)
                if (ep.Port == port) return true;
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Best-effort: find PID holding LISTEN on port via /proc (Linux) or lsof/ss fallback. Mirrors psutil.net_connections.
    /// Helper output is read asynchronously with a hard timeout : a
    /// stalled lsof/ss must never hang `stop`.
    /// </summary>
    public static bool TryFindPidListeningOnPort(int port, out int pid)
    {
        pid = -1;
        // Try lsof
        try
        {
            var psi = new ProcessStartInfo { FileName = "lsof", UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add($":{port}");
            psi.ArgumentList.Add("-sTCP:LISTEN");
            psi.ArgumentList.Add("-t");
            using var p = Process.Start(psi);
            if (p is not null)
            {
                string outp = ReadHelperOutput(p, TimeSpan.FromSeconds(5));
                // One split of the captured lsof output; both passes scan the
                // same array so verified servers keep priority over any holder.
                // Scoped to this path: the ss fallback below keeps its own
                // parse (different backend, different token shape).
                var lines = outp.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                    if (int.TryParse(line.Trim(), out var cand) && IsServerProcess(cand) && IsProcessListeningOnPort(cand, port)) { pid = cand; return true; }
                foreach (var line in lines)
                    if (int.TryParse(line.Trim(), out var cand) && IsProcessListeningOnPort(cand, port)) { pid = cand; return true; }
            }
        }
        catch { }
        // Try ss
        try
        {
            var psi2 = new ProcessStartInfo { FileName = "ss", UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
            psi2.ArgumentList.Add("-lptn");
            psi2.ArgumentList.Add($"sport = :{port}");
            using var p2 = Process.Start(psi2);
            if (p2 is not null)
            {
                string outp = ReadHelperOutput(p2, TimeSpan.FromSeconds(5));
                // parse pid=1234,
                foreach (var token in outp.Split(new[] { "pid=", "," }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var num = new string(token.TakeWhile(char.IsDigit).ToArray());
                    if (int.TryParse(num, out var cand) && IsServerProcess(cand) && IsProcessListeningOnPort(cand, port)) { pid = cand; return true; }
                }
            }
        }
        catch { }
        // Fallback: scan /proc
        try
        {
            if (Directory.Exists("/proc"))
            {
                // collect inodes for listening sockets on port via /proc/net/tcp*
                var targetInodes = GetListeningInodes(port, out _);
                if (targetInodes.Count > 0)
                {
                    // Two passes: verified servers first, then any process
                    // holding the socket (identical link resolution).
                    if (ScanProcForInode(targetInodes, serverOnly: true) is int verified) { pid = verified; return true; }
                    if (ScanProcForInode(targetInodes, serverOnly: false) is int holder) { pid = holder; return true; }
                }
            }
        }
        catch { }

        // /proc fd scan shared by both passes above: verified servers first,
        // then any socket holder. serverOnly gates the IsServerProcess
        // pre-filter and the fdinfo fallback, preserving each pass exactly.
        static int? ScanProcForInode(HashSet<string> inodes, bool serverOnly)
        {
            foreach (var dir in Directory.GetDirectories("/proc"))
            {
                var name = Path.GetFileName(dir);
                if (!int.TryParse(name, out var candPid)) continue;
                if (serverOnly && !IsServerProcess(candPid)) continue;
                try
                {
                    var fdDir = Path.Combine(dir, "fd");
                    if (!Directory.Exists(fdDir)) continue;
                    foreach (var fd in Directory.GetFiles(fdDir))
                    {
                        try
                        {
                            var link = File.ResolveLinkTarget(fd, true)?.ToString() ?? (serverOnly ? new FileInfo(fd).LinkTarget ?? "" : "");
                            // fallback via readlink
                            if (serverOnly && string.IsNullOrEmpty(link))
                            {
                                try { link = File.ReadAllText($"/proc/{candPid}/fdinfo/{Path.GetFileName(fd)}"); } catch { }
                            }
                            foreach (var ino in inodes)
                                if (link.Contains($"socket:[{ino}]")) return candPid;
                        }
                        catch { }
                    }
                }
                catch { }
            }
            return null;
        }
        return false;
    }

    /// <summary>
    /// shared /proc/net/tcp* LISTEN-inode parser (was duplicated in
    /// the port locator and the single-pid verifier). Returns the inodes of
    /// sockets in LISTEN state on <paramref name="port"/>.
    /// <paramref name="tablesRead"/> reports whether the tables were readable
    /// at all: false on non-Linux (no /proc/net/tcp) or on read errors, so
    /// callers can fail closed instead of degrading into a global check.
    /// </summary>
    private static HashSet<string> GetListeningInodes(int port, out bool tablesRead)
    {
        HashSet<string> targetInodes = [];
        tablesRead = false;
        foreach (var netFile in new[] { "/proc/net/tcp", "/proc/net/tcp6" })
        {
            if (!File.Exists(netFile)) continue;
            tablesRead = true;
            var lines = File.ReadAllLines(netFile);
            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 10) continue;
                var local = parts[1]; // 0100007F:0035
                var st = parts[3];
                if (st != "0A") continue; // LISTEN
                var portHex = local.Split(':').LastOrDefault();
                if (portHex is null) continue;
                if (int.TryParse(portHex, System.Globalization.NumberStyles.HexNumber, null, out var pnum) && pnum == port)
                {
                    var inode = parts[9];
                    targetInodes.Add(inode);
                }
            }
        }
        return targetInodes;
    }

    /// <summary>
    /// Reads a helper's stdout to end without ever blocking the caller past
    /// <paramref name="timeout"/>: async read + bounded wait, kill on expiry.
    /// Returns whatever was captured (possibly empty).
    /// </summary>
    private static string ReadHelperOutput(Process p, TimeSpan timeout)
    {
        try
        {
            var read = p.StandardOutput.ReadToEndAsync();
            if (!read.Wait(timeout)) { try { p.Kill(entireProcessTree: false); } catch { } }
            try { p.WaitForExit(1000); } catch { }
            return read.IsCompletedSuccessfully ? read.Result : "";
        }
        catch { return ""; }
    }

    /// <summary>
    /// Verify pid actually holds LISTEN on port (mirrors _process_listening_by_port).
    /// Fail closed: when per-PID verification is unavailable (no /proc tables
    /// on non-Linux, unreadable fd dir, unexpected errors) the answer is
    /// "not verified", never the global port check — degrading to the global
    /// check would let `stop` signal an unverified process on pid reuse.
    /// </summary>
    public static bool IsProcessListeningOnPort(int pid, int port)
    {
        // Quick check via /proc/net/tcp + fd as above for single pid
        try
        {
            var targetInodes = GetListeningInodes(port, out bool tablesRead);
            if (!tablesRead) return false;
            if (targetInodes.Count == 0) return false;
            var fdDir = $"/proc/{pid}/fd";
            if (!Directory.Exists(fdDir)) return false;
            foreach (var fd in Directory.GetFiles(fdDir))
            {
                try
                {
                    var link = File.ResolveLinkTarget(fd, true)?.ToString() ?? "";
                    foreach (var ino in targetInodes)
                        if (link.Contains($"socket:[{ino}]")) return true;
                }
                catch { }
            }
            return false;
        }
        catch { return false; }
    }

    /// <summary>
    /// The server.pid governing this game: always <c>{savePath}/server.pid</c>.
    /// Pinned to the target game : no listener-/proc/cwd probing and
    /// no directory-tree walk — those could return a FOREIGN game's pid file
    /// (nested checkouts, attacker-planted files) and signal the wrong world.
    /// Callers wanting discovery must do it explicitly, never implicitly here.
    /// </summary>
    public static string LocateServerPidFile(string savePath)
    {
        return Path.Combine(savePath, "server.pid");
    }

    /// <summary>
    /// Compatibility forwarder: <paramref name="port"/> was never read (the
    /// file is always <c>{savePath}/server.pid</c>). Kept so existing callers
    /// compile with zero behavior delta.
    /// </summary>
    public static string LocateServerPidFile(int port, string savePath)
    {
        _ = port;
        return LocateServerPidFile(savePath);
    }

    /// <summary>
    /// Live pid + fresh file = a concurrent starter still booting (the daemon
    /// parent holds the parent pid until it exits; the child re-claims right
    /// after). Wait bounded for the claim to go stale (file replaced/gone or
    /// pid dead); true means the caller should re-read and proceed, false
    /// means a genuinely running server that must be refused. An old file
    /// naming a live pid never waits — that server is already running.
    /// </summary>
    private static bool FreshLiveClaimWentStale(string pidPath, int oldPid)
    {
        try
        {
            if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(pidPath)).TotalSeconds >= 2.0)
                return false;
        }
        catch { return false; }
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        // Bounded poll without Thread.Sleep (banned in production): a fresh
        // ManualResetEventSlim waited with timeout is the accepted idle here.
        using var beat = new ManualResetEventSlim(false);
        while (DateTime.UtcNow < deadline)
        {
            beat.Wait(100);
            if (TryReadPid(pidPath) != oldPid) return true;
            if (!IsServerProcess(oldPid)) return true;
        }
        return false;
    }

    /// <summary>
    /// Attempts to atomically acquire the PID file at <c>{savePath}/server.pid</c>.
    /// Mirrors <c>atheriz/atheriz.py:486-555 start_server</c> PID race:
    ///   - if file exists: read old_pid, if IsServerProcess → fail ("already running")
    ///   - else remove stale and try CreateNew
    ///   - on FileExistsError re-read and verify again (up to 3 attempts)
    /// Also matches <c>spawn_daemon:1165-1235</c> concurrent spawn handling (no age
    /// gate: a live rival returns "already running" before the stale path).
    /// Returns true if acquired; caller must Dispose/Release.
    /// </summary>
    // Single source for the "already running" refusal: both stale/live
    // refusal sites below format this constant, and Program.cs keeps no
    // prose copy (a comment copy once let a grep-pin pass while the live
    // message lived here).
    internal const string AlreadyRunningMessagePrefix = "Server is already running with PID:";
    public static bool TryAcquire(string savePath, out PidFile? pidFile, out string? reason, int webserverPort = 9999)
    {
        pidFile = null;
        reason = null;

        // Guard — atheriz/atheriz.py:508-512 + PathGuards (Core; Server wrapper deleted as redundant)
        Atheriz.Core.Utils.PathGuards.GuardSavePath(savePath);
        Atheriz.Core.Utils.PathGuards.EnsureSaveDirectory(savePath);

        var pidPath = Path.Combine(savePath, "server.pid");

        int currentPid = Environment.ProcessId; // atheriz.py:507 pid = os.getpid()

        // Atomic create attempts — atheriz.py:517-554 (3 retries). The stale
        // check runs inside the loop so a fresh live claim that goes stale
        // mid-wait is re-read instead of refused.
        //
        // Shared stale-file verdict for the pre-create check and the
        // FileExists retry path below: live rival → refusal text; a fresh
        // live claim that went stale mid-flight → retry; otherwise proceed.
        // The port guard runs unconditionally — a corrupt/unparseable pid
        // file must not bypass it merely because TryReadPid returned null
        // (split-brain guard: never delete-and-retry into a bound port).
        // The delete steps stay at the call sites — they genuinely differ
        // (announced + terminal failure vs silent swallow + retry).
        (string? refusal, bool staleRetry) StaleVerdict()
        {
            int? oldPid = TryReadPid(pidPath);
            if (oldPid.HasValue && IsServerProcess(oldPid.Value))
            {
                if (FreshLiveClaimWentStale(pidPath, oldPid.Value)) return (null, true);
                return ($"{AlreadyRunningMessagePrefix} {oldPid.Value}", false);
            }
            if (IsPortListening(webserverPort))
                return ($"Port {webserverPort} still listening; refusing to overwrite PID file for an unverified process.", false);
            return (null, false);
        }
        for (int attempt = 0; attempt < 3; attempt++)
        {
            // First stale check — atheriz.py:486-496
            if (File.Exists(pidPath))
            {
                // Verdict shared with the FileExists retry path (live rival /
                // bound port refused inside StaleVerdict).
                var (refusal, staleRetry) = StaleVerdict();
                if (staleRetry) continue;
                if (refusal is not null) { reason = refusal; return false; }

                // Remove stale — atheriz.py:495. (No age/starting branch: the
                // live-PID check above already returned for a server process, so
                // any "just created" file reaching here is stale by definition.)
                try { File.Delete(pidPath); Console.WriteLine("Removing stale PID file."); }
                catch (Exception ex) { reason = $"Failed to remove stale PID file: {ex.Message}"; return false; }
            }

            try
            {
                // Mirrors open(pid_file, "x") + os.open O_EXCL 0o600
                using var fs = new FileStream(pidPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var pidBytes = System.Text.Encoding.UTF8.GetBytes(currentPid.ToString());
                fs.Write(pidBytes, 0, pidBytes.Length);
                // Durable before anyone reads it: a crash between write and
                // flush can leave an empty pid file that later parses as invalid.
                fs.Flush(true);
                // UnixFileMode 0o600 — atheriz.py relies on os.open 0o600; we set via FsUtil per AGENTS POSIX best-effort
                FsUtil.TryChmod0600(pidPath);
                // Dirsync the save dir so the server.pid directory entry itself
                // survives a crash (file fsync alone does not persist the entry).
                DirSync(Path.GetDirectoryName(pidPath));

                pidFile = new PidFile(pidPath, true, currentPid);
                return true;
            }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) == 80 || File.Exists(pidPath))
            {
                // FileExists — atheriz.py:520 except FileExistsError
                // Same verdict as the first stale check (live rival / bound
                // port refused inside StaleVerdict); only the delete-and-
                // retry tail below differs.
                var (refusal, staleRetry) = StaleVerdict();
                if (staleRetry) continue;
                if (refusal is not null) { reason = refusal; return false; }

                // No age gate: a live rival PID returned "already running"
                // above, so anything reaching here is stale by definition —
                // delete and retry.
                try { File.Delete(pidPath); } catch { }
                if (attempt == 2)
                {
                    reason = "Failed to acquire PID file after retries";
                    return false;
                }
                // retry — atheriz.py:530-554 second attempt
                continue;
            }
            catch (Exception ex)
            {
                reason = $"Failed to acquire PID file: {ex.Message}";
                return false;
            }
        }

        reason = "Failed to acquire PID file after retries";
        return false;
    }

    // Best-effort POSIX dirsync: fsync the directory fd so a newly created
    // pid-file entry is durable across a crash. No-op on non-POSIX
    // platforms; never throws.
    // Directory-sync P/Invokes live beside their only caller (DirSync):
    // kill stays in Cli.ProcessHelper beside RequestTerminate.
    private static class NativeMethods
    {
        [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi)]
        internal static extern int open(string pathname, int flags);
        [DllImport("libc", SetLastError = true)]
        internal static extern int fsync(int fd);
        [DllImport("libc", SetLastError = true)]
        internal static extern int close(int fd);
    }

    private static void DirSync(string? dir)
    {
        if (string.IsNullOrEmpty(dir)) return;
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        try
        {
            int fd = NativeMethods.open(dir, 0); // O_RDONLY
            if (fd < 0) return;
            try { NativeMethods.fsync(fd); }
            finally { NativeMethods.close(fd); }
        }
        catch { }
    }

    // Shared pid-file read for the CLI liveness probes (create/reset/
    // restart/stop + the game-folder guard): ReadAllText + Trim + TryParse in
    // one place. A TryParse failure is a dead claim (the old Parse sites
    // threw into a swallow-catch with the same outcome).
    internal static int? TryReadPid(string pidPath)
    {
        try
        {
            var text = File.ReadAllText(pidPath, System.Text.Encoding.UTF8).Trim();
            if (int.TryParse(text, out var pid)) return pid;
            return null;
        }
        catch { return null; }
    }

    // Live-claim probe: the file parses AND names a verified server process.
    // Stale/dead/unparseable/foreign pids report false (never throw).
    internal static bool IsLiveClaim(string pidPath, out int pid)
    {
        var read = TryReadPid(pidPath);
        if (read is int p && IsServerProcess(p)) { pid = p; return true; }
        pid = read ?? -1;
        return false;
    }

    /// <summary>
    /// Release the PID file — mirrors <c>atheriz/atheriz.py:679 pid_file.unlink()</c> on shutdown.
    /// Owner-verified: only deletes when the file still contains our own pid, so a stale
    /// <c>PidFile</c> can never delete a live successor's pid file on pid reuse.
    /// No finalizer: finalization during a long <c>RunAsync</c> could delete the live pid.
    /// </summary>
    public void Release()
    {
        if (_disposed) return;
        if (!_acquired) return;
        // Same exists-and-equals-owner delete check as ReleaseIfOwner below.
        ReleaseIfOwner(PidPath, _ownPid);
        _acquired = false;
    }

    /// <summary>
    /// Owner-verified delete for CLI paths that never acquired the file
    /// (e.g. <c>stop</c> acting on another process's pid file): deletes only
    /// when the file still contains <paramref name="expectedPid"/>, so a stale
    /// stop handle can never remove a live successor's pid file across the
    /// read/kill/delete window (pid reuse).
    /// </summary>
    public static bool ReleaseIfOwner(string pidPath, int expectedPid)
    {
        try
        {
            if (File.Exists(pidPath) && TryReadPid(pidPath) == expectedPid)
            {
                File.Delete(pidPath);
                return true;
            }
        }
        catch { }
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Release();
        GC.SuppressFinalize(this);
    }
}
