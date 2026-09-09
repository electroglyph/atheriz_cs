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
            // Port of reloader.reload_game_logic() — use PluginReloader async API or ServerLifecycle
            Atheriz.Core.Plugins.PluginReloader.ReloadGameLogicAsync(GlobalServices.GetAsyncTicker(), AtherizSettings.Global)
                .ContinueWith(t =>
                {
                    string result;
                    try
                    {
                        if (t.IsCanceled) result = "Reload canceled.";
                        else if (t.IsFaulted)
                        {
                            // fallback to ServerLifecycle.DoReload which handles ticker/map save
                            try { Atheriz.Core.Globals.StartStop.DoReload(AtherizSettings.Global); result = "Reload completed."; }
                            catch (Exception ex2) { result = $"Reload failed: {ex2.Message}"; }
                        }
                        else result = t.Result;
                    }
                    catch (Exception ex) { result = $"Reload failed: {ex.Message}"; }
                    if (capturedChannel is not null)
                    {
                        try { capturedChannel.Msg(result); } catch (Exception) { }
                        try { capturedGo.Msg(result); } catch (Exception) { }
                    }
                    else
                    {
                        try { capturedGo.Msg(result); } catch (Exception) { }
                    }
                }, TaskScheduler.Default);
        }
        catch (Exception ex) { try { go.Msg($"Reload failed: {ex.Message}"); } catch (Exception) { } }
    }
}