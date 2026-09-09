using System.Reflection;
using Atheriz.Core;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Objects;

// Object messaging and doors: sound emission, message parsing, and dirty tracking.
[Collection("Ported")]
public class ObjectMessagingTests
{
    private static void RegisterAll(params GameObject[] objs)
    {
        foreach (var o in objs)
            if (ObjectRegistry.Get(o.Id).Count == 0)
                ObjectRegistry.AddObject(o);
    }

    // --- Hear veto/addressing/desc-only ---

    [Fact]
    public void EmitSound_DescOnly_ReachesHearer()
    {
        // AtHear renders desc+msg, so a desc-only sound still propagates;
        // only a fully empty sound is dropped.
        ObjectRegistry.ClearAll();
        try
        {
            var room = GameObject.Create("room", isContainer: true);
            var emitter = GameObject.Create("emitter");
            var hearer = GameObject.Create("hearer", isPc: true);
            RegisterAll(room, emitter, hearer);
            Assert.True(emitter.MoveTo(room));
            Assert.True(hearer.MoveTo(room));
            hearer.ClearMessages();
            emitter.AtEmitSound("a loud crash", "", 60.0, false);
            Assert.Contains(hearer.PeekMessages(), m => m.Contains("a loud crash"));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Hear_SameRoom_IsDirect_NotDirectional()
    {
        // Pin: same-room hearing has no direction suffix.
        ObjectRegistry.ClearAll();
        try
        {
            var room = GameObject.Create("room", isContainer: true);
            var emitter = GameObject.Create("emitter");
            var hearer = GameObject.Create("hearer", isPc: true);
            RegisterAll(room, emitter, hearer);
            Assert.True(emitter.MoveTo(room));
            Assert.True(hearer.MoveTo(room));
            hearer.ClearMessages();
            hearer.AtHear(emitter, "says", "hello", 70.0, true);
            var all = string.Join("\n", hearer.PeekMessages());
            Assert.Contains("sayshello", all);
            Assert.DoesNotContain("to the", all);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    private sealed class VetoNode : Node
    {
        public VetoNode(Coord c) : base(c) { }
        public override (bool ok, GameObject emitter, string desc, string msg, double loudness, bool isSay) AtPreHear(
            GameObject emitter, string soundDesc, string soundMsg, double loudness, bool isSay)
            => (false, emitter, soundDesc, soundMsg, loudness, isSay);
    }

    [Fact]
    public void Node_AtHear_Vetoed_ReturnsAttenuated_WithoutLocalDelivery()
    {
        // Python parity (nodes.py:325-326,335): a vetoed node returns
        // loudness-attenuation (still positive, so BFS may forward) but
        // delivers to nothing locally.
        ObjectRegistry.ClearAll();
        try
        {
            var node = new VetoNode(new Coord("limbo", 1, 0, 0));
            var emitter = GameObject.Create("emitter");
            var hearer = GameObject.Create("hearer", isPc: true);
            RegisterAll(node, emitter, hearer);
            Assert.True(hearer.MoveTo(node));
            hearer.ClearMessages();
            double ret = node.AtHear(emitter, "boom", "loud!", 60.0, false);
            Assert.Equal(50.0, ret);
            Assert.Empty(hearer.PeekMessages());
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    // --- Node.MsgContents double-parse ---

    [Fact]
    public void MsgContents_NodeAndContainerPaths_AgreeOnSingleParse()
    {
        // Node and container MsgContents paths must deliver the identical
        // single-parse result for identical input.
        ObjectRegistry.ClearAll();
        try
        {
            var actor = GameObject.Create("Ann");
            var cRoom = GameObject.Create("croom", isContainer: true);
            var bob1 = GameObject.Create("Bob");
            var node = new Node(new Coord("limbo", 2, 0, 0));
            var bob2 = GameObject.Create("Bob");
            RegisterAll(actor, cRoom, bob1, node, bob2);
            Assert.True(bob1.MoveTo(cRoom));
            Assert.True(bob2.MoveTo(node));
            var mapping1 = new Dictionary<string, object?> { ["k"] = "$you" };
            var mapping2 = new Dictionary<string, object?> { ["k"] = "$you" };
            cRoom.MsgContents("Say {k}.", actor, mapping1);
            node.MsgContents("Say {k}.", fromObj: actor, mapping: mapping2);
            var m1 = Assert.Single(bob1.PeekMessages());
            var m2 = Assert.Single(bob2.PeekMessages());
            Assert.Equal(m1, m2);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    // --- Door dirty asymmetry ---

    [Fact]
    public void Door_ForcePaths_MarkDoorsDirty_SameAsTryPaths()
    {
        // ForceOpen/ForceClose mark doors dirty like the Try paths, so an
        // incremental save (force:false) persists the forced state.
        using var env = GlobalTestEnv.Enter();
        var coordA = new Coord("limbo", 0, 0, 0);
        var coordB = new Coord("limbo", 2, 0, 0);
        try
        {
            // Control: TryOpen marks dirty -> incremental save persists.
            var nh1 = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh1);
            var opener = GameObject.Create("opener");
            var d1 = Door.Create(coordA, "east", coordB, "west", closed: true);
            nh1.AddDoor(d1);
            nh1.Save(force: true);
            Assert.True(d1.TryOpen(opener));
            nh1.Save(force: false);
            using (var db = new AtherizDbContext(env.TempPath))
            {
                var probe = new NodeHandler(autoLoad: false);
                probe.Load(db);
                var doors = probe.GetDoors(coordA);
                Assert.NotNull(doors);
                Assert.Contains(doors!.Values, d => !d.Closed);
            }

            // Experiment: ForceOpen must behave the same.
            NodeHandler.SetCurrent(null);
            var nh2 = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh2);
            var d2 = Door.Create(coordA, "east", coordB, "west", closed: true);
            nh2.AddDoor(d2);
            nh2.Save(force: true);
            Assert.True(d2.ForceOpen());
            Assert.False(d2.Closed);
            nh2.Save(force: false);
            using (var db = new AtherizDbContext(env.TempPath))
            {
                var probe = new NodeHandler(autoLoad: false);
                probe.Load(db);
                var doors = probe.GetDoors(coordA);
                Assert.NotNull(doors);
                Assert.Contains(doors!.Values, d => !d.Closed);
            }
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void Door_TryOpenClose_Roundtrip_State()
    {
        // Pin: idempotent open/close both report success — when the door state
        // already matches what was wanted, the answer is true (owner decision
        // 2026-09-08; previously already_closed reported false).
        var d = Door.Create(new Coord("limbo", 0, 0, 0), "east", new Coord("limbo", 2, 0, 0), "west", closed: true);
        var caller = GameObject.Create("opener");
        Assert.True(d.TryOpen(caller));
        Assert.False(d.Closed);
        Assert.True(d.TryOpen(caller));
        Assert.True(d.TryClose(caller));
        Assert.True(d.Closed);
        Assert.True(d.TryClose(caller));
    }
}
