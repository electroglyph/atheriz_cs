using Atheriz.Core.Network;
using Atheriz.Core.Persistence;
using Atheriz.Core.Plugins;
using Atheriz.Server.Cli;
using Atheriz.Server.Infrastructure;
using Microsoft.Extensions.Hosting;

namespace Atheriz.Server.Hosting;

// Server side of an in-game restart: rebuild, checkpoint, hand over to a
// freshly spawned process, then stop. Every failure aborts loudly with the
// old server still running — the handover never starts until the build and
// the save both succeed, and the stop never runs until the replacement
// reports it is waiting.
public static class RestartCoordinator
{
    public sealed record RestartOutcome(bool Ok, string Message);

    // Single-flight gate: two overlapping restarts would double-spawn while
    // reporting one handover.
    private static int _running;

    /// <summary>
    /// Plain-text restart notice every session sees (telnet has no wire
    /// commands, so this is its only warning). The webclient also gets
    /// the <c>restarting</c> wire frame, which is what it waits on.
    /// </summary>
    public const string RestartNotice = "Server is restarting! It will be back up shortly.";

    /// <summary>
    /// Warns every connected session that the restart handover is committed
    /// and this process is about to stop: a <c>restarting</c> wire frame (the
    /// webclient holds its reconnects on it instead of burning its retry
    /// budget) plus the plain-text notice above. Best-effort and never
    /// fatal — the stop below runs even if the announce throws.
    /// </summary>
    public static void AnnounceRestart()
    {
        try
        {
            var manager = ConnectionManager.GlobalInstance;
            if (manager is null) return;
            foreach (var conn in manager.GetAllConnections())
            {
                try { conn.SendCommand("restarting"); }
                catch { }
            }
            try { manager.Broadcast(RestartNotice); }
            catch { }
        }
        catch { }
    }

    public static Task<RestartOutcome> RestartAsync(
        AtherizSettings settings,
        IHostApplicationLifetime lifetime,
        CancellationToken cancellationToken = default)
        => RestartAsync(settings, lifetime, buildAll: null, checkpoint: null, spawn: null, cancellationToken);

    public static async Task<RestartOutcome> RestartAsync(
        AtherizSettings settings,
        IHostApplicationLifetime lifetime,
        Func<AtherizSettings, CancellationToken, Task<BuildCoordinator.BuildAllResult>>? buildAll,
        Func<AtherizSettings, List<CheckpointWriter.SectionFailure>>? checkpoint,
        Func<ProcessStartInfo, Process?>? spawn,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(lifetime);
        if (Interlocked.Exchange(ref _running, 1) == 1)
            return new RestartOutcome(false, "Restart already in progress.");
        try
        {
            // Rebuild first, while the old server still runs: a failed build
            // leaves it untouched instead of stranding the game with nothing
            // to boot.
            var build = await (buildAll ?? ((s, ct) => BuildCoordinator.EnsureAllBuiltAsync(s, cancellationToken: ct)))(
                settings, cancellationToken).ConfigureAwait(false);
            if (!build.Ok) return new RestartOutcome(false, $"{build.Summary} Server keeps running.");
            List<CheckpointWriter.SectionFailure> failures;
            try
            {
                failures = (checkpoint ?? (s => CheckpointWriter.WriteCheckpoint(
                    s, "save", forceNodes: true, includeTime: s.TimeSystemEnabled)))(settings);
            }
            catch (Exception ex)
            {
                return new RestartOutcome(false, $"Restart aborted: checkpoint failed: {ex.Message} Server keeps running.");
            }
            if (failures.Count != 0)
                return new RestartOutcome(false, $"Restart aborted with {failures.Count} save error(s). Server keeps running.");
            var gameFolder = Directory.GetCurrentDirectory();
            if (!DaemonSpawner.TryResolveChildExe(out var fileName, out var dllArg, out var exeError))
                return new RestartOutcome(false, $"Restart aborted: {exeError} Server keeps running.");
            // A stale marker from a crashed restart must not satisfy the wait
            // below: the child writes its own marker when it starts waiting.
            var marker = TakeoverHandler.HandoverMarkerPath(settings);
            try
            {
                var markerDir = Path.GetDirectoryName(marker);
                if (markerDir is not null) Directory.CreateDirectory(markerDir);
                File.Delete(marker);
            }
            catch { }
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = gameFolder,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (dllArg is not null) psi.ArgumentList.Add(dllArg);
            psi.ArgumentList.Add("takeover");
            psi.ArgumentList.Add("--pid");
            psi.ArgumentList.Add(Environment.ProcessId.ToString());
            psi.ArgumentList.Add("--port");
            psi.ArgumentList.Add(settings.WebserverPort.ToString());
            if (!string.IsNullOrEmpty(settings.WebserverInterface))
            {
                psi.ArgumentList.Add("--host");
                psi.ArgumentList.Add(settings.WebserverInterface);
            }
            if (settings.TelnetEnabled)
            {
                psi.ArgumentList.Add("--telnet-port");
                psi.ArgumentList.Add(settings.TelnetPort.ToString());
            }
            psi.Environment[DaemonSpawner.DaemonEnvKey] = "1";
            Process? child;
            try { child = (spawn ?? (info => Process.Start(info)))(psi); }
            catch (Exception ex)
            {
                return new RestartOutcome(false, $"Restart aborted: could not spawn replacement: {ex.Message} Server keeps running.");
            }
            if (child is null)
                return new RestartOutcome(false, "Restart aborted: could not spawn replacement. Server keeps running.");
            using (child)
            {
                // The child only boots after this process exits, so it cannot
                // race the old listener; stopping before it waits would leave
                // no server at all if the child then fails.
                if (!await WaitForHandoverAsync(marker, child, TimeSpan.FromSeconds(10)).ConfigureAwait(false))
                    return new RestartOutcome(false, "Restart aborted: replacement did not start waiting. Server keeps running.");
            }
            // Committed: the replacement is waiting, so this process will
            // stop. Warn connected clients first — the webclient holds its
            // reconnects on the `restarting` wire frame instead of treating
            // the drop as a failure. Best-effort: the stop below runs even
            // if the announce throws.
            try { AnnounceRestart(); }
            catch { }
            // Same shape as the shutdown route: shutdown tasks now, process
            // exit after.
            _ = Task.Run(() =>
            {
                try { ServerLifecycle.DoShutdown(settings); }
                finally { try { lifetime.StopApplication(); } catch { } }
            });
            return new RestartOutcome(true, "Server is restarting...");
        }
        finally { Interlocked.Exchange(ref _running, 0); }
    }

    // True once the replacement reports it is waiting for the handover. A
    // dead child never satisfies the wait — it must not stop the server it
    // was meant to replace.
    private static async Task<bool> WaitForHandoverAsync(string marker, Process child, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            try { if (child.HasExited) return false; }
            catch { return false; }
            try { if (File.Exists(marker)) return true; }
            catch { }
            await Task.Delay(100).ConfigureAwait(false);
        }
        try { if (child.HasExited) return false; }
        catch { return false; }
        try { return File.Exists(marker); }
        catch { return false; }
    }
}
