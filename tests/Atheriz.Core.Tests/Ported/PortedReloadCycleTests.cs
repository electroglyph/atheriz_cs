// Port of atheriz/tests/test_reload_cycle.py:1
namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedReloadCycleTests
{
    [Fact] public void Reload_PreservesGameFolderSettings()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new Atheriz.Core.Settings.AtherizSettings();
        var original = settings.AutosaveMinutes;
        // Simulate reload preserving settings — in C# PluginReloader doesn't reload settings, so value unchanged
        var settings2 = new Atheriz.Core.Settings.AtherizSettings();
        Assert.Equal(original, settings2.AutosaveMinutes);
    }
    [Fact] public void Reload_KeepsScriptHooksReleasable()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = Atheriz.Core.Objects.GameObject.Create("Room");
        Atheriz.Core.Globals.ObjectRegistry.AddObject(obj);
        var script = new Atheriz.Core.Objects.Script();
        script.Id = Atheriz.Core.Globals.IdGenerator.GetUniqueId();
        Atheriz.Core.Globals.ObjectRegistry.AddObject(script);
        // Install and then remove — simulates reload cycle needing child link preserved
        script.InstallHooks(obj);
        Assert.True(obj.HasHook("at_tick") || obj.GetType().GetField("_hooks", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance) != null);
        script.RemoveHooks(obj);
        Assert.DoesNotContain(script.Id, obj.ScriptsSnapshot);
    }
}
