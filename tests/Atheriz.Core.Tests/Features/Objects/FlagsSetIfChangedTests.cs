using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Typed flag setters guard-set: a change marks modified, re-setting the same
// value is a no-op. Same change-detection the string TrySet had, without names.
[Collection("Ported")]
public class FlagsSetIfChangedTests
{
    private static void AssertGuarded(GameObject obj, Func<GameObject, bool> get, Action<GameObject, bool> set)
    {
        bool current = get(obj);
        obj.IsModified = false;
        set(obj, !current); // change: marks modified
        Assert.Equal(!current, get(obj));
        Assert.True(obj.IsModified);
        obj.IsModified = false;
        set(obj, !current); // same value: stays clean
        Assert.False(obj.IsModified);
        set(obj, current); // change back: marks modified
        Assert.True(obj.IsModified);
    }

    [Fact]
    public void AllGuardedFlags_GuardSetReturn()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var obj = GameObject.Create("flagbox");
            AssertGuarded(obj, o => o.IsPc, (o, v) => o.IsPc = v);
            AssertGuarded(obj, o => o.IsNpc, (o, v) => o.IsNpc = v);
            AssertGuarded(obj, o => o.IsItem, (o, v) => o.IsItem = v);
            AssertGuarded(obj, o => o.IsContainer, (o, v) => o.IsContainer = v);
            AssertGuarded(obj, o => o.IsScript, (o, v) => o.IsScript = v);
            AssertGuarded(obj, o => o.IsTickable, (o, v) => o.IsTickable = v);
            AssertGuarded(obj, o => o.IsAccount, (o, v) => o.IsAccount = v);
            AssertGuarded(obj, o => o.IsChannel, (o, v) => o.IsChannel = v);
            AssertGuarded(obj, o => o.IsNode, (o, v) => o.IsNode = v);
            AssertGuarded(obj, o => o.IsDeleted, (o, v) => o.IsDeleted = v);
            AssertGuarded(obj, o => o.IsConnected, (o, v) => o.IsConnected = v);
            AssertGuarded(obj, o => o.IsTemporary, (o, v) => o.IsTemporary = v);
            AssertGuarded(obj, o => o.IsBanned, (o, v) => o.IsBanned = v);
            AssertGuarded(obj, o => o.CanHear, (o, v) => o.CanHear = v);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
