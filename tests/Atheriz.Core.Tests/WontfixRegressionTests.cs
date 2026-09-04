using System.Reflection;
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests;

public class WontfixRegressionTests
{
    // Port of AGENTS.md put covers all placement wontfix: put.py:34-36 + drop.py:26
    [Fact]
    public void PutCoversAllPlacement()
    {
        ObjectRegistry.ClearAll(); IdGenerator.SetId(-1);
        var room = GameObject.Create("room"); room.IsContainer = true;
        var guest = GameObject.Create("guest");
        room.AddLock("put", _ => false);
        Assert.False(room.Access(guest, "put")); // put blocks
        Assert.True(room.Access(guest, "drop")); // no separate drop lock — wontfix
        Assert.Contains("put", CommandRegistry.LoggedIn.GetKeys());
        Assert.Contains("drop", CommandRegistry.LoggedIn.GetKeys());
        ObjectRegistry.ClearAll();
    }

    // Port of AGENTS.md no is_open on generic Object wontfix: base_obj.py:129 + contents.py:96-126
    [Fact]
    public void GenericObject_HasNoIsOpen()
    {
        var props = typeof(GameObject).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToHashSet();
        Assert.DoesNotContain("IsOpen", props);
        Assert.DoesNotContain("IsClosed", props);
        Assert.DoesNotContain("Locked", props);
        Assert.Contains("IsContainer", props);

        ObjectRegistry.ClearAll();
        var room = GameObject.Create("room"); room.IsContainer = true;
        var bag = GameObject.Create("bag"); bag.IsContainer = true;
        var coin = GameObject.Create("coin");
        room.AddContent(bag.Id); bag.AddContent(coin.Id);
        var dict = new Dictionary<int, GameObject> { [bag.Id] = bag, [coin.Id] = coin };
        GameObject? R(int id) => dict.TryGetValue(id, out var o) ? o : null;
        var gathered = ContentUtils.GatherContents(room, R);
        Assert.Contains(coin, gathered); // recurses regardless of open state — wontfix
        Assert.Contains(bag, gathered);
        ObjectRegistry.ClearAll();
    }

    // Port of AGENTS.md lazy channel cache wontfix: channel.py:17-18 lazy only
    [Fact]
    public void ChannelCache_IsLazyOnly()
    {
        var field = typeof(ChannelCommand).GetField("ChannelCache", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var cache = field!.GetValue(null) as Dictionary<string, Channel>;
        Assert.NotNull(cache);
        lock (cache!)
        {
            cache.Clear();
            var ch = new Channel { Name = "server", Desc = "test" };
            ch.Id = IdGenerator.GetUniqueId();
            ObjectRegistry.AddObject(ch);
            cache["server"] = ch;
            Assert.True(cache.ContainsKey("server"));
            ch.IsDeleted = true;
            // lazy: still present until is_deleted check
            Assert.True(cache.ContainsKey("server"));
            bool shouldRemove = cache["server"].IsDeleted || !cache["server"].Name.Equals("server", StringComparison.OrdinalIgnoreCase);
            Assert.True(shouldRemove);
            ObjectRegistry.RemoveObject(ch);
            cache.Clear();
        }
        ObjectRegistry.ClearAll();
    }

    private sealed class BeforeProbe
    {
        public bool Called;
        [Before] public void AtTestFunc() { Called = true; }
        [Before] public bool AtTestFuncBool() { Called = true; return false; }
    }

    // Port of AGENTS.md hookable before does not abort wontfix: base_obj.py:46-73 + base_script.py:19-32
    [Fact]
    public void Hookable_BeforeDoesNotAbort()
    {
        ObjectRegistry.ClearAll();
        var obj = GameObject.Create("obj");
        var probe = new BeforeProbe();
        var mi = typeof(BeforeProbe).GetMethod(nameof(BeforeProbe.AtTestFunc));
        var del = mi!.CreateDelegate(typeof(Action), probe);
        obj.InstallHook("TestFunc", del);
        Assert.True(obj.HasHook("TestFunc"));
        bool originalRan = false;
        var result = obj.Hookable("TestFunc", () => { originalRan = true; return 42; });
        Assert.True(probe.Called);
        Assert.True(originalRan);
        Assert.Equal(42, result);
        // also test before returning false still does not abort
        var obj2 = GameObject.Create("obj2");
        var probe2 = new BeforeProbe();
        var mi2 = typeof(BeforeProbe).GetMethod(nameof(BeforeProbe.AtTestFuncBool));
        var del2 = mi2!.CreateDelegate(typeof(Func<bool>), probe2);
        obj2.InstallHook("TestFunc2", del2);
        bool ran2 = false;
        var r2 = obj2.Hookable("TestFunc2", () => { ran2 = true; return 99; });
        Assert.True(probe2.Called);
        Assert.True(ran2);
        Assert.Equal(99, r2);
        ObjectRegistry.ClearAll();
    }

    // Port of AGENTS.md puppet snapshot only is_pc/privilege_level wontfix: puppet.py:110,138-142
    [Fact]
    public void Puppet_Snapshot_OnlyIsPcAndPrivilege()
    {
        ObjectRegistry.ClearAll(); IdGenerator.SetId(-1);
        var hero = GameObject.Create("hero", isPc: true);
        hero.PrivilegeLevel = Privilege.Builder;
        hero.Quelled = false;
        var npc = GameObject.Create("npc", isNpc: true);
        npc.PrivilegeLevel = Privilege.Guest;
        npc.IsPc = false;
        npc.Quelled = false;
        npc.CanHear = true;
        npc.IsMapable = true;
        var conn = new FakeConnection("puppet_test");
        var sess = conn.Session;
        hero.Session = sess;
        sess.Puppet = hero;
        ObjectRegistry.AddObject(hero); ObjectRegistry.AddObject(npc);
        bool ok = hero.Puppet(sess, npc);
        Assert.True(ok);
        Assert.True(npc.IsPc);
        Assert.Equal(Privilege.Builder, npc.PrivilegeLevel);
        // check snapshot only has 2 keys via internal method
        var mi = typeof(GameObject).GetMethod("GetPuppetRestore", BindingFlags.NonPublic | BindingFlags.Instance);
        var restore = mi!.Invoke(npc, null) as Dictionary<string, object>;
        Assert.NotNull(restore);
        Assert.Equal(2, restore!.Count);
        Assert.True(restore.ContainsKey("is_pc"));
        Assert.True(restore.ContainsKey("privilege_level"));
        Assert.False(restore.ContainsKey("quelled"));
        Assert.False(restore.ContainsKey("can_hear"));
        // mutate quelled/can_hear during puppet
        npc.Quelled = true;
        npc.CanHear = false;
        bool ok2 = hero.Unpuppet(sess);
        Assert.True(ok2);
        Assert.False(npc.IsPc); // restored
        Assert.Equal(Privilege.Guest, npc.PrivilegeLevel);
        // wontfix: quelled/can_hear not restored
        Assert.True(npc.Quelled);
        Assert.False(npc.CanHear);
        ObjectRegistry.ClearAll();
    }

    private sealed class CustomLCommand : Command
    {
        public override string Key => "l";
        public override bool UseParser => false;
        public override void Run(IMessageTarget caller, object? args) => caller.Msg("custom-l");
    }

    // Port of AGENTS.md glued alias [:1] shadowing wontfix: inputfuncs.py:91-94
    [Fact]
    public void GluedAlias_ShadowsSingleChar()
    {
        ObjectRegistry.ClearAll();
        CommandRegistry.ResetForTesting();
        var _ = CommandRegistry.LoggedIn; // init with look l
        var room = GameObject.Create("room"); room.IsContainer = true;
        var custom = new CustomLCommand();
        var ext = new CmdSet(); ext.Add(custom);
        room.ExternalCmdSet = ext;
        var hero = GameObject.Create("hero");
        hero.Desc = "A hero stands here.";
        hero.Location = new LocationRef.ObjectLocation(room.Id);
        ObjectRegistry.AddObject(room); ObjectRegistry.AddObject(hero);
        var job = CommandDispatcher.DispatchLoggedIn(hero, "l", immediate: true);
        Assert.NotNull(job);
        hero.ClearMessages();
        job!.Func(job.Caller, job.Args);
        var msgs = hero.PeekMessages();
        // look should win over external l — wontfix precedence
        Assert.DoesNotContain(msgs, m => m.Contains("custom-l"));
        Assert.Contains(msgs, m => m.ToLower().Contains("hero stands"));
        CommandRegistry.ResetForTesting(); ObjectRegistry.ClearAll();
    }

    // Port of AGENTS.md mixed Coord/tuple caller error wontfix: utils.py:362,373
    [Fact]
    public void MixedCoordTuple_IsCallerError()
    {
        var a = new List<object?> { "limbo", 0, 0, 0 };
        var b = new List<object?> { 0, 0 }; // mismatched length
        Assert.Equal("", GameUtils.GetDir(a, b)); // caller error returns ""
        var c1 = new Coord("limbo", 0, 0, 0);
        var c2 = new Coord("other", 0, 0, 0);
        Assert.Equal("", GameUtils.GetDir(c1, c2)); // different area
        // dist_3d also assumes matching types — mismatched area same as caller error handled via empty string for GetDir
        var dist = GameUtils.Dist3d(c1, new Coord("limbo", 3, 4, 0));
        Assert.Equal(5.0, dist, 3);
    }

    // Port of AGENTS.md global static salt wontfix: base_account.py:140 + salt.py:13
    [Fact]
    public void StaticSalt_IsShared()
    {
        // Verify SaltProvider uses static field shared by all accounts (wontfix) — no per-user salt
        var field = typeof(SaltProvider).GetField("_salt", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        Assert.True(field!.IsStatic);
        var salt = "sharedtestsalt";
        var h1 = Account.HashPassword("secret123", salt);
        var h2 = Account.HashPassword("secret123", salt);
        Assert.Equal(h1, h2);
        var acc1 = new Account { Name = "alice" }; acc1.SetPassword("secret123", salt);
        var acc2 = new Account { Name = "bob" }; acc2.SetPassword("secret123", salt);
        Assert.Equal(acc1.PasswordHash, acc2.PasswordHash);
        Assert.True(acc1.CheckPassword("secret123", salt));
        Assert.True(acc2.CheckPassword("secret123", salt));
        // also verify explicit global field is static — setting affects all (checked via reflection, not via global mutation to avoid parallel race)
    }
}
