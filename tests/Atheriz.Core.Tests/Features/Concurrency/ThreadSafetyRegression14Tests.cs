using System.Reflection;
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Plugins;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Concurrency;

// Concurrency regression pins: door-dirty retention,
// fresh-transition survival, delete-vs-add refusal, raw-write tolerance,
// ban-kick snapshot, drain-retry guard. Real engine paths.
[Collection("Ported")]
public sealed class ThreadSafetyRegression14Tests
{
    private static bool NhDoorsDirty(NodeHandler nh)
    {
        var f = typeof(NodeHandler).GetField("_modified3", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("no _modified3");
        return (bool)f.GetValue(nh)!;
    }

    private static void NhDoorsClean(NodeHandler nh)
    {
        var f = typeof(NodeHandler).GetField("_modified3", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("no _modified3");
        f.SetValue(nh, false);
    }

    // Every single-field change marks, even inside a no-change
    // SetEndpoints defer window (generation-guarded, never suppressed).
    [Fact]
    public async Task DoorMark_DeferWindowChurn_NeverLosesDirty()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var cA = new Coord("b5", 0, 0, 0);
            var cB = new Coord("b5", 2, 0, 0);
            var door = new Door(cA, cB, "north", "south");
            NhDoorsClean(nh);
            for (int round = 0; round < 150; round++)
            {
                NhDoorsClean(nh);
                string exit = $"e{round}";
                using var barrier = new Barrier(2);
                var t1 = Task.Run(() =>
                {
                    barrier.SignalAndWait();
                    for (int i = 0; i < 200; i++)
                        door.SetEndpoints(cA, cB);
                });
                var t2 = Task.Run(() =>
                {
                    barrier.SignalAndWait();
                    door.FromExit = exit;
                });
                await Task.WhenAll([t1, t2]).WaitAsync(TimeSpan.FromSeconds(30));
                Assert.True(NhDoorsDirty(nh), $"round {round}: change lost inside defer window");
            }
            Assert.Equal($"e{149}", door.FromExit);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    // Exact-key teardown — a fresh edge to the same dest from another
    // source survives a concurrent RemoveLink (handler-transition level).
    [Fact]
    public async Task RemoveLink_ConcurrentAddToSameDest_FreshTransitionSurvives()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var n1 = new Node(new Coord("b6a", 0, 0, 0));
            var n2 = new Node(new Coord("b6a", 4, 0, 0));
            var destB = new Coord("b6b", 0, 0, 0);
            try { ObjectRegistry.AddObject(n1); } catch { }
            try { ObjectRegistry.AddObject(n2); } catch { }
            for (int round = 0; round < 100; round++)
            {
                n1.AddLink(new NodeLink("e", destB, ["e"]));
                try { n2.RemoveLink("e"); } catch { }
                using var barrier = new Barrier(2);
                var t1 = Task.Run(() => { barrier.SignalAndWait(); n1.RemoveLink("e"); });
                var t2 = Task.Run(() => { barrier.SignalAndWait(); n2.AddLink(new NodeLink("e", destB, ["e"])); });
                await Task.WhenAll([t1, t2]).WaitAsync(TimeSpan.FromSeconds(30));
                var trans = nh.FindTransitions(toArea: "b6b");
                Assert.Contains(trans, t => t.FromCoord.Equals(n2.Coord) && t.ToCoord.Equals(destB));
                Assert.Contains(n2.GetLinks(), l => l.Coord.Equals(destB));
            }
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    // Real Delete vs plural AddObjects — a post-flag add never implants
    // into the deleted node, and the implant never homes the fresh object.
    [Fact]
    public async Task DeleteVsAddObjects_Race_NeverImplantsInDeletedNode()
    {
        using var env = GlobalTestEnv.Enter();
        for (int round = 0; round < 60; round++)
        {
            var n = new Node(new Coord("b7", round, 0, 0));
            try { ObjectRegistry.AddObject(n); } catch { }
            var fresh = GameObject.Create($"b7fresh{round}");
            // Registered: Delete's content sweep resolves members through
            // the registry, so an unregistered implant would be invisible to
            // the teardown and the oracle below would measure harness
            // absence, not the refusal.
            try { ObjectRegistry.AddObject(fresh); } catch { }
            using var barrier = new Barrier(2);
            var t1 = Task.Run(() => { barrier.SignalAndWait(); n.Delete(null); });
            var t2 = Task.Run(() => { barrier.SignalAndWait(); n.AddObjects([fresh]); });
            await Task.WhenAll([t1, t2]).WaitAsync(TimeSpan.FromSeconds(30));
            // No implant: a post-flag add must refuse, so a LIVE fresh
            // object is never homed in a deleted node. The add-wins ordering
            // instead ends both-dead (Delete detaches and deletes its
            // contents) — consistent, not an implant.
            if (!fresh.IsDeleted)
            {
                Assert.False(n.IsDeleted && n.ContentsSnapshot.Contains(fresh.Id));
                Assert.False(fresh.Location is Atheriz.Core.Persistence.Dto.LocationRef.ObjectLocation ol
                    && ol.ObjectId == n.Id && n.IsDeleted);
            }
        }
    }

    // Legacy raw-field writes racing the locked scan never throw (the
    // bounded retry absorbs them); helpers stay the cooperative path.
    [Fact]
    public async Task IsExcludedAssembly_RawWriteRace_NeverThrows()
    {
        using var env = GlobalTestEnv.Enter();
        const string scratch = "d10scratch-xyz";
        try
        {
            using var barrier = new Barrier(2);
            var t1 = Task.Run(() =>
            {
                barrier.SignalAndWait();
                for (int i = 0; i < 2000; i++)
                {
                    try { PluginReloader.ExcludedAssemblies.Add(scratch); } catch { }
                    try { PluginReloader.ExcludedAssemblies.Remove(scratch); } catch { }
                }
            });
            var t2 = Task.Run(() =>
            {
                barrier.SignalAndWait();
                for (int i = 0; i < 2000; i++)
                    PluginReloader.IsExcludedAssembly(i % 2 == 0 ? "d10scratch-xyz.dll" : "other.dll");
            });
            await Task.WhenAll([t1, t2]).WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally { try { PluginReloader.ExcludedAssemblies.Remove(scratch); } catch { } }
        Assert.DoesNotContain(scratch, PluginReloader.ExcludedAssemblies);
    }

    // Online ban Msg+Closes the live connection; offline
    // targets skip silently; dead sessions are never acted on.
    [Fact]
    public void BanKick_LiveConnSnapshot_KicksOnlineSkipsOffline()
    {
        using var env = GlobalTestEnv.Enter();
        var room = new Node(new Coord("e7k", 0, 0, 0));
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            nh.AddNode(room);
            var banner = GameObject.Create("e7banner", isPc: true);
            banner.PrivilegeLevel = Privilege.Admin;
            try { ObjectRegistry.AddObject(banner); } catch { }
            banner.MoveTo(room, force: true, announce: false);
            var victim = GameObject.Create("E7Victim", isPc: true);
            victim.PrivilegeLevel = Privilege.Player;
            try { ObjectRegistry.AddObject(victim); } catch { }
            victim.MoveTo(room, force: true, announce: false);
            var vconn = new TestConnection("e7victim");
            vconn.ClientHost = "10.9.9.9";
            victim.Session = vconn.Session;
            Assert.True(BanHelper.TryGetLiveConn(victim, out var sess, out var conn));
            Assert.Same(vconn.Session, sess);
            Assert.NotNull(conn);
            var offline = GameObject.Create("E7Offline", isPc: true);
            try { ObjectRegistry.AddObject(offline); } catch { }
            offline.MoveTo(room, force: true, announce: false);
            Assert.False(BanHelper.TryGetLiveConn(offline, out _, out _));
            var cmd = new BanCommand();
            var pa = cmd.Parser!.ParseArgs(["E7Victim"]);
            new BanCommand().Run(new CommandContext(banner, null, pa, "", CancellationToken.None));
            Assert.Contains(vconn.Sent, s => s.Cmd == "text" && s.Args.Count > 0
                && (s.Args[0]?.ToString() ?? "").Contains("banned"));
            var pa2 = cmd.Parser!.ParseArgs(["E7Offline"]);
            var before = vconn.Sent.Count;
            new BanCommand().Run(new CommandContext(banner, null, pa2, "", CancellationToken.None));
            Assert.Equal(before, vconn.Sent.Count);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    // The drain retry never arms past dispose (drop-at-source, not just
    // drop-at-drain).
    [Fact]
    public void RetryDrain_GuardsDisposed()
    {
        var src = Features.Regression.SourceScan.Read("src", "Atheriz.Core", "Network", "BaseConnection.cs");
        var region = Features.Regression.SourceScan.Region(src, "private void RetryDrain()");
        Assert.Contains("_disposed", region);
    }
}
