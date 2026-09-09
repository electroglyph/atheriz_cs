using Atheriz.Core;
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// Wander counts must be validated as positive (WanderCommand.cs:19-21,31-40).
[Collection("Ported")]
public class WanderCommandTests
{
    private static (NodeHandler Nh, Node Room, GameObject Builder) SetupRoom()
    {
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var coord = new Coord("limbo", 0, 0, 0);
        var node = new Node(coord);
        nh.AddNode(node);
        var builder = GameObject.Create("bob", privilege: Privilege.Builder);
        ObjectRegistry.AddObject(builder);
        Assert.True(builder.MoveTo(node));
        builder.ClearMessages();
        return (nh, node, builder);
    }

    private static void Teardown()
    {
        NodeHandler.SetCurrent(null);
        ObjectRegistry.ClearAll();
    }

    [Fact]
    public void Wander_NegativeCount_IsRejected()
    {
        // A negative count must never be reported as spawned.
        ObjectRegistry.ClearAll();
        NodeHandler.SetCurrent(null);
        try
        {
            var (_, _, builder) = SetupRoom();
            int npcsBefore = ObjectRegistry.FilterBy(o => o.IsNpc).Count;
            var pa = new GameArgumentParser.ParsedArgs();
            pa["count"] = -5;
            new WanderCommand().Run(builder, pa);
            var msgs = string.Join("\n", builder.PeekMessages());
            Assert.DoesNotContain("Spawned -5", msgs);
            Assert.Equal(npcsBefore, ObjectRegistry.FilterBy(o => o.IsNpc).Count);
        }
        finally { Teardown(); }
    }

    [Fact]
    public void Wander_ZeroCount_IsRejected()
    {
        // Zero must not be reported as a successful spawn either.
        ObjectRegistry.ClearAll();
        NodeHandler.SetCurrent(null);
        try
        {
            var (_, _, builder) = SetupRoom();
            var job = CommandDispatcher.DispatchLoggedIn(builder, "wander 0", immediate: true);
            Assert.NotNull(job);
            job!.Func(job.Caller, job.Args);
            var msgs = string.Join("\n", builder.PeekMessages());
            Assert.DoesNotContain("Spawned 0", msgs);
            Assert.Empty(ObjectRegistry.FilterBy(o => o.IsNpc));
        }
        finally { Teardown(); }
    }

    [Fact]
    public void Wander_SpawnedNpcs_HaveDistinctIds()
    {
        // A3-C-12: the wanderer ctor peeked the counter (GetId) instead of
        // allocating (GetUniqueId), so every wanderer shared one Id.
        ObjectRegistry.ClearAll();
        NodeHandler.SetCurrent(null);
        try
        {
            var (_, _, builder) = SetupRoom();
            var pa = new GameArgumentParser.ParsedArgs();
            pa["count"] = 3;
            new WanderCommand().Run(builder, pa);
            var ids = ObjectRegistry.FilterBy(o => o.IsNpc).Select(o => o.Id).ToList();
            Assert.Equal(3, ids.Count);
            Assert.All(ids, id => Assert.NotEqual(-1, id));
            Assert.Equal(3, ids.Distinct().Count());
        }
        finally { Teardown(); }
    }
}
