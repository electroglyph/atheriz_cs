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
        // Shared dot-wait cadence below; the exit probe keeps its own
        // catch semantics (probe failure = exited) and tail.
        await WaitUntilAsync(() => { try { return proc.HasExited; } catch { return true; } }, tenths).ConfigureAwait(false);
        try { return proc.HasExited; } catch { return true; }
    }

    public static async Task KillProcessWithDots(Process proc)
    {
        // Terminate first (the old code only waited, then SIGKILLed); escalate
        // to SIGKILL only when the process survives SIGTERM.
        RequestTerminate(proc);
        if (!await WaitForExitDotsAsync(proc, 30).ConfigureAwait(false))
        {
            Console.Write(" Timeout! Force killing...");
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: false); } catch { }
            await WaitForExitDotsAsync(proc, 30).ConfigureAwait(false);
        }
    }

    // Shared dot-wait core: bounded Delay(100) + Write(".") cadence only.
    // Per-caller exit predicates (with their own catch semantics) and tails
    // stay at the call sites — ExitDots treats probe failure as exited while
    // PidExit treats it as alive (except ArgumentException), and the bounds
    // differ (tenths param vs hardcoded 50).
    internal static async Task WaitUntilAsync(Func<bool> isDone, int tenths)
    {
        for (int i = 0; i < tenths && !isDone(); i++)
        {
            await Task.Delay(100).ConfigureAwait(false);
            Console.Write(".");
        }
    }

    public static async Task<bool> WaitForPidExitAsync(int pid)
    {
        await WaitUntilAsync(() =>
        {
            bool exists = true;
            try { using var p = Process.GetProcessById(pid); exists = !p.HasExited; } catch (ArgumentException) { exists = false; } catch { }
            return !exists;
        }, 50).ConfigureAwait(false);
        try { using var q = Process.GetProcessById(pid); return q.HasExited; } catch (ArgumentException) { return true; } catch { return false; }
    }

    internal static class NativeMethods
    {
        [DllImport("libc", SetLastError = true)]
        internal static extern int kill(int pid, int sig);
    }
}
