using System.Diagnostics;

namespace Atheriz.Server.Cli;

public static class ProcessHelper
{
    public static async Task KillProcessWithDots(Process proc)
    {
        // Actually terminate first (the old code only waited); then wait for
        // exit with progress dots (audit B33).
        try { if (!proc.HasExited) proc.Kill(); } catch { }
        for (int i = 0; i < 50; i++)
        {
            bool done = false;
            try { if (proc.HasExited) done = true; } catch { done = true; }
            if (done) break;
            await Task.Delay(100);
            Console.Write(".");
        }
    }

    public static async Task WaitForPidExitAsync(int pid)
    {
        for (int i = 0; i < 50; i++)
        {
            bool exists = true;
            try { using var p = Process.GetProcessById(pid); exists = !p.HasExited; } catch (ArgumentException) { exists = false; } catch { }
            if (!exists) break;
            await Task.Delay(100);
            Console.Write(".");
        }
    }
}
