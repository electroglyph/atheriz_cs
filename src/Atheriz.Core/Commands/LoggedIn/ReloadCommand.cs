// Port of atheriz/commands/loggedin/reload.py:43

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class ReloadCommand : Command
{
    public override string Key => "reload";
    public override string Desc => "Reload game logic and modules.";
    public override bool UseParser => false;
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsSuperUser(caller);
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        Atheriz.Core.Objects.GameObject? channel;
        try
        {
            // the channel fetch itself can throw; keep it inside
            // try so a broken channel service still reaches the reload.
            channel = GlobalServices.GetServerChannel();
            try { channel?.Msg("Server is reloading..."); } catch (Exception) { }
            try { Atheriz.Core.ServerEvents.AtServerReload(); } catch (Exception) { }
            try { AtherizLogger.LogInformation($"Reload triggered by {go.Name} ({go.Id})"); } catch (Exception) { }
        }
        catch (Exception ex) { try { go.Msg($"Reload failed: {ex.Message}"); } catch (Exception) { } return; }
        // never block the command thread on the async reload work
        // (Python reload.py:43 runs it inline, stalling dispatch). The
        // continuation reports the first cause instead of swallowing it.
        var capturedGo = go;
        var capturedChannel = channel;
        try
        {
            // Port of reloader.reload_game_logic() — use PluginReloader async API or ServerLifecycle.
            // Arguments evaluate here (not inside the async method) so a
            // throwing ticker fetch lands in this catch, as before.
            _ = FinishReloadAsync(
                Atheriz.Core.Plugins.PluginReloader.ReloadGameLogicAsync(GlobalServices.GetAsyncTicker(), AtherizSettings.Global),
                capturedGo, capturedChannel);
        }
        catch (Exception ex) { try { go.Msg($"Reload failed: {ex.Message}"); } catch (Exception) { } }
    }

    // Async form of the old ContinueWith chain: canceled reports "canceled",
    // a fault falls back to ServerLifecycle.DoReload (completed/failed), and
    // success reports the reloader's own result. Same outcome strings.
    private static async Task FinishReloadAsync(Task<string> reloadTask, Atheriz.Core.Objects.GameObject go, Atheriz.Core.Objects.GameObject? channel)
    {
        ArgumentNullException.ThrowIfNull(reloadTask);
        string result;
        try
        {
            result = await reloadTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { result = "Reload canceled."; }
        catch (Exception)
        {
            // fallback to ServerLifecycle.DoReload which handles ticker/map save
            try { Atheriz.Core.Globals.StartStop.DoReload(AtherizSettings.Global); result = "Reload completed."; }
            catch (Exception ex2) { result = $"Reload failed: {ex2.Message}"; }
        }
        if (channel is not null)
        {
            try { channel.Msg(result); } catch (Exception) { }
            try { go.Msg(result); } catch (Exception) { }
        }
        else
        {
            try { go.Msg(result); } catch (Exception) { }
        }
    }
}