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
        // Port of save.py:32 faithful order: save_objects() + map.save() + node.save(force=True) + gametime.save.
        // Uses the live singletons (never throwaway instances) and settings.SavePath (never hardcoded "save").
        ObjectRegistry.SaveObjects();
        GlobalServices.GetMapHandler().Save();
        GlobalServices.GetNodeHandler().Save(force: true);
        if (AtherizSettings.Global.TimeSystemEnabled)
            GlobalServices.GetGameTime().Save();
        sw.Stop();
        go.Msg($"Saved in {sw.Elapsed.TotalMilliseconds} milliseconds.");
    }
}