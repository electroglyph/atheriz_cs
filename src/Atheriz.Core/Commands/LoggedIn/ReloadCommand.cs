// Port of atheriz/commands/loggedin/reload.py:43
using Atheriz.Core.Objects;
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class ReloadCommand : Command
{
    public override string Key => "reload";
    public override string Desc => "Reload game logic and modules.";
    public override bool UseParser => false;
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsSuperUser(caller);
    public override void Run(IMessageTarget caller, object? args)
    {
        if (caller is not GameObject go) { caller.Msg("You can't do that."); return; }
        var channel = GlobalServices.GetServerChannel();
        if (channel != null)
        {
            try { channel.Msg("Server is reloading..."); } catch { }
        }
        try { Atheriz.Core.ServerEvents.AtServerReload(); } catch { }
        try { AtherizLogger.LogInformation($"Reload triggered by {go.Name} ({go.Id})"); } catch { }
        string result;
        try
        {
            // Port of reloader.reload_game_logic() — use PluginReloader sync wrapper or ServerLifecycle
            try
            {
                var task = Atheriz.Core.Plugins.PluginReloader.ReloadGameLogicAsync(GlobalServices.GetAsyncTicker(), AtherizSettings.Global);
                result = task.GetAwaiter().GetResult();
            }
            catch
            {
                // fallback to ServerLifecycle.DoReload which handles ticker/map save
                try { Atheriz.Core.Globals.StartStop.DoReload(AtherizSettings.Global); result = "Reload completed."; }
                catch (Exception ex2) { result = $"Reload failed: {ex2.Message}"; }
            }
        }
        catch (Exception ex) { result = $"Reload failed: {ex.Message}"; }
        if (channel != null)
        {
            try { channel.Msg(result); } catch {}
            try { go.Msg(result); } catch {}
        }
        else
        {
            go.Msg(result);
        }
    }
}