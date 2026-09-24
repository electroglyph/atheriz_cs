using Atheriz.Core.Concurrency;
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Core.Tests;
using Atheriz.Core;

namespace Atheriz.Core.Tests.Features.Simplify;

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
