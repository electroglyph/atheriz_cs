using System.Runtime.InteropServices;

namespace Atheriz.Server.Cli;

// The daemon child detaches itself at startup (called from ServerHost when
// ATHERIZ_DAEMON=1). Unix: setsid() drops the controlling terminal so hangup
// can no longer reach the process; SIGHUP-ignore and stdio-to-/dev/null are
// belt and braces. Windows: FreeConsole() leaves the parent console.
// Best-effort and never fatal: every step is OS-guarded with a recorded
// run-attached fallback. All native declarations in production live here.
public static partial class DaemonDetach
{
    private static PosixSignalRegistration? _sighupKeepalive;

    internal static string DetachNote { get; private set; } = "not a daemon child";

    public static bool IsDaemonChild =>
        string.Equals(Environment.GetEnvironmentVariable("ATHERIZ_DAEMON"), "1", StringComparison.Ordinal);

    public static void TryDetachThisProcess()
    {
        if (!IsDaemonChild) return;
        try { Environment.SetEnvironmentVariable("ATHERIZ_DAEMON", null); } catch { }
        if (OperatingSystem.IsWindows())
        {
            try { DetachNote = FreeConsole() ? "detached from console (FreeConsole)" : "no console attached (FreeConsole false)"; }
            catch (Exception ex) { DetachNote = $"FreeConsole failed ({ex.GetType().Name}); continuing attached"; }
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            TrySetsid();
            IgnoreSighup();
            CloseInheritedPipesAndSockets();
            RedirectStdioToDevNull();
        }
        else
        {
            DetachNote = "unknown OS; continuing attached";
        }
        try { Console.SetOut(TextWriter.Null); } catch { }
        try { Console.SetError(TextWriter.Null); } catch { }
        try { Console.SetIn(TextReader.Null); } catch { }
    }

    private static void TrySetsid()
    {
        try
        {
            int rc;
            if (OperatingSystem.IsMacOS())
            {
                try { rc = SetSidSystem(); }
                catch { rc = SetSidLibc(); }
            }
            else
            {
                rc = SetSidLibc();
            }
            // setsid returns the new session id (== pid) on success, -1 on error.
            DetachNote = rc < 0 ? $"setsid failed; continuing attached" : $"detached with setsid (session {rc})";
        }
        catch (Exception ex) { DetachNote = $"setsid unavailable ({ex.GetType().Name}); continuing attached"; }
    }

    private static void IgnoreSighup()
    {
        try { _sighupKeepalive = PosixSignalRegistration.Create(PosixSignal.SIGHUP, static ctx => ctx.Cancel = true); }
        catch { }
    }

    private const int ReadWriteFlag = 2; // O_RDWR

    // A background child inherits every inheritable fd of its parent,
    // including duplicates of the redirected stdout/stderr pipes. .NET's
    // Process.WaitForExitAsync waits for stream EOF, so a daemon that keeps
    // those copies open hangs every programmatic launch (tests, harnesses)
    // until the caller's own timeout — even though the server is ready in
    // seconds. fds 0-2 are pointed at /dev/null below; here we close only
    // fds aliasing those same pipes/sockets (matched by link target, so
    // runtime-internal pipes like the signal-delivery pipe — closing which
    // silently re-armed SIGHUP death — and regular files stay open).
    // Linux-only: macOS keeps the old behavior (untestable on this host).
    private static void CloseInheritedPipesAndSockets()
    {
        if (!OperatingSystem.IsLinux()) return;
        HashSet<string> stdioTargets = new(StringComparer.Ordinal);
        foreach (int std in new[] {0, 1, 2})
        {
            string? stdTarget;
            try { stdTarget = new FileInfo($"/proc/self/fd/{std}").LinkTarget; } catch { continue; }
            if (stdTarget is not null
                && (stdTarget.StartsWith("pipe:", StringComparison.Ordinal) || stdTarget.StartsWith("socket:", StringComparison.Ordinal)))
                stdioTargets.Add(stdTarget);
        }
        if (stdioTargets.Count == 0) return;
        List<int> candidates = new();
        try
        {
            foreach (var entry in Directory.EnumerateFiles("/proc/self/fd"))
            {
                var name = Path.GetFileName(entry);
                if (int.TryParse(name, out int fd) && fd >= 3) candidates.Add(fd);
            }
        }
        catch { return; }
        foreach (int fd in candidates)
        {
            // Re-resolve immediately before closing: fd numbers can be
            // recycled between the listing and the close, and only
            // pipes/sockets may go.
            string? target;
            try { target = new FileInfo($"/proc/self/fd/{fd}").LinkTarget; } catch { continue; }
            if (target is null || !stdioTargets.Contains(target)) continue;
            try { CloseLibc(fd); } catch { }
        }
    }

    private static void RedirectStdioToDevNull()
    {
        try
        {
            bool mac = OperatingSystem.IsMacOS();
            int fd = mac ? OpenSystem("/dev/null", ReadWriteFlag) : OpenLibc("/dev/null", ReadWriteFlag);
            if (fd < 0) return;
            try
            {
                if (mac) { _ = Dup2System(fd, 0); _ = Dup2System(fd, 1); _ = Dup2System(fd, 2); }
                else { _ = Dup2Libc(fd, 0); _ = Dup2Libc(fd, 1); _ = Dup2Libc(fd, 2); }
            }
            finally
            {
                if (fd > 2)
                {
                    try { if (mac) _ = CloseSystem(fd); else _ = CloseLibc(fd); } catch { }
                }
            }
        }
        catch { }
    }

    [LibraryImport("libc", EntryPoint = "setsid", SetLastError = true)]
    private static partial int SetSidLibc();

    [LibraryImport("__Internal", EntryPoint = "setsid", SetLastError = true)]
    private static partial int SetSidSystem();

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true)]
    private static partial int OpenLibc([MarshalAs(UnmanagedType.LPUTF8Str)] string pathname, int flags);

    [LibraryImport("__Internal", EntryPoint = "open", SetLastError = true)]
    private static partial int OpenSystem([MarshalAs(UnmanagedType.LPUTF8Str)] string pathname, int flags);

    [LibraryImport("libc", EntryPoint = "dup2", SetLastError = true)]
    private static partial int Dup2Libc(int oldfd, int newfd);

    [LibraryImport("__Internal", EntryPoint = "dup2", SetLastError = true)]
    private static partial int Dup2System(int oldfd, int newfd);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int CloseLibc(int fd);

    [LibraryImport("__Internal", EntryPoint = "close", SetLastError = true)]
    private static partial int CloseSystem(int fd);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeConsole();
}
