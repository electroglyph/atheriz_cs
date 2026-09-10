using System.Text.Json;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Objects;

// Pins for object lifecycle/move/account/script behavior: each case is either a
// race/perf/hygiene shape with no runtime-observable difference (pinned by
// source scan via SourceScan) or a behavioral change pinned by a test that
// fails on the old code.
[Collection("Ported")]
public class ObjectMoveAccountScriptTests
{
    // The singleton caches its GlobalServices fallback so node-moves after
    // the first lookup skip the global lookup entirely.
    [Fact]
    public void MapHandlerSingleton_Get_CachesFallback()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Objects", "GameObject.Move.cs");
        var region = SourceScan.Region(src, "public static MapHandler? Get()");
        Assert.Contains("_instance = GlobalServices.GetMapHandler()", region);
    }

    [Fact]
    public void MapHandlerSingleton_Set_StillWinsOverFallback()
    {
        var t = typeof(GameObject).Assembly.GetType("Atheriz.Core.Objects.MapHandlerSingleton");
        Assert.NotNull(t);
        var field = t.GetField("_instance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        var prior = field.GetValue(null);
        try
        {
            var mh = new MapHandler(autoLoad: false);
            var set = t.GetMethod("Set");
            Assert.NotNull(set);
            set.Invoke(null, new object[] { mh });
            var get = t.GetMethod("Get");
            Assert.NotNull(get);
            Assert.Same(mh, get.Invoke(null, null));
        }
        finally { field.SetValue(null, prior); }
    }

    // AddExits takes only its target: the old internalCall flag is gone, so
    // no caller can grab the live link list instead of a snapshot.
    [Fact]
    public void AddExits_TakesOnlyItsTarget()
    {
        var m = typeof(Node).GetMethod("AddExits");
        Assert.NotNull(m);
        Assert.Single(m.GetParameters());
    }

    [Fact]
    public void AddExits_InstallsExitCommandsFromSnapshot()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var node = new Node(new Coord("objmove", 0, 0, 0));
            node.AddLink(new NodeLink("north", new Coord("objmove", 0, 1, 0), new List<string> { "n" }));
            var obj = GameObject.Create("walker");
            node.AddExits(obj);
            Assert.NotNull(obj.InternalCmdSet);
            Assert.Contains(obj.InternalCmdSet.GetAll(), c => c.Tag == "exits");
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    // The old-location resolve keeps its ever-created fallback: the comment
    // now describes the fallback as intentional instead of claiming removal.
    [Fact]
    public void Move_OldLocation_KeepsEverCreatedFallback()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Objects", "GameObject.Move.cs");
        Assert.Contains("GetEver(olLoc.ObjectId)", src);
        Assert.DoesNotContain("removed the ever-created resurrection cache", src);
        Assert.Contains("ever-created registry entry", src);
    }

    // Deleting with no caller is a live path (every caller-less Delete
    // route): it succeeds without any null-suppression at the call site.
    [Fact]
    public void AccountDelete_NullCaller_Succeeds()
    {
        using var env = GlobalTestEnv.Enter();
        var acc = Account.Create("nullcaller", "pw");
        if (ObjectRegistry.Get(acc.Id).Count == 0) ObjectRegistry.AddObject(acc);
        Assert.True(acc.Delete(null));
        Assert.True(acc.IsDeleted);
    }

    [Fact]
    public void AccountDelete_PassesCallerStraightThrough()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Objects", "Account.cs");
        var region = SourceScan.Region(src, "internal (int count, List<object> ops)? DeleteImmediate");
        Assert.Contains("AtDelete(caller)", region);
        Assert.DoesNotContain("caller!", region);
    }

    // Script.Child reads under the script lock: a worker read blocks while a
    // writer holds the lock instead of observing a half-published re-attach.
    [Fact]
    public void ScriptChild_Read_BlocksUnderWriteLock()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var child = GameObject.Create("child");
            var script = new Script();
            script.InstallHooks(child);
            Assert.Same(child, script.Child);
            script.SyncRoot.EnterWriteLock();
            var task = Task.Run(() => script.Child);
            try
            {
                Assert.False(task.Wait(TimeSpan.FromMilliseconds(300)), "Child read completed without acquiring the read lock");
            }
            finally { script.SyncRoot.ExitWriteLock(); }
            Assert.True(task.Wait(TimeSpan.FromSeconds(10)), "Child read did not finish after the lock was released");
            Assert.Same(child, task.Result);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void ScriptRemoveHooks_DefaultDetachesInstalledChild()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var child = GameObject.Create("hookchild");
            var script = new Script();
            script.InstallHooks(child);
            script.RemoveHooks();
            Assert.Null(script.Child);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    // Extra-JSON lookup is a direct locked read with no dummy delegate.
    [Fact]
    public void TryGetExtraJson_RoundtripsValue()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var o = GameObject.Create("extra");
            o.SetExtraJson("score", JsonSerializer.SerializeToElement(42));
            Assert.True(o.TryGetExtraJson("score", out var je));
            Assert.Equal(42, je.GetInt32());
            Assert.False(o.TryGetExtraJson("missing", out _));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void TryGetExtraJson_HasNoDummyDelegate()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Objects", "GameObject.cs");
        var region = SourceScan.Region(src, "public bool TryGetExtraJson");
        Assert.DoesNotContain("return 0;", region);
    }

    // Clearing followers that removes nothing leaves the object clean: no
    // spurious checkpoint write for a no-op.
    [Fact]
    public void ClearFollowersExcept_LeavesCleanWhenNothingRemoved()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var o = GameObject.Create("followed");
            var friend = GameObject.Create("friend");
            o.AddFollower(friend.Id);
            o.IsModified = false;
            o.ClearFollowersExcept(new HashSet<int> { friend.Id });
            Assert.False(o.IsModified);
            o.ClearFollowersExcept(new HashSet<int>());
            Assert.True(o.IsModified);
            Assert.Empty(o.FollowersSnapshot);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
