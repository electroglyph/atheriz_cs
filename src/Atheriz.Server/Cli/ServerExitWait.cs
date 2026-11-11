using Atheriz.Server.Infrastructure;

namespace Atheriz.Server.Cli;

// Post-graceful-shutdown settle wait (split out of StopHandler to keep that
// file under its size cap). Graceful shutdown is asynchronous: the server
// drains pools (up to 10s), saves, and checkpoints before exiting. Waiting
// here keeps a following start from booting against a dying server (shard
// WAL WouldBlock storms and GPU double-sessions). Bounded at 30s; a hung
// server warns and the CLI returns instead of hanging. No-op when no pid
// claims the server (tests and already-stopped servers return immediately).
internal static class ServerExitWait
{
    internal static async Task WaitForServerExitAsync(AtherizSettings effective)
    {
        int port = effective.WebserverPort;
        string pidFilePath;
        try { pidFilePath = PidFile.LocateServerPidFile(effective.SavePath); }
        catch { return; }
        int? pid = null;
        try { pid = PidFile.TryReadPid(pidFilePath); } catch { }
        if (pid is null) return;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            bool alive;
            try { using var proc = Process.GetProcessById(pid.Value); alive = !proc.HasExited; }
            catch (ArgumentException) { alive = false; }
            catch { alive = true; }
            bool portOpen;
            try { portOpen = PidFile.IsPortListening(port); } catch { portOpen = true; }
            if (!alive && !portOpen) return;
            try { await Task.Delay(200).ConfigureAwait(false); } catch { return; }
        }
        try { Console.WriteLine($"Warning: server process {pid} has not exited 30s after graceful shutdown; a following start may contend on its shards."); } catch { }
    }
}
