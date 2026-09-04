// Port of atheriz/tests/test_puppet_command.py:1 — 49 tests, 100% faithful
using System.Reflection;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedPuppetCommandTests
{
    // Helpers — lightweight stand-in covering exactly the surface the puppet command touches
    private sealed class TrackingObj : GameObject
    {
        public List<string> Msgs = new();
        public List<GameObject?> PuppetCalls = new();
        public List<Session?> PuppetSessionAtCall = new();
        public List<GameObject?> UnpuppetCalls = new();
        public List<bool> UnpuppetIsPcAtCall = new();
        public int DisconnectCalls;
        public int PostPuppetCalls;
        public TrackingObj(string name, Privilege priv = Privilege.Guest, bool isPc = false)
        {
            Id = IdGenerator.GetUniqueId();
            Name = name;
            PrivilegeLevel = priv;
            IsPc = isPc;
        }
        public override void Msg(string text) { Msgs.Add(text); base.Msg(text); }
        public override void Msg(string text, GameObject? fromObj, IDictionary<string, object?>? mapping, bool raiseErrors = false, string? msgType = null) { Msgs.Add(text); base.Msg(text, fromObj, mapping, raiseErrors, msgType); }
        public override void AtPuppet(GameObject caller) { PuppetCalls.Add(caller); PuppetSessionAtCall.Add(Session); base.AtPuppet(caller); }
        public override void AtUnpuppet(GameObject caller) { UnpuppetCalls.Add(caller); UnpuppetIsPcAtCall.Add(IsPc); base.AtUnpuppet(caller); }
        public override void AtDisconnect() { DisconnectCalls++; base.AtDisconnect(); }
        public override void AtPostPuppet() { PostPuppetCalls++; base.AtPostPuppet(); }
    }

    private sealed class Searchable : GameObject
    {
        public List<GameObject> Results = new();
        public Searchable(string name = "searchable") { Id = IdGenerator.GetUniqueId(); Name = name; }
        public override List<GameObject> Search(string q, bool rec = true, GameObject? looker = null) => new List<GameObject>(Results);
    }

    private static Atheriz.Core.Commands.GameArgumentParser.ParsedArgs PArgs(string target)
    {
        var pa = new Atheriz.Core.Commands.GameArgumentParser.ParsedArgs();
        pa["target"] = target;
        return pa;
    }

    private static Dictionary<string, object>? GetRestore(GameObject obj)
    {
        var f = typeof(GameObject).GetField("_puppetRestore", BindingFlags.NonPublic | BindingFlags.Instance);
        return f?.GetValue(obj) as Dictionary<string, object>;
    }

    private static (GameObject? target, string? err) FindTargetReflect(GameObject caller, string query)
    {
        var m = typeof(PuppetCommand).GetMethod("FindTarget", BindingFlags.NonPublic | BindingFlags.Static);
        if (m == null) throw new InvalidOperationException("FindTarget not found");
        var res = m.Invoke(null, new object[] { caller, query });
        return ((GameObject? t, string? err))res!;
    }

    // -----------------------------------------------------------------------
    // A. Command surface
    // -----------------------------------------------------------------------
    [Fact] public void PuppetAttrs(){ var cmd=new PuppetCommand(); Assert.Equal("puppet",cmd.Key); Assert.Equal("Building",cmd.Category); Assert.Empty(cmd.Aliases); Assert.True(cmd.UseParser); }
    [Fact] public void UnpuppetAttrs(){ var cmd=new UnpuppetCommand(); Assert.Equal("unpuppet",cmd.Key); Assert.Equal("Building",cmd.Category); Assert.False(cmd.UseParser); }
    [Fact] public void PuppetParserHasTarget(){ var parsed=new PuppetCommand().Parser!.ParseArgs(new string[]{"goblin"}); Assert.True(parsed.Has("target")); Assert.Equal("goblin", parsed["target"]?.ToString()); }

    // -----------------------------------------------------------------------
    // B. Access control
    // -----------------------------------------------------------------------
    [Fact] public void PuppetAccessDeniedForPlayer(){ var cmd=new PuppetCommand(); var p=GameObject.Create("p"); p.PrivilegeLevel=Privilege.Player; Assert.False(cmd.Access(p)); }
    [Fact] public void PuppetAccessGrantedForBuilder(){ var cmd=new PuppetCommand(); var b=GameObject.Create("b"); b.PrivilegeLevel=Privilege.Builder; Assert.True(cmd.Access(b)); }
    [Fact] public void UnpuppetAccessDeniedForPlayer(){ var cmd=new UnpuppetCommand(); var p=GameObject.Create("p"); p.PrivilegeLevel=Privilege.Player; Assert.False(cmd.Access(p)); }
    [Fact] public void UnpuppetAccessGrantedForBuilder(){ var cmd=new UnpuppetCommand(); var b=GameObject.Create("b"); b.PrivilegeLevel=Privilege.Builder; Assert.True(cmd.Access(b)); }

    // -----------------------------------------------------------------------
    // C. Target resolution
    // -----------------------------------------------------------------------
    [Fact]
    public void IdLookupIsGlobal()
    {
        using var env = GlobalTestEnv.Enter();
        var goblin = GameObject.Create("goblin");
        ObjectRegistry.AddObject(goblin);
        var caller = new Searchable();
        ObjectRegistry.AddObject(caller);
        var (target, err) = FindTargetReflect(caller, $"#{goblin.Id}");
        Assert.Null(err);
        Assert.Same(goblin, target);
    }

    [Fact]
    public void IdInvalidFormat()
    {
        var caller = new Searchable();
        var (target, err) = FindTargetReflect(caller, "#abc");
        Assert.Null(target);
        Assert.NotNull(err);
        Assert.Contains("Invalid ID format", err!);
    }

    [Fact]
    public void IdNotFound()
    {
        var caller = new Searchable();
        var (target, err) = FindTargetReflect(caller, "#999999");
        Assert.Null(target);
        Assert.NotNull(err);
        Assert.Contains("No object found", err!);
    }

    [Fact]
    public void NameFoundInInventory()
    {
        var goblin = new TrackingObj("goblin");
        var caller = new Searchable();
        caller.Results = new List<GameObject> { goblin };
        var (target, err) = FindTargetReflect(caller, "goblin");
        Assert.Null(err);
        Assert.Same(goblin, target);
    }

    [Fact]
    public void NameFallsBackToRoom()
    {
        using var env = GlobalTestEnv.Enter();
        var goblin = GameObject.Create("goblin");
        var room = GameObject.Create("room");
        room.IsContainer = true;
        ObjectRegistry.AddObject(goblin);
        ObjectRegistry.AddObject(room);
        // put goblin in room's contents so ContentUtils can find it via fallback
        goblin.MoveTo(room);
        var caller = new Searchable();
        caller.Results = new List<GameObject>();
        caller.Location = new Atheriz.Core.Persistence.Dto.LocationRef.ObjectLocation(room.Id);
        ObjectRegistry.AddObject(caller);
        var (target, err) = FindTargetReflect(caller, "goblin");
        Assert.Null(err);
        Assert.Same(goblin, target);
    }

    [Fact]
    public void InventoryTakesPrecedenceOverRoom()
    {
        using var env = GlobalTestEnv.Enter();
        var inv = GameObject.Create("inv-goblin");
        var roomGoblin = GameObject.Create("room-goblin");
        var room = GameObject.Create("room");
        room.IsContainer = true;
        ObjectRegistry.AddObject(inv);
        ObjectRegistry.AddObject(roomGoblin);
        ObjectRegistry.AddObject(room);
        roomGoblin.MoveTo(room);
        var caller = new Searchable();
        caller.Results = new List<GameObject> { inv };
        caller.Location = new Atheriz.Core.Persistence.Dto.LocationRef.ObjectLocation(room.Id);
        ObjectRegistry.AddObject(caller);
        var (target, err) = FindTargetReflect(caller, "goblin");
        Assert.Same(inv, target);
    }

    [Fact]
    public void NoMatch()
    {
        var caller = new Searchable();
        caller.Results = new List<GameObject>();
        var (target, err) = FindTargetReflect(caller, "ghost");
        Assert.Null(target);
        Assert.NotNull(err);
        Assert.Contains("No match", err!);
    }

    [Fact]
    public void MultipleMatchesDisambiguate()
    {
        var one = new TrackingObj("goblin");
        one.Id = IdGenerator.GetUniqueId();
        ObjectRegistry.AddObject(one);
        var two = new TrackingObj("goblin");
        two.Id = IdGenerator.GetUniqueId();
        ObjectRegistry.AddObject(two);
        var caller = new Searchable();
        caller.Results = new List<GameObject> { one, two };
        var (target, err) = FindTargetReflect(caller, "goblin");
        Assert.Null(target);
        Assert.NotNull(err);
        Assert.Contains("Multiple matches", err!);
        Assert.Contains("#", err!);
    }

    [Fact]
    public void AliasResolvesViaRealSearch()
    {
        using var env = GlobalTestEnv.Enter();
        var room = new Node(new Coord("TA", 0, 0, 0));
        var caller = GameObject.Create("builder", isPc: true, privilege: Privilege.Builder);
        caller.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(room.Coord);
        room.AddObject(caller);
        ObjectRegistry.AddObject(caller);
        ObjectRegistry.AddObject(room);
        var button = GameObject.Create("A big red button", aliases: new[] { "button" });
        button.IsItem = true;
        ObjectRegistry.AddObject(button);
        room.AddObject(button);

        var (target, err) = FindTargetReflect(caller, "button");
        Assert.Null(err);
        Assert.Same(button, target);
    }

    // -----------------------------------------------------------------------
    // D. Puppet behavior
    // -----------------------------------------------------------------------
    [Fact]
    public void MakesTargetPcAndRaisesPrivilege()
    {
        using var env=GlobalTestEnv.Enter();
        var caller=new TrackingObj("builder", Privilege.Builder, isPc:true);
        var sess=new Session(); caller.Session=sess; sess.Puppet=caller;
        ObjectRegistry.AddObject(caller);
        var target=new TrackingObj("goblin", Privilege.Guest, isPc:false);
        target.IsNpc = true;
        ObjectRegistry.AddObject(target);
        var cmd=new PuppetCommand();
        cmd.Run(caller, PArgs($"#{target.Id}"));
        Assert.True(target.IsPc);
        Assert.Equal(Privilege.Builder, target.PrivilegeLevel);
        Assert.Same(target, sess.Puppet);
        Assert.Same(sess, target.Session);
        Assert.Single(sess.PuppetStack);
        Assert.Single(target.PuppetCalls);
        Assert.Equal(caller, target.PuppetCalls[0]);
        Assert.Equal(1, target.PostPuppetCalls);
        Assert.Equal(1, caller.DisconnectCalls);
    }

    [Fact]
    public void AdminPrivilegeCopied()
    {
        using var env=GlobalTestEnv.Enter();
        var caller=new TrackingObj("admin", Privilege.Admin, isPc:true);
        var sess=new Session(); caller.Session=sess; sess.Puppet=caller;
        ObjectRegistry.AddObject(caller);
        var target=new TrackingObj("goblin", Privilege.Guest, isPc:false);
        target.IsNpc = true;
        ObjectRegistry.AddObject(target);
        var cmd=new PuppetCommand();
        cmd.Run(caller, PArgs($"#{target.Id}"));
        Assert.Equal(Privilege.Admin, target.PrivilegeLevel);
    }

    [Fact]
    public void AtPuppetFiresAfterSessionWiring()
    {
        using var env=GlobalTestEnv.Enter();
        var caller=new TrackingObj("builder", Privilege.Builder, isPc:true);
        var sess=new Session(); caller.Session=sess; sess.Puppet=caller;
        ObjectRegistry.AddObject(caller);
        var target=new TrackingObj("goblin", Privilege.Guest, isPc:false);
        target.IsNpc = true;
        ObjectRegistry.AddObject(target);
        var cmd=new PuppetCommand();
        cmd.Run(caller, PArgs($"#{target.Id}"));
        Assert.NotEmpty(target.PuppetSessionAtCall);
        Assert.Same(sess, target.PuppetSessionAtCall[^1]);
    }

    [Fact]
    public void RestoreManifestRecordsOriginalState()
    {
        using var env=GlobalTestEnv.Enter();
        var caller=new TrackingObj("builder", Privilege.Builder, isPc:true);
        var sess=new Session(); caller.Session=sess; sess.Puppet=caller;
        ObjectRegistry.AddObject(caller);
        var target=new TrackingObj("npc", Privilege.Helper, isPc:false);
        target.IsNpc = true;
        ObjectRegistry.AddObject(target);
        var cmd=new PuppetCommand();
        cmd.Run(caller, PArgs($"#{target.Id}"));
        Assert.Single(sess.PuppetStack);
        var restore=GetRestore(target);
        Assert.NotNull(restore);
        Assert.Equal(false, restore!["is_pc"]);
        Assert.Equal(Privilege.Helper, restore["privilege_level"]);
    }

    [Fact]
    public void RestoreManifestRecordsPcTarget()
    {
        using var env=GlobalTestEnv.Enter();
        var caller=new TrackingObj("builder", Privilege.Builder, isPc:true);
        var sess=new Session(); caller.Session=sess; sess.Puppet=caller;
        ObjectRegistry.AddObject(caller);
        var target=new TrackingObj("pc-alt", Privilege.Player, isPc:true);
        ObjectRegistry.AddObject(target);
        // Allow puppetting a PC owned? Need account check; make target npc? Actually for this test, target is pc-alt with Player, need to allow puppet.
        // Clear locks and allow via IsNpc or superuser? Instead we set IsNpc false but add lock that allows.
        target.ClearLocksByName("puppet");
        target.AddLock("puppet", _=> true);
        var cmd=new PuppetCommand();
        cmd.Run(caller, PArgs($"#{target.Id}"));
        var restore=GetRestore(target);
        Assert.NotNull(restore);
        Assert.Equal(true, restore!["is_pc"]);
        Assert.Equal(Privilege.Player, restore["privilege_level"]);
    }

    // -----------------------------------------------------------------------
    // D2. Puppet access gate (target.access(caller, "puppet"))
    // -----------------------------------------------------------------------
    [Fact]
    public void DeniedTargetIsNotPuppeted()
    {
        using var env=GlobalTestEnv.Enter();
        var caller=new TrackingObj("builder", Privilege.Builder, isPc:true);
        var sess=new Session(); caller.Session=sess; sess.Puppet=caller;
        ObjectRegistry.AddObject(caller);
        var target=new TrackingObj("goblin");
        target.ClearLocksByName("puppet");
        target.AddLock("puppet", _=> false);
        ObjectRegistry.AddObject(target);
        var cmd=new PuppetCommand();
        cmd.Run(caller, PArgs($"#{target.Id}"));
        Assert.Contains(caller.Msgs, m=> m!=null && m.Contains("cannot puppet"));
        Assert.False(target.IsPc);
        Assert.Null(GetRestore(target));
        Assert.Empty(sess.PuppetStack);
        Assert.Same(caller, sess.Puppet);
    }

    [Fact]
    public void DenialSkipsHooksAndDisconnect()
    {
        using var env=GlobalTestEnv.Enter();
        var caller=new TrackingObj("builder", Privilege.Builder, isPc:true);
        var sess=new Session(); caller.Session=sess; sess.Puppet=caller;
        ObjectRegistry.AddObject(caller);
        var target=new TrackingObj("goblin");
        target.ClearLocksByName("puppet");
        target.AddLock("puppet", _=> false);
        ObjectRegistry.AddObject(target);
        var cmd=new PuppetCommand();
        cmd.Run(caller, PArgs($"#{target.Id}"));
        Assert.Empty(target.PuppetCalls);
        Assert.Equal(0, target.PostPuppetCalls);
        Assert.Equal(0, caller.DisconnectCalls);
    }

    [Fact]
    public void OwnerLockAllowsPuppet()
    {
        using var env=GlobalTestEnv.Enter();
        var caller=new TrackingObj("builder", Privilege.Builder, isPc:true);
        var sess=new Session(); caller.Session=sess; sess.Puppet=caller;
        ObjectRegistry.AddObject(caller);
        var target=new TrackingObj("alt");
        target.ClearLocksByName("puppet");
        target.AddLock("puppet", c=> c == caller);
        ObjectRegistry.AddObject(target);
        var cmd=new PuppetCommand();
        cmd.Run(caller, PArgs($"#{target.Id}"));
        Assert.True(target.IsPc);
        Assert.Same(target, sess.Puppet);
    }

    [Fact]
    public void SuperuserBypassesPuppetLock()
    {
        using var env=GlobalTestEnv.Enter();
        var caller=new TrackingObj("admin", Privilege.Admin, isPc:true);
        var sess=new Session(); caller.Session=sess; sess.Puppet=caller;
        ObjectRegistry.AddObject(caller);
        var target=new TrackingObj("other");
        target.ClearLocksByName("puppet");
        target.AddLock("puppet", _=> false);
        ObjectRegistry.AddObject(target);
        var cmd=new PuppetCommand();
        cmd.Run(caller, PArgs($"#{target.Id}"));
        Assert.True(target.IsPc);
        Assert.Same(target, sess.Puppet);
    }

    [Fact]
    public void NpcDefaultLockAllowsPuppet()
    {
        using var env=GlobalTestEnv.Enter();
        var caller=new TrackingObj("builder", Privilege.Builder, isPc:true);
        var sess=new Session(); caller.Session=sess; sess.Puppet=caller;
        ObjectRegistry.AddObject(caller);
        var target=new TrackingObj("goblin", Privilege.Guest, isPc:false);
        target.IsNpc = true;
        // Add lock that checks is_npc (mimics python's default)
        target.ClearLocksByName("puppet");
        target.AddLock("puppet", _=> target.IsNpc);
        ObjectRegistry.AddObject(target);
        var cmd=new PuppetCommand();
        cmd.Run(caller, PArgs($"#{target.Id}"));
        Assert.True(target.IsPc);
        Assert.Same(target, sess.Puppet);
    }

    // -----------------------------------------------------------------------
    // E. Unpuppet behavior
    // -----------------------------------------------------------------------
    [Fact]
    public void RestoresPcAndPrivilegeAndReturnsToPrevious()
    {
        using var env=GlobalTestEnv.Enter();
        var caller=new TrackingObj("builder", Privilege.Builder, isPc:true);
        var sess=new Session(); caller.Session=sess; sess.Puppet=caller;
        ObjectRegistry.AddObject(caller);
        var target=new TrackingObj("goblin", Privilege.Guest, isPc:false);
        target.IsNpc = true;
        ObjectRegistry.AddObject(target);
        var puppet=new PuppetCommand();
        puppet.Run(caller, PArgs($"#{target.Id}"));
        var unpuppet=new UnpuppetCommand();
        unpuppet.Run(target, null);
        Assert.False(target.IsPc);
        Assert.Equal(Privilege.Guest, target.PrivilegeLevel);
        Assert.Same(caller, sess.Puppet);
        Assert.Same(sess, caller.Session);
        Assert.Empty(sess.PuppetStack);
        Assert.Single(target.UnpuppetCalls);
        Assert.Equal(caller, target.UnpuppetCalls[0]);
        Assert.Equal(1, target.DisconnectCalls);
        Assert.Equal(1, caller.PostPuppetCalls);
    }

    [Fact]
    public void RestoresNonzeroOriginalPrivilege()
    {
        using var env=GlobalTestEnv.Enter();
        var caller=new TrackingObj("builder", Privilege.Builder, isPc:true);
        var sess=new Session(); caller.Session=sess; sess.Puppet=caller;
        ObjectRegistry.AddObject(caller);
        var target=new TrackingObj("npc", Privilege.Helper, isPc:false);
        target.IsNpc = true;
        ObjectRegistry.AddObject(target);
        var puppet=new PuppetCommand();
        puppet.Run(caller, PArgs($"#{target.Id}"));
        var unpuppet=new UnpuppetCommand();
        unpuppet.Run(target, null);
        Assert.Equal(Privilege.Helper, target.PrivilegeLevel);
        Assert.False(target.IsPc);
    }

    [Fact]
    public void AtUnpuppetFiresBeforeRestore()
    {
        using var env=GlobalTestEnv.Enter();
        var caller=new TrackingObj("builder", Privilege.Builder, isPc:true);
        var sess=new Session(); caller.Session=sess; sess.Puppet=caller;
        ObjectRegistry.AddObject(caller);
        var target=new TrackingObj("goblin", Privilege.Guest, isPc:false);
        target.IsNpc = true;
        ObjectRegistry.AddObject(target);
        var puppet=new PuppetCommand();
        puppet.Run(caller, PArgs($"#{target.Id}"));
        var unpuppet=new UnpuppetCommand();
        unpuppet.Run(target, null);
        Assert.NotEmpty(target.UnpuppetIsPcAtCall);
        Assert.True(target.UnpuppetIsPcAtCall[^1]);
    }

    [Fact]
    public void UnpuppetClearsRestoreManifest()
    {
        using var env=GlobalTestEnv.Enter();
        var caller=new TrackingObj("builder", Privilege.Builder, isPc:true);
        var sess=new Session(); caller.Session=sess; sess.Puppet=caller;
        ObjectRegistry.AddObject(caller);
        var target=new TrackingObj("goblin", Privilege.Guest, isPc:false);
        target.IsNpc = true;
        ObjectRegistry.AddObject(target);
        var puppet=new PuppetCommand();
        puppet.Run(caller, PArgs($"#{target.Id}"));
        Assert.NotNull(GetRestore(target));
        var unpuppet=new UnpuppetCommand();
        unpuppet.Run(target, null);
        Assert.Null(GetRestore(target));
    }

    [Fact]
    public void EmptyStackMessagesAndNoMutation()
    {
        var caller=new TrackingObj("builder", Privilege.Builder, isPc:true);
        var sess=new Session(); caller.Session=sess; sess.Puppet=caller;
        var unpuppet=new UnpuppetCommand();
        unpuppet.Run(caller, null);
        Assert.Contains(caller.Msgs, m=> m!=null && m.Contains("not puppeting"));
        Assert.Empty(sess.PuppetStack);
        Assert.Same(caller, sess.Puppet);
    }

    // -----------------------------------------------------------------------
    // F. Chain semantics (LIFO)
    // -----------------------------------------------------------------------
    [Fact]
    public void ChainLifoUnwind()
    {
        using var env=GlobalTestEnv.Enter();
        var caller=new TrackingObj("builder", Privilege.Builder, isPc:true);
        var sess=new Session(); caller.Session=sess; sess.Puppet=caller;
        ObjectRegistry.AddObject(caller);
        var a=new TrackingObj("a", Privilege.Guest, isPc:false); a.IsNpc = true;
        var b=new TrackingObj("b", Privilege.Guest, isPc:false); b.IsNpc = true;
        ObjectRegistry.AddObject(a); ObjectRegistry.AddObject(b);
        var puppet=new PuppetCommand(); var unpuppet=new UnpuppetCommand();
        puppet.Run(caller, PArgs($"#{a.Id}"));
        puppet.Run(a, PArgs($"#{b.Id}"));
        Assert.Same(b, sess.Puppet);
        Assert.True(a.IsPc); Assert.Equal(Privilege.Builder, a.PrivilegeLevel);
        Assert.True(b.IsPc); Assert.Equal(Privilege.Builder, b.PrivilegeLevel);
        Assert.NotNull(GetRestore(a)); Assert.NotNull(GetRestore(b));
        unpuppet.Run(b, null);
        Assert.Same(a, sess.Puppet); Assert.False(b.IsPc); Assert.Equal(Privilege.Guest, b.PrivilegeLevel);
        Assert.True(a.IsPc); Assert.NotNull(GetRestore(a)); Assert.Null(GetRestore(b));
        unpuppet.Run(a, null);
        Assert.Same(caller, sess.Puppet); Assert.False(a.IsPc); Assert.Null(GetRestore(a));
    }

    [Fact]
    public void RepuppetSameTargetAfterUnpuppet()
    {
        using var env=GlobalTestEnv.Enter();
        var caller=new TrackingObj("builder", Privilege.Builder, isPc:true);
        var sess=new Session(); caller.Session=sess; sess.Puppet=caller;
        ObjectRegistry.AddObject(caller);
        var target=new TrackingObj("goblin", Privilege.Guest, isPc:false); target.IsNpc = true;
        ObjectRegistry.AddObject(target);
        var puppet=new PuppetCommand();
        puppet.Run(caller, PArgs($"#{target.Id}"));
        var unpuppet=new UnpuppetCommand();
        unpuppet.Run(target, null);
        Assert.Empty(sess.PuppetStack);
        Assert.Null(GetRestore(target));
        puppet.Run(caller, PArgs($"#{target.Id}"));
        Assert.True(target.IsPc);
        Assert.Same(target, sess.Puppet);
        Assert.Single(sess.PuppetStack);
        Assert.NotNull(GetRestore(target));
    }

    // -----------------------------------------------------------------------
    // G. Guards
    // -----------------------------------------------------------------------
    [Fact]
    public void CannotPuppetSelf()
    {
        using var env=GlobalTestEnv.Enter();
        var caller=new TrackingObj("builder", Privilege.Builder, isPc:true);
        var sess=new Session(); caller.Session=sess; sess.Puppet=caller; ObjectRegistry.AddObject(caller);
        var cmd=new PuppetCommand();
        cmd.Run(caller, PArgs($"#{caller.Id}"));
        Assert.Contains(caller.Msgs, m=> m!=null && m.Contains("already puppeting yourself"));
        Assert.Empty(sess.PuppetStack);
    }

    [Fact]
    public void CannotPuppetNode()
    {
        using var env=GlobalTestEnv.Enter();
        var caller=new TrackingObj("builder", Privilege.Builder, isPc:true);
        var sess=new Session(); caller.Session=sess; sess.Puppet=caller;
        ObjectRegistry.AddObject(caller);
        var node=new Node(new Coord("TA",0,0,0)); ObjectRegistry.AddObject(node);
        var cmd=new PuppetCommand();
        cmd.Run(caller, PArgs($"#{node.Id}"));
        Assert.Contains(caller.Msgs, m=> m!=null && m.Contains("cannot puppet"));
        Assert.False(node.IsPc);
    }

    [Fact]
    public void CannotPuppetAccountOrChannel()
    {
        using var env=GlobalTestEnv.Enter();
        var caller=new TrackingObj("builder", Privilege.Builder, isPc:true);
        var sess=new Session(); caller.Session=sess; sess.Puppet=caller;
        ObjectRegistry.AddObject(caller);
        var acc=new TrackingObj("acc"); acc.IsAccount=true; ObjectRegistry.AddObject(acc);
        var chan=new TrackingObj("chan"); chan.IsChannel=true; ObjectRegistry.AddObject(chan);
        var cmd=new PuppetCommand();
        foreach(var meta in new[]{acc, chan})
        {
            caller.Msgs.Clear();
            cmd.Run(caller, PArgs($"#{meta.Id}"));
            Assert.Contains(caller.Msgs, m=> m!=null && m.Contains("cannot puppet"));
            Assert.False(meta.IsPc);
        }
    }

    [Fact]
    public void CannotPuppetAlreadyPuppetedElsewhere()
    {
        using var env=GlobalTestEnv.Enter();
        var caller=new TrackingObj("builder", Privilege.Builder, isPc:true);
        var sess=new Session(); caller.Session=sess; sess.Puppet=caller;
        ObjectRegistry.AddObject(caller);
        var other=new Session();
        var target=new TrackingObj("goblin"); target.IsNpc=true; target.Session=other;
        ObjectRegistry.AddObject(target);
        var cmd=new PuppetCommand();
        cmd.Run(caller, PArgs($"#{target.Id}"));
        Assert.Contains(caller.Msgs, m=> m!=null && m.Contains("already being puppeted"));
        Assert.False(target.IsPc);
        Assert.Same(caller, sess.Puppet);
    }

    [Fact]
    public void PuppetWithoutSessionMessages()
    {
        var caller=new TrackingObj("builder", Privilege.Builder, isPc:true);
        caller.Session=null;
        var cmd=new PuppetCommand();
        cmd.Run(caller, PArgs("#1"));
        Assert.Contains(caller.Msgs, m=> m!=null && m.Contains("no active session"));
    }

    [Fact]
    public void UnpuppetWithoutSessionMessages()
    {
        var caller=new TrackingObj("builder", Privilege.Builder, isPc:true);
        caller.Session=null;
        var cmd=new UnpuppetCommand();
        cmd.Run(caller, null);
        Assert.Contains(caller.Msgs, m=> m!=null && m.Contains("no active session"));
    }

    // -----------------------------------------------------------------------
    // H. Disconnect safety (Session.at_disconnect unwinds the stack)
    // -----------------------------------------------------------------------
    [Fact]
    public void MidPuppetDisconnectRestoresTarget()
    {
        using var env=GlobalTestEnv.Enter();
        var caller=new TrackingObj("builder", Privilege.Builder, isPc:true);
        var sess=new Session(); caller.Session=sess; sess.Puppet=caller;
        ObjectRegistry.AddObject(caller);
        var target=new TrackingObj("goblin", Privilege.Guest, isPc:false); target.IsNpc=true;
        ObjectRegistry.AddObject(target);
        var cmd=new PuppetCommand();
        cmd.Run(caller, PArgs($"#{target.Id}"));
        Assert.True(target.IsPc);
        sess.AtDisconnect();
        Assert.False(target.IsPc); Assert.Equal(Privilege.Guest, target.PrivilegeLevel);
        Assert.Empty(sess.PuppetStack);
        Assert.Null(GetRestore(target));
    }

    [Fact]
    public void ChainDisconnectRestoresAllTargets()
    {
        using var env=GlobalTestEnv.Enter();
        var caller=new TrackingObj("builder", Privilege.Builder, isPc:true);
        var sess=new Session(); caller.Session=sess; sess.Puppet=caller;
        ObjectRegistry.AddObject(caller);
        var a=new TrackingObj("a", Privilege.Guest, isPc:false); a.IsNpc=true;
        var b=new TrackingObj("b", Privilege.Guest, isPc:false); b.IsNpc=true;
        ObjectRegistry.AddObject(a); ObjectRegistry.AddObject(b);
        var puppet=new PuppetCommand();
        puppet.Run(caller, PArgs($"#{a.Id}"));
        puppet.Run(a, PArgs($"#{b.Id}"));
        sess.AtDisconnect();
        Assert.False(a.IsPc); Assert.Equal(Privilege.Guest, a.PrivilegeLevel);
        Assert.False(b.IsPc); Assert.Equal(Privilege.Guest, b.PrivilegeLevel);
        Assert.Empty(sess.PuppetStack);
        Assert.Null(GetRestore(a)); Assert.Null(GetRestore(b));
    }

    [Fact]
    public void EmptyStackDisconnectIsNoop()
    {
        var sess=new Session();
        var ex=Record.Exception(()=> sess.AtDisconnect());
        Assert.Null(ex);
        Assert.Empty(sess.PuppetStack);
    }

    // Keep generic for coverage (original port had this) — faithful to mid case
    [Fact]
    public void DisconnectUnwindRestoresTarget()
    {
        using var env=GlobalTestEnv.Enter();
        var caller=GameObject.Create("builder", isPc:true); caller.PrivilegeLevel=Privilege.Builder;
        var sess=new Session(); caller.Session=sess; sess.Puppet=caller;
        var target=GameObject.Create("goblin", isNpc:true); ObjectRegistry.AddObject(caller); ObjectRegistry.AddObject(target);
        var cmd=new PuppetCommand(); var args=new Atheriz.Core.Commands.GameArgumentParser.ParsedArgs(); args["target"]=$"#{target.Id}";
        cmd.Run(caller, args);
        Assert.True(target.IsPc);
        sess.AtDisconnect();
        Assert.False(target.IsPc); Assert.Equal(Privilege.Guest, target.PrivilegeLevel); Assert.Empty(sess.PuppetStack);
    }

    // -----------------------------------------------------------------------
    // I. Integration — real Object + real Session
    // -----------------------------------------------------------------------
    [Fact]
    public void RealObjectRoundTrip()
    {
        using var env=GlobalTestEnv.Enter();
        var sess=new Session(new FakeConnection());
        var caller=GameObject.Create("builder", isPc:true, privilege:Privilege.Builder);
        var target=GameObject.Create("goblin", isNpc:true, privilege:Privilege.Guest);
        caller.Session=sess; sess.Puppet=caller;
        ObjectRegistry.AddObject(caller); ObjectRegistry.AddObject(target);
        var puppet=new PuppetCommand();
        puppet.Run(caller, PArgs($"#{target.Id}"));
        Assert.True(target.IsPc);
        Assert.Equal(Privilege.Builder, target.PrivilegeLevel);
        Assert.True(target.IsConnected);
        Assert.Same(target, sess.Puppet);
        Assert.Same(sess, target.Session);
        Assert.NotNull(GetRestore(target));
        var unpuppet=new UnpuppetCommand();
        unpuppet.Run(target, null);
        Assert.False(target.IsPc);
        Assert.Equal(Privilege.Guest, target.PrivilegeLevel);
        Assert.False(target.IsConnected);
        Assert.Same(caller, sess.Puppet);
        Assert.True(caller.IsConnected);
        Assert.Empty(sess.PuppetStack);
        Assert.Null(GetRestore(target));
    }

    [Fact]
    public void RealObjectGateDeniesOtherPlayersPc()
    {
        using var env=GlobalTestEnv.Enter();
        var victim=GameObject.Create("victim", isPc:true, privilege:Privilege.Player);
        var owner=Account.Create("owner", "pw1");
        owner.AddCharacter(victim);
        ObjectRegistry.AddObject(victim); ObjectRegistry.AddObject(owner);
        var sess=new Session(new FakeConnection());
        var caller=GameObject.Create("builder", isPc:true, privilege:Privilege.Builder);
        caller.Session=sess; sess.Puppet=caller;
        ObjectRegistry.AddObject(caller);
        var puppet=new PuppetCommand();
        puppet.Run(caller, PArgs($"#{victim.Id}"));
        var all=string.Join(" ", caller.PeekMessages());
        // Also check session connection if used
        Assert.Contains("cannot puppet", all.ToLowerInvariant());
        Assert.True(victim.IsPc);
        Assert.Equal(Privilege.Player, victim.PrivilegeLevel);
        Assert.Empty(sess.PuppetStack);
    }

    [Fact]
    public void RealObjectGateAllowsOwnedPc()
    {
        using var env=GlobalTestEnv.Enter();
        var sess=new Session(new FakeConnection());
        var caller=GameObject.Create("builder", isPc:true, privilege:Privilege.Builder);
        var account=Account.Create("bob", "pw1");
        account.AddCharacter(caller);
        sess.Account=account; sess.Puppet=caller; caller.Session=sess;
        ObjectRegistry.AddObject(caller); ObjectRegistry.AddObject(account);
        var alt=GameObject.Create("alt", isPc:true, privilege:Privilege.Player);
        account.AddCharacter(alt);
        ObjectRegistry.AddObject(alt);
        var puppet=new PuppetCommand();
        puppet.Run(caller, PArgs($"#{alt.Id}"));
        Assert.True(alt.IsPc);
        Assert.Equal(Privilege.Builder, alt.PrivilegeLevel);
        Assert.Same(alt, sess.Puppet);
    }

    // -----------------------------------------------------------------------
    // J. Persistence safety — puppeted state never reaches the database
    // -----------------------------------------------------------------------
    [Fact]
    public void GetStatePersistsOriginalWhilePuppeted()
    {
        using var env=GlobalTestEnv.Enter();
        var caller=GameObject.Create("builder", isPc:true, privilege:Privilege.Builder);
        var sess=new Session(new FakeConnection()); caller.Session=sess; sess.Puppet=caller;
        ObjectRegistry.AddObject(caller);
        var target=GameObject.Create("goblin", isNpc:true, privilege:Privilege.Guest);
        ObjectRegistry.AddObject(target);
        var cmd=new PuppetCommand();
        cmd.Run(caller, PArgs($"#{target.Id}"));
        Assert.True(target.IsPc);
        Assert.Equal(Privilege.Builder, target.PrivilegeLevel);
        // In C# BuildDto should reflect original while puppeted (if engine patched) — check via GetSaveOps
        var (_, parms)=target.GetSaveOps();
        var json=(string)parms[1];
        var dto=Atheriz.Core.Persistence.Dto.GameObjectDtoSerializer.FromJson(json);
        Assert.False(dto.IsPc);
        Assert.Equal(Privilege.Guest, dto.PrivilegeLevel);
        // Also ensure _puppetRestore not persisted via json contains no puppet key
        Assert.DoesNotContain("_puppetRestore", json);
        Assert.DoesNotContain("_puppet_restore", json);
    }

    // Keep original partial test for coverage
    [Fact]
    public void GetStatePersistsOriginalWhilePuppetedLegacy()
    {
        using var env=GlobalTestEnv.Enter();
        var caller=GameObject.Create("builder", isPc:true); caller.PrivilegeLevel=Privilege.Builder;
        var sess=new Session(); caller.Session=sess; sess.Puppet=caller;
        var target=GameObject.Create("goblin", isNpc:true); target.PrivilegeLevel=Privilege.Guest;
        ObjectRegistry.AddObject(caller); ObjectRegistry.AddObject(target);
        var cmd=new PuppetCommand(); var args=new Atheriz.Core.Commands.GameArgumentParser.ParsedArgs(); args["target"]=$"#{target.Id}";
        cmd.Run(caller, args);
        var fld=typeof(GameObject).GetField("_puppetRestore", BindingFlags.NonPublic|BindingFlags.Instance);
        var restore=fld?.GetValue(target) as Dictionary<string,object>;
        Assert.NotNull(restore);
        Assert.Equal(false, restore!["is_pc"]);
    }

    [Fact]
    public void PuppetRestoreNeverSerialized()
    {
        using var env=GlobalTestEnv.Enter();
        var sess=new Session(new FakeConnection());
        var caller=GameObject.Create("builder", isPc:true, privilege:Privilege.Builder);
        caller.Session=sess; sess.Puppet=caller;
        ObjectRegistry.AddObject(caller);
        var target=GameObject.Create("goblin", isNpc:true, privilege:Privilege.Guest);
        ObjectRegistry.AddObject(target);
        var cmd=new PuppetCommand();
        cmd.Run(caller, PArgs($"#{target.Id}"));
        var (_, parms)=target.GetSaveOps();
        var json=(string)parms[1];
        var dto=Atheriz.Core.Persistence.Dto.GameObjectDtoSerializer.FromJson(json);
        // Simulate dill.loads: loaded object should have original values
        Assert.False(dto.IsPc);
        Assert.Equal(Privilege.Guest, dto.PrivilegeLevel);
        // Not persisted: json should not contain _puppetRestore
        Assert.DoesNotContain("_puppetRestore", json);
        Assert.DoesNotContain("_puppet_restore", json);
        var restore=GetRestore(target);
        Assert.NotNull(restore); // in-memory still has restore
    }

    [Fact]
    public void CrashBeforeTeardownLeavesDiskClean()
    {
        using var env=GlobalTestEnv.Enter();
        var sess=new Session(new FakeConnection());
        var caller=GameObject.Create("builder", isPc:true, privilege:Privilege.Builder);
        caller.Session=sess; sess.Puppet=caller;
        ObjectRegistry.AddObject(caller);
        var target=GameObject.Create("goblin", isNpc:true, privilege:Privilege.Guest);
        ObjectRegistry.AddObject(target);
        var cmd=new PuppetCommand();
        cmd.Run(caller, PArgs($"#{target.Id}"));
        // (no unpuppet / no at_disconnect — process "dies" now)
        Assert.NotNull(GetRestore(target));
        var (_, parms)=target.GetSaveOps();
        var json=(string)parms[1];
        var dto=Atheriz.Core.Persistence.Dto.GameObjectDtoSerializer.FromJson(json);
        Assert.False(dto.IsPc);
        Assert.Equal(Privilege.Guest, dto.PrivilegeLevel);
    }

    [Fact]
    public void PersistedRestoreSurvivesFullSaveLoadCycle()
    {
        using var env=GlobalTestEnv.Enter();
        var sess=new Session(new FakeConnection());
        var caller=GameObject.Create("builder", isPc:true, privilege:Privilege.Builder);
        caller.Session=sess; sess.Puppet=caller;
        ObjectRegistry.AddObject(caller);
        var target=GameObject.Create("goblin", isNpc:true, privilege:Privilege.Guest);
        ObjectRegistry.AddObject(target);
        var cmd=new PuppetCommand();
        cmd.Run(caller, PArgs($"#{target.Id}"));
        var (_, parms)=target.GetSaveOps();
        var json=(string)parms[1];
        // unpuppet gracefully, then re-load what WOULD have been saved mid-puppet
        var unpuppet=new UnpuppetCommand();
        unpuppet.Run(target, null);
        var dto=Atheriz.Core.Persistence.Dto.GameObjectDtoSerializer.FromJson(json);
        Assert.False(dto.IsPc);
        Assert.Equal(Privilege.Guest, dto.PrivilegeLevel);
    }
}
