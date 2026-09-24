namespace Atheriz.Server.Cli;

public static class ProcessHelper
{
    // Signal first, escalate only when the process survives.
    // Process.Kill() sends SIGTERM on Unix (graceful first step) and
    // terminates on Windows.
    public static void RequestTerminate(Process proc)
    {
        try { if (!proc.HasExited) proc.Kill(entireProcessTree: false); } catch { }
    }

    // Quiet terminate escalation: SIGTERM, bounded wait, SIGKILL on survival,
    // bounded wait. No progress output; callers print their own lines.
    public static async Task TerminateAsync(Process proc, TimeSpan? grace = null)
    {
        grace ??= TimeSpan.FromSeconds(3);
        RequestTerminate(proc);
        if (!await WaitForExitAsync(proc, grace.Value).ConfigureAwait(false))
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: false); } catch { }
            await WaitForExitAsync(proc, grace.Value).ConfigureAwait(false);
        }
    }

    internal static async Task<bool> WaitForExitAsync(Process proc, TimeSpan timeout)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            try { await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        catch { }
        try { return proc.HasExited; } catch { return true; }
    }

    public static async Task<bool> WaitForPidExitAsync(int pid, TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(5);
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            bool gone;
            try { using var p = Process.GetProcessById(pid); gone = p.HasExited; }
            catch (ArgumentException) { return true; }
            catch { return false; }
            if (gone) return true;
            await Task.Delay(100).ConfigureAwait(false);
        }
        try { using var q = Process.GetProcessById(pid); return q.HasExited; } catch (ArgumentException) { return true; } catch { return false; }
    }
}
