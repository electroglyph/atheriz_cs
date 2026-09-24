using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Plugins;

namespace Atheriz.Core.Tests.Ported;

// Explicit migration contract: a replacement implementing IMigrateFrom<T>
// constructs normally (ctor runs — unlike the legacy copy path) and pulls
// state via MigrateFrom. A broken contract keeps the old instance live.
[Collection("Ported")]
public sealed class PortedMigrateFromTests
{
    private sealed class OldSword : GameObject
    {
        public int Damage = 5;
    }

    private sealed class NewSword : GameObject, IMigrateFrom<OldSword>
    {
        public static bool CtorRan;
        public int Damage;
        public NewSword() { CtorRan = true; Damage = -1; }
        public void MigrateFrom(OldSword old) { Damage = old.Damage * 2; }
    }

    private sealed class BrokenSword : GameObject, IMigrateFrom<OldSword>
    {
        public BrokenSword() { }
        public void MigrateFrom(OldSword old) => throw new InvalidOperationException("boom");
    }

    private sealed class CtorlessSword : GameObject, IMigrateFrom<OldSword>
    {
        public CtorlessSword(int required) { _ = required; }
        public void MigrateFrom(OldSword old) { }
    }

    [Fact]
    public void PatchLiveObjects_MigratedType_RunsCtorAndPullsState()
    {
        NewSword.CtorRan = false;
        var old = new OldSword { Damage = 7 };
        ObjectRegistry.AddObject(old);
        try
        {
            var patched = PluginReloader.PatchLiveObjects(typeof(OldSword), typeof(NewSword));
            Assert.Equal(1, patched);
            Assert.True(NewSword.CtorRan);
            var live = ObjectRegistry.FilterBy(o => o.GetType() == typeof(NewSword));
            var replacement = Assert.Single(live);
            Assert.Equal(14, ((NewSword)replacement).Damage);
            Assert.Equal(old.Id, replacement.Id);
        }
        finally { Cleanup<OldSword>(); Cleanup<NewSword>(); }
    }

    [Fact]
    public void PatchLiveObjects_ThrowingMigrate_KeepsOldInstance()
    {
        var old = new OldSword { Damage = 7 };
        ObjectRegistry.AddObject(old);
        try
        {
            var patched = PluginReloader.PatchLiveObjects(typeof(OldSword), typeof(BrokenSword));
            Assert.Equal(0, patched);
            Assert.Empty(ObjectRegistry.FilterBy(o => o.GetType() == typeof(BrokenSword)));
            Assert.Contains(old.Id, ObjectRegistry.FilterBy(o => o.GetType() == typeof(OldSword)).Select(o => o.Id));
        }
        finally { Cleanup<OldSword>(); Cleanup<BrokenSword>(); }
    }

    [Fact]
    public void PatchLiveObjects_MissingParameterlessCtor_KeepsOldInstance()
    {
        var old = new OldSword();
        ObjectRegistry.AddObject(old);
        try
        {
            var patched = PluginReloader.PatchLiveObjects(typeof(OldSword), typeof(CtorlessSword));
            Assert.Equal(0, patched);
            Assert.Contains(old.Id, ObjectRegistry.FilterBy(o => o.GetType() == typeof(OldSword)).Select(o => o.Id));
        }
        finally { Cleanup<OldSword>(); Cleanup<CtorlessSword>(); }
    }

    private static void Cleanup<T>() where T : GameObject
    {
        foreach (var o in ObjectRegistry.FilterBy(o => o.GetType() == typeof(T)).ToList())
            ObjectRegistry.RemoveObject(o);
    }
}
