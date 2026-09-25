using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Concurrency;

// Concurrency regression pins: parser/social registration churn, stale-kick
// refusal, ban-scope uniformity, door-place joiner safety, door re-validation,
// single-generation settings reads, link-copy coherence, dead draw-key hint.
// All exercise real engine paths; no mocks.
[Collection("Ported")]
public sealed class ThreadSafetyRegression8Tests
{
    private sealed class TransferScript : Script
    {
        public Channel? Ch;
        public GameObject? NewLeader;
        public TransferScript() { Name = "e5transfer"; }
        [After]
        public bool at_msg_receive(string? text, GameObject? fromObj, string? msgType)
        {
            // Simulate the racing LeaveOp transfer landing mid-kick.
            if (Ch is not null && NewLeader is not null) Ch.CreatedBy = NewLeader.Id;
            return true;
        }
    }

    private sealed class RacyDoor : Door
    {
        public RacyDoor(Coord from, Coord to, string fromExit, string toExit)
            : base(from, to, fromExit, toExit, null, "", "", true, false) { }
        public override bool TryOpen(GameObject caller)
        {
            bool ok = base.TryOpen(caller);
            // Simulate the racing close landing between verdict and move.
            ForceClose();
            return ok;
        }
    }

    // Parses racing custom-func registration never throw.
    [Fact]
    public async Task FuncParser_ConcurrentRegistration_NeverThrows()
    {
        using var env = GlobalTestEnv.Enter();
        var actor = GameObject.Create("e1actor", isPc: true);
        var receiver = GameObject.Create("e1recv", isPc: true);
        using var barrier = new Barrier(2);
        var parse = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (int i = 0; i < 2000; i++)
                _ = FuncParser.Parse("$you() hi", actor, receiver, null, false);
        });
        var mutate = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (int i = 0; i < 200; i++)
            {
                FuncParser.AddActorStanceCallable("e1scratch", (a, k, ctx, raw) => "x");
                FuncParser.RemoveActorStanceCallable("e1scratch");
            }
        });
        await Task.WhenAll([parse, mutate]).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.DoesNotContain("e1scratch", FuncParser.ActorStanceCallables.Keys);
    }

    // Social dispatch racing table mutation never throws.
    [Fact]
    public async Task Socials_ConcurrentMutation_NeverThrows()
    {
        using var env = GlobalTestEnv.Enter();
        var cmd = new SocialsCommand();
        using var barrier = new Barrier(2);
        var read = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (int i = 0; i < 2000; i++)
            {
                _ = cmd.Aliases.Count;
                _ = SocialsCommand.TryGetSocial("smile", out _);
            }
        });
        var mutate = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (int i = 0; i < 200; i++)
            {
                SocialsCommand.AddSocial("e2scratch", ("a", "b"));
                SocialsCommand.RemoveSocial("e2scratch");
            }
        });
        await Task.WhenAll([read, mutate]).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.DoesNotContain("e2scratch", cmd.Aliases);
    }

    // A leadership transfer mid-kick refuses the stale kick.
    [Fact]
    public void GroupKick_TransferMidKick_RefusesStaleKick()
    {
        using var env = GlobalTestEnv.Enter();
        var room = new Node(new Coord("e5area", 0, 0, 0));
        try { ObjectRegistry.AddObject(room); } catch { }
        var a = GameObject.Create("e5anna", isPc: true);
        var b = GameObject.Create("e5bob", isPc: true);
        var c = GameObject.Create("e5cat", isPc: true);
        // Builders see offline PCs in search (regular players do not).
        a.PrivilegeLevel = Privilege.Builder;
        var plant = GameObject.Create("e5plant", isPc: true);
        foreach (var o in new[] { a, b, c, plant })
        {
            try { ObjectRegistry.AddObject(o); } catch { }
            o.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(room.Coord);
            room.AddObject(o);
        }
        var ch = Channel.Create("e5group");
        try { ObjectRegistry.AddObject(ch); } catch { }
        ch.CreatedBy = a.Id;
        ch.AddListener(a);
        ch.AddListener(b);
        ch.AddListener(c);
        var transfer = new TransferScript { Ch = ch, NewLeader = b };
        try { ObjectRegistry.AddObject(transfer); } catch { }
        plant.AddScript(transfer);
        ch.AddListener(plant);
        a.GroupChannel = ch.Id;
        b.GroupChannel = ch.Id;
        c.GroupChannel = ch.Id;
        a.ClearMessages();
        new GroupCommand().Run(a, new GroupCommand().Parser!.ParseArgs(["kick", "e5cat"]));
        // The transfer landed before the removal: the stale kick refuses.
        Assert.Contains(ch.Listeners, id => id == c.Id);
        Assert.Contains(a.PeekMessages(), m => m.Contains("not the leader"));
    }

    // Concurrent ban/unban of one scope never leaves a mixed scope.
    [Fact]
    public async Task BanScope_ConcurrentBanUnban_StaysUniform()
    {
        using var env = GlobalTestEnv.Enter();
        var admin = GameObject.Create("e7admin", isPc: true);
        admin.PrivilegeLevel = Privilege.Builder;
        try { ObjectRegistry.AddObject(admin); } catch { }
        var acc = Account.Create("e7acc", "e7password123");
        var c1 = GameObject.Create("e7victim", isPc: true);
        var c2 = GameObject.Create("e7sibling", isPc: true);
        Assert.True(acc.TryAddCharacter(c1, 10));
        Assert.True(acc.TryAddCharacter(c2, 10));
        var scope = new[] { acc, c1, c2 };
        for (int round = 0; round < 30; round++)
        {
            foreach (var o in scope) o.IsBanned = false;
            var banPa = new BanCommand().Parser!.ParseArgs(["e7victim", "--account"]);
            var unbanPa = new UnbanCommand().Parser!.ParseArgs(["e7victim", "--account"]);
            using var barrier = new Barrier(2);
            var t1 = Task.Run(() => { barrier.SignalAndWait(); new BanCommand().Run(admin, banPa); });
            var t2 = Task.Run(() => { barrier.SignalAndWait(); new UnbanCommand().Run(admin, unbanPa); });
            await Task.WhenAll([t1, t2]).WaitAsync(TimeSpan.FromSeconds(30));
            bool first = scope[0].IsBanned;
            Assert.True(scope.All(o => o.IsBanned == first));
        }
    }

    // A joiner racing door placement is never stranded in a removed node.
    [Fact]
    public async Task DoorPlace_ConcurrentJoiner_NeverStranded()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            for (int round = 0; round < 30; round++)
            {
                int z = round + 100;
                var loc = new Node(new Coord("e11area", 0, 0, z));
                var doorNode = new Node(new Coord("e11area", 0, 1, z));
                var dest = new Node(new Coord("e11area", 0, 2, z));
                nh.AddNode(loc);
                nh.AddNode(doorNode);
                nh.AddNode(dest);
                var builder = GameObject.Create("e11builder", isPc: true);
                builder.PrivilegeLevel = Privilege.Builder;
                var victim = GameObject.Create("e11victim", isPc: true);
                try { ObjectRegistry.AddObject(builder); } catch { }
                try { ObjectRegistry.AddObject(victim); } catch { }
                Assert.True(builder.MoveTo(loc, force: true, announce: false));
                Assert.True(victim.MoveTo(loc, force: true, announce: false));
                var doorPa = new DoorCommand().Parser!.ParseArgs(["-n"]);
                using var barrier = new Barrier(2);
                var t1 = Task.Run(() => { barrier.SignalAndWait(); new DoorCommand().Run(builder, doorPa); });
                var t2 = Task.Run(() => { barrier.SignalAndWait(); victim.MoveTo(doorNode, force: true, announce: false); });
                await Task.WhenAll([t1, t2]).WaitAsync(TimeSpan.FromSeconds(30));
                // Either the victim was evacuated (node removed) or the
                // removal was refused (node kept) — never stranded inside a
                // removed node.
                bool removed = nh.GetNode(doorNode.Coord) is null;
                var victimLoc = victim.ResolveLocationObject();
                Assert.False(removed && victimLoc is not null && victimLoc.Id == doorNode.Id);
            }
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    // A close landing after the open verdict still refuses the move.
    [Fact]
    public void ExitMove_CloseAfterOpenVerdict_RefusesMove()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var r1 = new Node(new Coord("e13area", 0, 0, 0));
            var r2 = new Node(new Coord("e13area", 1, 0, 0));
            nh.AddNode(r1);
            nh.AddNode(r2);
            var door = new RacyDoor(r1.Coord, r2.Coord, "east", "west");
            nh.AddDoor(door);
            var mover = GameObject.Create("e13mover", isPc: true);
            try { ObjectRegistry.AddObject(mover); } catch { }
            Assert.True(mover.MoveTo(r1, force: true, announce: false));
            var exit = new LoggedInExitCommand { CallerId = mover.Id, Location = r1.Coord, Destination = r2.Coord, ExitName = "east" };
            exit.DoMove();
            Assert.Equal(r1.Coord, ((Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation)mover.Location).Coord);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    // The build cell observes one settings generation.
    [Fact]
    public void BuildCommand_SettingsReads_SingleGeneration()
    {
        using var env = GlobalTestEnv.Enter();
        var src = Tests.Features.Regression.SourceScan.Read("src", "Atheriz.Core", "Commands", "LoggedIn", "BuildingCommands.cs");
        Assert.Contains("var sset = AtherizSettings.Global;", src);
        Assert.Contains("var roomPH = sset.RoomPlaceholder;", src);
    }

    // Link snapshots are copies, never torn live refs.
    [Fact]
    public async Task GetRandomLink_ConcurrentRemap_AlwaysCoherent()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("e18area", 0, 0, 0));
        var c1 = new Coord("e18area", 1, 0, 0);
        var c2 = new Coord("e18area", 2, 0, 0);
        node.AddLink(new NodeLink("e", c1));
        var first = node.GetRandomLink();
        var second = node.GetRandomLink();
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal(first, second);
        using var barrier = new Barrier(2);
        var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        var writer = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (int i = 0; i < 500; i++)
            {
                try
                {
                    node.RemoveLink(i % 2 == 0 ? "e" : "f");
                    node.AddLink(new NodeLink(i % 2 == 0 ? "f" : "e", i % 2 == 0 ? c2 : c1));
                }
                catch (Exception ex) { errors.Enqueue(ex); }
            }
        });
        var reader = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (int i = 0; i < 2000; i++)
            {
                try
                {
                    var l = node.GetRandomLink();
                    if (l is null) continue;
                    bool coherent = (l.Name == "e" && l.Coord.Equals(c1)) || (l.Name == "f" && l.Coord.Equals(c2));
                    if (!coherent) errors.Enqueue(new InvalidOperationException($"torn link: {l}"));
                }
                catch (Exception ex) { errors.Enqueue(ex); }
            }
        });
        await Task.WhenAll([writer, reader]).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(errors.IsEmpty);
    }

    // A dead draw key fails closed with a reopen hint on the wire reason.
    [Fact]
    public void DrawKey_DeadOnArrival_RejectsUnknownKey()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection("e26draw");
        var sess = conn.Session;
        string key = MapEdit.Grant(conn.ClientHost ?? "?", "e26area", 0, sess);
        MapEdit.DiscardSession(sess);
        var result = MapEdit.Consume(key, conn.ClientHost ?? "?", 1);
        Assert.Equal(MapEditStatus.Reject, result.Status);
        Assert.Equal("unknown_key", result.Reason);
        var src = Tests.Features.Regression.SourceScan.Read("src", "Atheriz.Core", "Network", "InputFuncs.cs");
        Assert.Contains("Reopen the editor", src);
    }
}
