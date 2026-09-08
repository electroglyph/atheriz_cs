using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Atheriz.Server.Cli;

public static class ProcessHelper
{
    // Port of atheriz.py stop_server terminate() (SIGTERM) before kill() (SIGKILL):
    // signal first, escalate only when the process survives.
    public static void RequestTerminate(Process proc)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                if (NativeMethods.kill(proc.Id, 15) == 0) return;
            }
        }
        catch { }
        try { if (!proc.HasExited) proc.Kill(entireProcessTree: false); } catch { }
    }

    public static async Task<bool> WaitForExitDotsAsync(Process proc, int tenths)
    {
        for (int i = 0; i < tenths; i++)
        {
            try { if (proc.HasExited) return true; } catch { return true; }
            await Task.Delay(100);
            Console.Write(".");
        }
        try { return proc.HasExited; } catch { return true; }
    }

    public static async Task KillProcessWithDots(Process proc)
    {
        // Terminate first (the old code only waited, then SIGKILLed); escalate
        // to SIGKILL only when the process survives SIGTERM.
        RequestTerminate(proc);
        if (!await WaitForExitDotsAsync(proc, 30))
        {
            Console.Write(" Timeout! Force killing...");
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: false); } catch { }
            await WaitForExitDotsAsync(proc, 30);
        }
    }

    public static async Task<bool> WaitForPidExitAsync(int pid)
    {
        for (int i = 0; i < 50; i++)
        {
            bool exists = true;
            try { using var p = Process.GetProcessById(pid); exists = !p.HasExited; } catch (ArgumentException) { exists = false; } catch { }
            if (!exists) return true;
            await Task.Delay(100);
            Console.Write(".");
        }
        try { using var q = Process.GetProcessById(pid); return q.HasExited; } catch (ArgumentException) { return true; } catch { return false; }
    }

    internal static class NativeMethods
    {
        [DllImport("libc", SetLastError = true)]
        internal static extern int kill(int pid, int sig);
        [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi)]
        internal static extern int open(string pathname, int flags);
        [DllImport("libc", SetLastError = true)]
        internal static extern int fsync(int fd);
        [DllImport("libc", SetLastError = true)]
        internal static extern int close(int fd);
    }
}
