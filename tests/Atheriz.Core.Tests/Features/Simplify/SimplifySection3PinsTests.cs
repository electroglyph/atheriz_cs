using Atheriz.Core.Commands;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Core.Tests;
using Atheriz.Core;

namespace Atheriz.Core.Tests.Features.Simplify;

// Merged from Section3BatchEPinsTests.cs
// Batch E (§3.1) pins: Lazy<T> publication, deleted twin shims, extracted
// LockTable/HookRegistry, split ban/cooldown stores.
[Collection("Ported")]
public class Section3BatchEPinsTests
{
    [Fact]
    public void GetOrCreateSingleton_Gone()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "GlobalServices.cs");
        Assert.DoesNotContain("GetOrCreateSingleton", src);
        Assert.DoesNotContain("_singletonLock", src);
        Assert.Contains("Lazy<", src);
    }

    [Fact]
    public void LockTable_IsSingleHome()
    {
        var go = SourceScan.Read("src", "Atheriz.Core", "Objects", "GameObject.cs");
        Assert.Contains("private readonly LockTable _lockTable = new();", go);
        Assert.DoesNotContain("Dictionary<string, List<LockEntry>> _locks", go);
        var door = SourceScan.Read("src", "Atheriz.Core", "Objects", "Door.cs");
        Assert.Contains("private readonly LockTable _lockTable = new();", door);
        Assert.DoesNotContain("Dictionary<string, List<LockEntry>> _locks", door);
        Assert.True(File.Exists(Path.Combine(SourceScan.RepoRoot(), "src", "Atheriz.Core", "Objects", "LockTable.cs")));
    }

    [Fact]
    public void HookRegistry_IsSingleHome()
    {
        var go = SourceScan.Read("src", "Atheriz.Core", "Objects", "GameObject.cs");
        Assert.Contains("private readonly HookRegistry _hookRegistry = new();", go);
        Assert.DoesNotContain("Dictionary<string, HashSet<Delegate>> _hooks", go);
        Assert.True(File.Exists(Path.Combine(SourceScan.RepoRoot(), "src", "Atheriz.Core", "Objects", "HookRegistry.cs")));
    }

    [Fact]
    public void Locks_And_Hooks_StillBehave()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var o = GameObject.Create("bantable");
            o.AddLock("get", _ => false);
            Assert.False(o.Access(GameObject.Create("intruder"), "get"));
            Assert.True(o.Access(GameObject.Create("intruder"), "missing"));
            o.ClearLocksByName("get");
            Assert.True(o.Access(GameObject.Create("intruder"), "get"));
            bool fired = false;
            o.InstallHook("custom_hook", (Action)(() => fired = true));
            Assert.True(o.HasHook("custom_hook"));
            // Unmarked delegates install but never dispatch: the original
            // runs and the hook stays silent (Hookable partition contract).
            Assert.Equal(0, o.Hookable("custom_hook", () => 0));
            Assert.False(fired);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Bans_LiveInIpBanStore()
    {
        ObjectRegistry.ClearAll();
        try
        {
            ObjectRegistry.BanIp("10.0.0.1", 9999999999);
            Assert.True(IpBanStore.IsIpBanned("10.0.0.1"));
            IpBanStore.UnbanIp("10.0.0.1");
            Assert.False(ObjectRegistry.IsIpBanned("10.0.0.1"));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Cooldowns_LiveInCreationCooldownStore()
    {
        ObjectRegistry.ClearAll();
        try
        {
            ObjectRegistry.ApplyCreationCooldown("account", "10.0.0.2", 1000, 60);
            Assert.True(CreationCooldownStore.CreationCooldownActive("10.0.0.2", 1010));
            CreationCooldownStore.ClearCreationCooldown("10.0.0.2");
            Assert.False(ObjectRegistry.CreationCooldownActive("10.0.0.2", 1010));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void BoundedDictionary_IsTopLevel()
    {
        Assert.NotNull(typeof(BoundedDictionary<,>));
        Assert.Null(typeof(ObjectRegistry).GetNestedType("BoundedDictionary`2"));
    }

    [Fact]
    public void SetAsyncTicker_IsLiveForTryGet()
    {
        var prior = GlobalServices.TryGetTicker();
        try
        {
            var t = GlobalServices.GetAsyncTicker();
            GlobalServices.SetAsyncTicker(t);
            Assert.Same(t, GlobalServices.TryGetTicker());
        }
        finally { GlobalServices.Reset(); }
    }

    [Fact]
    public void FaultedCreation_RetriesInsteadOfWedging()
    {
        using var env = GlobalTestEnv.Enter();
        GlobalServices.Reset();
        try
        {
            // Default settings carry an invalid SavePath outside a game
            // folder, so pinned creation fails like DoStartup's boot attempt
            // with a bad ambient path — and must not wedge the slot.
            var bad = new AtherizSettings();
            Assert.Throws<InvalidOperationException>(() => GlobalServices.GetNodeHandler(bad));
            // The fault stores nothing ...
            Assert.Null(GlobalServices.TryGetNodeHandler());
            // ... and the next ambient read retries instead of rethrowing.
            var h = GlobalServices.GetNodeHandler();
            Assert.NotNull(h);
            // First success wins: the pinned overload now sees the ambient one.
            Assert.Same(h, GlobalServices.GetNodeHandler(bad));
        }
        finally { GlobalServices.Reset(); }
    }
}

// Merged from Section3BRemovalPinsTests.cs
// Batch A removal pins: stringly-typed front doors deleted in favor of typed
// members. Each pin fails if the removed shape is reintroduced.
[Collection("Ported")]
public class Section3BRemovalPinsTests
{
    [Fact]
    public void Flags_TrySet_Removed()
    {
        Assert.Null(typeof(Flags).GetMethod("TrySet"));
    }

    [Fact]
    public void NodeArea_GetOrCreateGrid_Removed()
    {
        Assert.Null(typeof(NodeArea).GetMethod("GetOrCreateGrid"));
        Assert.NotNull(typeof(NodeArea).GetMethod("GetOrAddGrid"));
    }

    [Fact]
    public void Door_IsClosedIsLocked_Removed()
    {
        var props = typeof(Door).GetProperties().Select(p => p.Name).ToHashSet();
        Assert.DoesNotContain("IsClosed", props);
        Assert.DoesNotContain("IsLocked", props);
        Assert.Contains("Closed", props);
        Assert.Contains("Locked", props);
    }

    [Fact]
    public void Account_Delete_VirtualDispatch()
    {
        using var env = GlobalTestEnv.Enter();
        var acc = Account.Create("DispatchAccount", "password");
        GameObject asBase = acc;
        var res = asBase.Delete(null);
        Assert.NotNull(res);
        Assert.True(acc.IsDeleted);
        Assert.DoesNotContain(acc.Id, ObjectRegistry.FilterBy(_ => true).Select(o => o.Id));
    }
}

// Merged from Section3RegressionPinsTests.cs
// Section 3 regression pins: each new typed front door forwards to the same
// core, so behavior is identical and the pin fails if the door drifts.
[Collection("Ported")]
public class Section3RegressionPinsTests
{
    [Fact]
    public void MoveTo_TypedOverloads_ForwardToCore()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var room = GameObject.Create("room");
            var obj = GameObject.Create("walker");
            Assert.True(obj.MoveTo(room, force: true));
            Assert.Contains(obj.Id, room.ContentsSnapshot);
            Assert.True(obj.MoveToNowhere(force: true));
            Assert.Null(obj.ResolveLocationObject());
            // Unknown coord finds no node and reports false, like the core.
            Assert.False(obj.MoveTo(new Coord("nowhere", 1, 2, 3), force: true));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void MoveTo_NoLocationRefOverload_KeepsNullUnambiguous()
    {
        foreach (var m in typeof(GameObject).GetMethods().Where(m => m.Name == nameof(GameObject.MoveTo)))
            Assert.DoesNotContain("LocationRef", m.GetParameters()[0].ParameterType.Name);
        Assert.NotNull(typeof(GameObject).GetMethod("MoveToLocation"));
        Assert.NotNull(typeof(GameObject).GetMethod("MoveToNowhere"));
    }

    [Fact]
    public void BroadcastToContents_NodeOverrides_BaseKeepsContract()
    {
        var m = typeof(Node).GetMethod("BroadcastToContents");
        Assert.NotNull(m);
        Assert.Equal(typeof(Node), m.DeclaringType);
        var src = SourceScan.Read("src", "Atheriz.Core", "Objects", "ContentUtils.cs");
        var region = SourceScan.Region(src, "public static void EmitToLocation(");
        Assert.DoesNotContain("is Node", region);
    }

    [Fact]
    public void EntityKind_Classify_KeepsOldPrecedence()
    {
        Assert.Equal(EntityKind.Script, EntityKinds.Classify(new GameObjectDto { Type = "script", IsNode = true }));
        Assert.Equal(EntityKind.Channel, EntityKinds.Classify(new GameObjectDto { Type = "channel" }));
        Assert.Equal(EntityKind.Account, EntityKinds.Classify(new GameObjectDto { Type = "account" }));
        Assert.Equal(EntityKind.Node, EntityKinds.Classify(new GameObjectDto { Type = "node" }));
        Assert.Equal(EntityKind.Node, EntityKinds.Classify(new GameObjectDto { Type = "object", IsNode = true }));
        Assert.Equal(EntityKind.Object, EntityKinds.Classify(new GameObjectDto { Type = "bogus" }));
        Assert.Equal("node", EntityKinds.Name(EntityKind.Node));
    }

    [Fact]
    public void LockPolicies_Evaluate_MatchesPredicates()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var builder = GameObject.Create("builder");
            builder.PrivilegeLevel = Privilege.Builder;
            var other = GameObject.Create("other");
            Assert.True(LockPolicies.Evaluate(LockPolicies.LockPolicy.Builder, builder, other));
            Assert.False(LockPolicies.Evaluate(LockPolicies.LockPolicy.Builder, other, other));
            Assert.False(LockPolicies.Evaluate(LockPolicies.LockPolicy.NotSelf, other, other));
            Assert.True(LockPolicies.Evaluate(LockPolicies.LockPolicy.NotSelf, other, builder));
            Assert.False(LockPolicies.Evaluate(LockPolicies.LockPolicy.Denied, builder, other));
            Assert.False(LockPolicies.Evaluate(LockPolicies.LockPolicy.Custom, builder, other));
            // Non-PC targets are visible even offline, like the PcView closure.
            Assert.True(LockPolicies.Evaluate(LockPolicies.LockPolicy.PcView, other, builder));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void LegendEntry_ToJson_RoundTrips()
    {
        var e = new LegendEntry("S", "desc", (1, 2)) { Show = false, Fg = 12.0, Bg = 34.0 };
        var back = LegendEntry.FromJson(e.ToJson());
        Assert.Equal(e, back);
    }

    [Fact]
    public void Ticker_QuantizedKey_MergesFloatDust()
    {
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100, reliefLimit: 0);
        var ticker = new AsyncTicker(pool);
        try
        {
            ticker.AddCoro(() => Task.CompletedTask, 0.1 + 0.2);
            ticker.AddCoro(() => Task.CompletedTask, 0.3);
            Assert.Single(ticker.Slots);
            Assert.Equal(2, ticker.GetSlot(0.3)?.CoroCount);
        }
        finally { ticker.Clear(); ticker.Stop(); }
    }

    [Fact]
    public void GatherContents_MaxDepthParam_BoundsWalk()
    {
        using var env = GlobalTestEnv.Enter();
        var root = new GameObject(); root.Id = 900101; root.Name = "root"; ObjectRegistry.AddObject(root);
        var box = new GameObject(); box.Id = 900102; box.Name = "box"; box.IsContainer = true; ObjectRegistry.AddObject(box);
        var inner = new GameObject(); inner.Id = 900103; inner.Name = "inner"; ObjectRegistry.AddObject(inner);
        root.AddObject(box);
        box.AddObject(inner);
        GameObject? Resolve(int id) => ObjectRegistry.GetSingle(id);
        var shallow = ContentUtils.GatherContents(root, Resolve, maxDepth: 1);
        Assert.Contains(shallow, o => o.Id == box.Id);
        Assert.DoesNotContain(shallow, o => o.Id == inner.Id);
        var deep = ContentUtils.GatherContents(root, Resolve);
        Assert.Contains(deep, o => o.Id == inner.Id);
    }

    [Fact]
    public void Flags_TypedSetters_MarkModifiedOnlyOnChange()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var obj = GameObject.Create("flagbox");
            obj.IsModified = false;
            obj.IsPc = false; // no change: stays clean
            Assert.False(obj.IsModified);
            obj.IsPc = true;
            Assert.True(obj.IsPc);
            Assert.True(obj.IsModified);
            obj.IsModified = false;
            obj.IsPc = true; // no change: stays clean
            Assert.False(obj.IsModified);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    // Singletons publish via Lazy<T> (execution-and-publication): concurrent
    // getters observe the same instance with no manual lock, and the old
    // SingletonLock surface is gone.
    [Fact]
    public void Singletons_PublishOnceConcurrently()
    {
        var results = new CmdSet[8];
        System.Threading.Tasks.Parallel.For(0, 8, i => results[i] = GlobalServices.GetLoggedInCmdSet());
        foreach (var r in results) Assert.Same(results[0], r);
    }

    [Fact]
    public void SingletonLock_Surface_Removed()
    {
        Assert.Null(typeof(GlobalServices).GetProperty("SingletonLock"));
    }
}
