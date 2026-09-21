using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Plugins;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Plugins;

// Patch-construction pins: GetUninitializedObject bypasses the ctor AND field
// initializers, so the replacement's own containers stay null unless its
// AtInit re-creates them (PatchLiveObjects always drives
// ResolveRelations→AtInit after patching). Live symptom: patched replacements
// threw NullReferenceException on first touch of replacement-owned state.
[Collection("Ported")]
public class PluginPatchRepairTests
{
    private class RepairOld : GameObject
    {
        public int Kept = 7;
    }
    private sealed class RepairNew : RepairOld
    {
        private Dictionary<int, string>? _newOnly;
        public RepairNew() { throw new InvalidOperationException("ctor must be bypassed"); }
        public override void AtInit() { _newOnly ??= new(); _newOnly.TryAdd(0, "init"); base.AtInit(); }
        public int NewOnlyCount => _newOnly?.Count ?? -1;
    }

    [Fact]
    public void PatchLiveObjects_NewOnlyContainer_InitializedOldStateKept()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = new RepairOld { Kept = 42 };
        ObjectRegistry.AddObject(obj);
        var patched = PluginReloader.PatchLiveObjects(typeof(RepairOld), typeof(RepairNew));
        Assert.Equal(1, patched);
        var repl = Assert.IsType<RepairNew>(ObjectRegistry.Get(obj.Id).FirstOrDefault());
        Assert.Equal(42, repl.Kept);
        // Repair + ResolveRelations→AtInit ran: container usable, init touched it.
        Assert.Equal(1, repl.NewOnlyCount);
    }

    [Fact]
    public void PatchLiveObjects_AtInitAfterPatch_DoesNotThrow()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = new RepairOld();
        ObjectRegistry.AddObject(obj);
        PluginReloader.PatchLiveObjects(typeof(RepairOld), typeof(RepairNew));
        var repl = Assert.IsType<RepairNew>(ObjectRegistry.Get(obj.Id).FirstOrDefault());
        var ex = Record.Exception(() => repl.AtInit());
        Assert.Null(ex);
    }
}
