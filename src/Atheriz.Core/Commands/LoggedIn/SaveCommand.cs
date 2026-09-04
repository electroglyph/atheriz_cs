// Port of atheriz/commands/loggedin/save.py:32
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Commands;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class SaveCommand : Command
{
    public override string Key => "save";
    public override string Desc => "Save all the things.";
    public override string Category => "Admin";
    public override bool Hide => true;
    public override bool UseParser => false;
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsSuperUser(caller);
    public override void Run(IMessageTarget caller, object? args)
    {
        if (caller is not GameObject go) { caller.Msg("You can't do that."); return; }
        go.Msg("Saving...");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // mirror Python: save_objects() + map.save() + node.save(force=True) + gametime.save if enabled
            ObjectRegistry.SaveObjects("save", force: true);
        }
        catch (Exception ex) { go.Msg($"SaveObjects failed: {ex.Message}"); }
        try { NodeHandler.GetCurrent()?.Save(force: true); } catch { }
        try { new MapHandler(null, false).Save(force: true); } catch { }
        if (AtherizSettings.Global.TimeSystemEnabled)
        {
            try { var gt = new GameTime(null, false); gt.Save(); } catch { }
        }
        sw.Stop();
        go.Msg($"Saved in {sw.Elapsed.TotalMilliseconds} milliseconds.");
    }
}