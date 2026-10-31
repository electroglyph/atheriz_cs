using Atheriz.Server.Hosting;

namespace Atheriz.Server.Cli;

// Child side of an in-game restart: the running server spawns
// `takeover --pid <parent>` and stops itself only after this process reports
// it is waiting. The child waits for the parent to exit and the ports to
// free, then runs the replacement in this process — so the bind never races
// the old listener, and a stuck parent aborts the handover without binding.
public static class TakeoverHandler
{
    internal const string HandoverMarkerFileName = "restart.waiting";

    /// <summary>
    /// Handover marker the child writes when it starts waiting and deletes
    /// before booting. The parent stops only after seeing it; a stale marker
    /// from a crashed restart is deleted by the parent before spawning.
    /// </summary>
    public static string HandoverMarkerPath(AtherizSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var save = settings.SavePath;
        string abs;
        try { abs = Path.IsPathRooted(save) ? Path.GetFullPath(save) : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), save)); }
        catch { abs = save; }
        return Path.Combine(abs, HandoverMarkerFileName);
    }

    /// <summary>
    /// Waits for the parent server to exit and its ports to free, then runs
    /// the replacement server in this process. Returns the process exit code.
    /// </summary>
    public static async Task<int> TakeoverAsync(
        AtherizSettings settings,
        int parentPid,
        int? port = null,
        string? host = null,
        int? telnetPort = null,
        Func<int, TimeSpan, Task<bool>>? waitParent = null,
        Func<int, TimeSpan, Task<bool>>? waitPortFree = null,
        Func<int?, string?, int?, Task<int>>? start = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        // Never plant a relative save folder outside a game folder: the
        // handover marker write below creates its parent directory.
        try { Atheriz.Core.Utils.PathGuards.GuardSavePath(settings.SavePath); }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        var marker = HandoverMarkerPath(settings);
        try
        {
            var dir = Path.GetDirectoryName(marker);
            if (dir is not null) Directory.CreateDirectory(dir);
            File.WriteAllText(marker, Environment.ProcessId.ToString());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"takeover: cannot write handover marker: {ex.Message}");
            return 1;
        }
        var waitForParent = waitParent ?? ((pid, timeout) => ProcessHelper.WaitForPidExitAsync(pid, timeout));
        if (!await waitForParent(parentPid, TimeSpan.FromSeconds(30)).ConfigureAwait(false))
        {
            Console.Error.WriteLine($"takeover: parent process {parentPid} still running; aborting without binding.");
            try { File.Delete(marker); } catch { }
            return 1;
        }
        var portVal = port ?? settings.WebserverPort;
        var waitFree = waitPortFree ?? RestartHandler.WaitForPortFreeAsync;
        if (!await waitFree(portVal, TimeSpan.FromSeconds(30)).ConfigureAwait(false))
        {
            Console.Error.WriteLine($"takeover: port {portVal} still listening after parent exit; aborting without binding.");
            try { File.Delete(marker); } catch { }
            return 1;
        }
        if (settings.TelnetEnabled)
        {
            var telnetVal = telnetPort ?? settings.TelnetPort;
            if (!await waitFree(telnetVal, TimeSpan.FromSeconds(30)).ConfigureAwait(false))
            {
                Console.Error.WriteLine($"takeover: telnet port {telnetVal} still listening after parent exit; aborting without binding.");
                try { File.Delete(marker); } catch { }
                return 1;
            }
        }
        try { File.Delete(marker); } catch { }
        var run = start ?? ServerHost.RunForegroundAsync;
        return await run(port, host, telnetPort).ConfigureAwait(false);
    }
}
