using System.Diagnostics;
using System.Net.NetworkInformation;
using Atheriz.Core.Utils;

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
    /// Mirrors <c>atheriz/atheriz.py:458-472 _pid_is_server_process</c>.
    /// Python uses <c>psutil.pid_exists + proc.name().lower().startswith(("python","atheriz"))</c>.
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
            // atheriz.py:470 — startswith python/atheriz
            if (lower.StartsWith("python") || lower.StartsWith("atheriz") || lower.Contains("atheriz"))
                return true;
            // Fallback: check main module filename if available (helps when process name truncated)
            try
            {
                var mod = proc.MainModule?.FileName ?? "";
                var ml = mod.ToLowerInvariant();
                if (ml.Contains("atheriz"))
                    return true;
                // dotnet host: only trust when the command line names the server
                // assembly itself. A bare "Atheriz" substring is NOT enough: the
                // test host loads Atheriz.Server.dll but its command line names
                // the test assembly, so `stop` can never terminate a test run
                // on pid reuse.
                if (ml.Contains("dotnet") || lower.Contains("dotnet"))
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
            if (p != null)
            {
                string outp = p.StandardOutput.ReadToEnd();
                p.WaitForExit(2000);
                foreach (var line in outp.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    if (int.TryParse(line.Trim(), out var cand) && IsServerProcess(cand) && IsProcessListeningOnPort(cand, port)) { pid = cand; return true; }
                foreach (var line in outp.Split('\n', StringSplitOptions.RemoveEmptyEntries))
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
            if (p2 != null)
            {
                string outp = p2.StandardOutput.ReadToEnd();
                p2.WaitForExit(2000);
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
                var targetInodes = new HashSet<string>();
                foreach (var netFile in new[] { "/proc/net/tcp", "/proc/net/tcp6" })
                {
                    if (!File.Exists(netFile)) continue;
                    var lines = File.ReadAllLines(netFile);
                    foreach (var line in lines.Skip(1))
                    {
                        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 10) continue;
                        var local = parts[1]; // 0100007F:0035
                        var st = parts[3];
                        if (st != "0A") continue; // LISTEN
                        var portHex = local.Split(':').LastOrDefault();
                        if (portHex == null) continue;
                        if (int.TryParse(portHex, System.Globalization.NumberStyles.HexNumber, null, out var pnum) && pnum == port)
                        {
                            var inode = parts[9];
                            targetInodes.Add(inode);
                        }
                    }
                }
                if (targetInodes.Count > 0)
                {
                    foreach (var dir in Directory.GetDirectories("/proc"))
                    {
                        var name = Path.GetFileName(dir);
                        if (!int.TryParse(name, out var candPid)) continue;
                        if (!IsServerProcess(candPid)) continue;
                        try
                        {
                            var fdDir = Path.Combine(dir, "fd");
                            if (!Directory.Exists(fdDir)) continue;
                            foreach (var fd in Directory.GetFiles(fdDir))
                            {
                                try
                                {
                                    var link = File.ResolveLinkTarget(fd, true)?.ToString() ?? new FileInfo(fd).LinkTarget ?? "";
                                    // fallback via readlink
                                    if (string.IsNullOrEmpty(link))
                                    {
                                        try { link = File.ReadAllText($"/proc/{candPid}/fdinfo/{Path.GetFileName(fd)}"); } catch { }
                                    }
                                    foreach (var ino in targetInodes)
                                        if (link.Contains($"socket:[{ino}]")) { pid = candPid; return true; }
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }
                    // second pass without IsServerProcess filter
                    foreach (var dir in Directory.GetDirectories("/proc"))
                    {
                        var name = Path.GetFileName(dir);
                        if (!int.TryParse(name, out var candPid)) continue;
                        try
                        {
                            var fdDir = Path.Combine(dir, "fd");
                            if (!Directory.Exists(fdDir)) continue;
                            foreach (var fd in Directory.GetFiles(fdDir))
                            {
                                try
                                {
                                    var link = File.ResolveLinkTarget(fd, true)?.ToString() ?? "";
                                    foreach (var ino in targetInodes)
                                        if (link.Contains($"socket:[{ino}]")) { pid = candPid; return true; }
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Verify pid actually holds LISTEN on port (mirrors _process_listening_by_port).
    /// </summary>
    public static bool IsProcessListeningOnPort(int pid, int port)
    {
        // Quick check via /proc/net/tcp + fd as above for single pid
        try
        {
            var targetInodes = new HashSet<string>();
            foreach (var netFile in new[] { "/proc/net/tcp", "/proc/net/tcp6" })
            {
                if (!File.Exists(netFile)) continue;
                var lines = File.ReadAllLines(netFile);
                foreach (var line in lines.Skip(1))
                {
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 10) continue;
                    var local = parts[1];
                    var st = parts[3];
                    if (st != "0A") continue;
                    var portHex = local.Split(':').LastOrDefault();
                    if (portHex == null) continue;
                    if (int.TryParse(portHex, System.Globalization.NumberStyles.HexNumber, null, out var pnum) && pnum == port)
                        targetInodes.Add(parts[9]);
                }
            }
            if (targetInodes.Count == 0) return IsPortListening(port);
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
        catch { return IsPortListening(port); }
    }

    /// <summary>
    /// Locate the server.pid governing <paramref name="port"/>: the save-dir
    /// file first, then the cwd of the listener process, then a walk up the
    /// current directory tree. Returns a path that may not exist (caller decides).
    /// </summary>
    public static string LocateServerPidFile(int port, string savePath)
    {
        var pidFilePath = Path.Combine(savePath, "server.pid");
        if (!File.Exists(pidFilePath))
        {
            try
            {
                if (TryFindPidListeningOnPort(port, out var lpid2))
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
        return pidFilePath;
    }

    /// <summary>
    /// Attempts to atomically acquire the PID file at <c>{savePath}/server.pid</c>.
    /// Mirrors <c>atheriz/atheriz.py:486-555 start_server</c> PID race:
    ///   - if file exists: read old_pid, if IsServerProcess → fail ("already running")
    ///   - else remove stale and try CreateNew
    ///   - on FileExistsError re-read and verify again (up to 3 attempts)
    /// Also matches <c>spawn_daemon:1165-1235</c> concurrent spawn handling with age check.
    /// Returns true if acquired; caller must Dispose/Release.
    /// </summary>
    public static bool TryAcquire(string savePath, out PidFile? pidFile, out string? reason, int webserverPort = 9999)
    {
        pidFile = null;
        reason = null;

        // Guard — atheriz/atheriz.py:508-512 + PathGuards (Core; Server wrapper deleted P1-16)
        Atheriz.Core.Utils.PathGuards.GuardSavePath(savePath);
        Atheriz.Core.Utils.PathGuards.EnsureSaveDirectory(savePath);

        var pidPath = Path.Combine(savePath, "server.pid");

        // First stale check — atheriz.py:486-496
        if (File.Exists(pidPath))
        {
            int? oldPid = TryReadPid(pidPath);
            if (oldPid.HasValue && IsServerProcess(oldPid.Value))
            {
                reason = $"Server is already running with PID: {oldPid.Value}";
                return false;
            }

            // Stale: verify the port is actually free before deleting — deleting a stale
            // file while something still LISTENs risks a split-brain second server.
            if (oldPid.HasValue)
            {
                if (IsPortListening(webserverPort))
                {
                    reason = $"Port {webserverPort} still listening; refusing to overwrite PID file for an unverified process.";
                    return false;
                }
            }

            // Also handle age check for concurrent spawn — atheriz.py:1188-1194 if age < 1.0 → already starting
            try
            {
                var age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - new DateTimeOffset(File.GetLastWriteTimeUtc(pidPath)).ToUnixTimeSeconds();
                // Use File.GetLastWriteTimeUtc for cross-platform
                var mtime = File.GetLastWriteTimeUtc(pidPath);
                var ageSec = (DateTime.UtcNow - mtime).TotalSeconds;
                if (ageSec < 1.0 && oldPid.HasValue && IsServerProcess(oldPid.Value))
                {
                    reason = "Server is already starting (PID file just created)";
                    return false;
                }
            }
            catch { }

            // Remove stale — atheriz.py:495
            try { File.Delete(pidPath); Console.WriteLine("Removing stale PID file."); }
            catch (Exception ex) { reason = $"Failed to remove stale PID file: {ex.Message}"; return false; }
        }

        int currentPid = Environment.ProcessId; // atheriz.py:507 pid = os.getpid()

        // Atomic create attempts — atheriz.py:517-554 (3 retries)
        for (int attempt = 0; attempt < 3; attempt++)
        {
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
                int? oldPid = TryReadPid(pidPath);
                if (oldPid.HasValue && IsServerProcess(oldPid.Value))
                {
                    reason = $"Server is already running with PID: {oldPid.Value}";
                    return false;
                }

                // Check age for concurrent winner — atheriz.py:1188-1225
                try
                {
                    var mtime = File.GetLastWriteTimeUtc(pidPath);
                    var ageSec = (DateTime.UtcNow - mtime).TotalSeconds;
                    if (ageSec < 2.0)
                    {
                        // Might be concurrent spawn winner; but if pid is dead we still clean?
                        // If pid dead and age <2s, treat as stale but wait? Python treats as already starting
                        // We check if oldPid not server process → still delete
                        if (!oldPid.HasValue || !IsServerProcess(oldPid.Value))
                        {
                            // stale concurrent file with dead pid → delete and retry
                            try { File.Delete(pidPath); } catch { }
                            continue;
                        }
                        reason = "Server is already starting (concurrent spawn)";
                        return false;
                    }
                }
                catch { }

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
    private static void DirSync(string? dir)
    {
        if (string.IsNullOrEmpty(dir)) return;
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        try
        {
            int fd = Cli.ProcessHelper.NativeMethods.open(dir, 0); // O_RDONLY
            if (fd < 0) return;
            try { Cli.ProcessHelper.NativeMethods.fsync(fd); }
            finally { Cli.ProcessHelper.NativeMethods.close(fd); }
        }
        catch { }
    }

    private static int? TryReadPid(string pidPath)
    {
        try
        {
            var text = File.ReadAllText(pidPath, System.Text.Encoding.UTF8).Trim();
            if (int.TryParse(text, out var pid)) return pid;
            return null;
        }
        catch { return null; }
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
        try
        {
            if (File.Exists(PidPath) && TryReadPid(PidPath) == _ownPid)
                File.Delete(PidPath);
        }
        catch { }
        _acquired = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Release();
        GC.SuppressFinalize(this);
    }
}
