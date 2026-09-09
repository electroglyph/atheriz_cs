// Port of atheriz/commands/loggedin/save.py:32

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
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        go.Msg("Saving...");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // Port of save.py:32 faithful order: save_objects() + map.save() + node.save(force=True) + gametime.save.
        // Uses the live singletons (never throwaway instances) and settings.SavePath (never hardcoded "save").
        // each save is guarded so one failure neither skips the
        // remaining saves nor leaves "Saving..." with no follow-up.
        int failures = 0;
        failures += TrySave(go, "objects", () => ObjectRegistry.SaveObjects());
        failures += TrySave(go, "map", () => GlobalServices.GetMapHandler().Save());
        failures += TrySave(go, "nodes", () => GlobalServices.GetNodeHandler().Save(force: true));
        if (AtherizSettings.Global.TimeSystemEnabled)
            failures += TrySave(go, "gametime", () => GlobalServices.GetGameTime().Save());
        sw.Stop();
        if (failures == 0) go.Msg($"Saved in {sw.Elapsed.TotalMilliseconds} milliseconds.");
        else go.Msg($"Save completed with {failures} error(s) in {sw.Elapsed.TotalMilliseconds} milliseconds.");
    }

    private static int TrySave(IMessageTarget go, string what, Action save)
    {
        try { save(); return 0; }
        catch (Exception ex) { go.Msg($"Save {what} failed: {ex.Message}"); return 1; }
    }
}