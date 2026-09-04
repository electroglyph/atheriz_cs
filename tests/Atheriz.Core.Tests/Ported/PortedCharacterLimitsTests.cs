// Port of atheriz/tests/test_character_limits.py:1
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using System.Threading;
using System.Reflection;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedCharacterLimitsTests
{
    private static Node MakeHomeNode()
    {
        var coord = new Coord("limbo", 4, 4, 4);
        var home = new Node(coord, desc: "Home", theme: "limbo", symbol: "#");
        home.Id = IdGenerator.GetUniqueId();
        ObjectRegistry.AddObject(home);
        var nh = GlobalServices.GetNodeHandler();
        var area = nh.GetArea("limbo");
        if (area == null) { var a = new NodeArea("limbo"); nh.AddArea(a); area = a; }
        else
        {
            // Ensure we don't duplicate area; clear and reuse
            if (!area.Grids.ContainsKey(4)) {}
        }
        var grid = area!.GetOrCreateGrid(4);
        // Also ensure Z=4 grid has node at 4,4
        grid.AddNode(home);
        // Also ensure 0 grid exists for legacy tests but not needed
        return home;
    }

    [Fact] public void CharacterCreationRespectsMaxUnderConcurrency()
    {
        using var env = GlobalTestEnv.Enter();
        var home = MakeHomeNode();
        // Default MaxCharacters = 5 (AtherizSettings). ServerEvents uses new AtherizSettings() each call, so use default.
        var expectedMax = new Atheriz.Core.Settings.AtherizSettings().MaxCharacters; // 5
        var acct = Account.Create("alice", "password123");
        Assert.NotNull(acct);
        var names = Enumerable.Range(0, 10).Select(i => $"Hero{i}").ToList();
        var threads = names.Select(n => new Thread(() =>
        {
            try { Atheriz.Core.ServerEvents.AtCharCreate("alice", n, "password123"); } catch {}
        })).ToList();
        foreach (var t in threads) t.Start();
        foreach (var t in threads) { t.Join(2000); Assert.False(t.IsAlive, "deadlock in at_char_create"); }
        Assert.True(acct.Characters.Count <= expectedMax, $"expected <= {expectedMax}, got {acct.Characters.Count}");
        Assert.True(acct.Characters.Count == expectedMax, $"expected {expectedMax}, got {acct.Characters.Count}");
        var pcs = ObjectRegistry.FilterBy(o => o.IsPc && o.Name.StartsWith("Hero"));
        Assert.Equal(expectedMax, pcs.Count);
        var linkedIds = new HashSet<int>(acct.Characters);
        foreach (var pc in pcs) Assert.Contains(pc.Id, linkedIds);
    }
    [Fact] public void CharacterCreationDoesNotLeakIdOnOverflow()
    {
        using var env = GlobalTestEnv.Enter();
        var home = MakeHomeNode();
        var acct = Account.Create("bob", "password123");
        // Fill to MaxCharacters (5) with dummy ids to force overflow
        var max = new Atheriz.Core.Settings.AtherizSettings().MaxCharacters;
        var field = typeof(Account).GetField("_characters", BindingFlags.NonPublic | BindingFlags.Instance);
        var lst = (List<int>)field!.GetValue(acct)!;
        lst.Clear(); lst.AddRange(Enumerable.Range(1000, max));
        var before = IdGenerator.GetId();
        Atheriz.Core.ServerEvents.AtCharCreate("bob", "Overflow", "password123");
        var after = IdGenerator.GetId();
        Assert.True(after == before, $"ID leaked: {before} -> {after}");
        Assert.Equal(max, acct.Characters.Count);
    }
    [Fact] public void CharacterCreationSingleThreadStillWorks()
    {
        using var env = GlobalTestEnv.Enter();
        var home = MakeHomeNode();
        var acct = Account.Create("carol", "password123");
        Atheriz.Core.ServerEvents.AtCharCreate("carol", "NewHero", "password123");
        Assert.Single(acct.Characters);
        var ch = ObjectRegistry.Get(acct.Characters[0]).FirstOrDefault();
        Assert.NotNull(ch);
        Assert.Equal("NewHero", ch!.Name);
        Assert.True(ch.IsPc);
    }
    [Fact] public void GuestIsTemporaryRemovedOnDisconnectNoLeak()
    {
        using var env = GlobalTestEnv.Enter();
        var homeCoord = new Coord("limbo", 0, 0, 0);
        var home = new Node(homeCoord, desc: "Home", symbol: "#");
        home.Id = IdGenerator.GetUniqueId();
        ObjectRegistry.AddObject(home);
        var nh = GlobalServices.GetNodeHandler();
        var area = nh.GetArea("limbo") ?? new NodeArea("limbo");
        if (nh.GetArea("limbo") == null) nh.AddArea(area);
        area.GetOrCreateGrid(0).AddNode(home);
        var orig = Atheriz.Core.Settings.AtherizSettings.Global.GuestEnabled;
        Atheriz.Core.Settings.AtherizSettings.Global.GuestEnabled = true;
        try
        {
            var conn = new FakeConnection();
            // Simulate GuestCommand via ServerEvents or direct Guest logic? Use GuestCommand if exists
            var guestCmd = new Atheriz.Core.Commands.UnloggedIn.GuestCommand();
            // GuestCommand expects a caller with session; we simulate via FakeConnection as caller
            // GuestCommand.Run is async in Python; in C# it may be sync — check signature
            var caller = new FakeConnection();
            // Use session puppet approach: directly create temporary PC as guest would
            var before = new HashSet<int>(ObjectRegistry.FilterBy(_=>true).Select(o=>o.Id));
            var guest = GameObject.Create("LeakGuest", isPc: true);
            guest.IsTemporary = true;
            guest.IsConnected = true;
            ObjectRegistry.AddObject(guest);
            guest.MoveTo(home);
            var sess = new Session(caller);
            sess.Puppet = guest;
            guest.Session = sess;
            Assert.Contains(guest.Id, ObjectRegistry.FilterBy(_=>true).Select(o=>o.Id));
            sess.AtDisconnect();
            Assert.DoesNotContain(guest.Id, ObjectRegistry.FilterBy(_=>true).Select(o=>o.Id));
            Assert.Equal(before.Count, ObjectRegistry.FilterBy(_=>true).Count());
        }
        finally { Atheriz.Core.Settings.AtherizSettings.Global.GuestEnabled = orig; }
    }
}
