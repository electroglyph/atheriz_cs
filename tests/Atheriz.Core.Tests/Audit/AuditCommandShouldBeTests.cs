using System.Reflection;
using System.Text.Json;
using Atheriz.Core;
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Audit;

// Should-be tests for audit §2 B2/B4-B10 (+B6 door settings, B1-adjacent parser
// coverage lives in AuditContainmentMoveShouldBeTests). Fails while the bug is
// present, passes once fixed. Production code is untouched.
[Collection("Ported")]
public class AuditCommandShouldBeTests
{
    private static void RunJob(CommandDispatcher.Job? job)
    {
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);
    }

    // --- B2: set/unset protected-attribute bypass ---

    [Fact]
    public void Set_CaseVariantOfProtectedFlag_IsRejectedForNonSuperuser()
    {
        // audit B2: Protected is Ordinal and FindProp is IgnoreCase, so any
        // non-lowercase spelling (Is_Pc, IS_PC, ...) bypasses the guard and
        // sets a protected flag as a mere builder.
        ObjectRegistry.ClearAll();
        try
        {
            var builder = GameObject.Create("builder", privilege: Privilege.Builder);
            ObjectRegistry.AddObject(builder);
            var job = CommandDispatcher.DispatchLoggedIn(builder, "set me Is_Pc True", immediate: true);
            RunJob(job);
            Assert.Contains("is protected", string.Join("\n", builder.PeekMessages()));
            Assert.False(builder.IsPc);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Set_QuelledCaseVariant_IsRejectedForNonSuperuser()
    {
        // audit B2: same bypass for the quelled flag (which gates IsBuilder /
        // IsSuperUser themselves).
        ObjectRegistry.ClearAll();
        try
        {
            var builder = GameObject.Create("builder", privilege: Privilege.Builder);
            ObjectRegistry.AddObject(builder);
            var job = CommandDispatcher.DispatchLoggedIn(builder, "set me Quelled True", immediate: true);
            RunJob(job);
            Assert.Contains("is protected", string.Join("\n", builder.PeekMessages()));
            Assert.False(builder.Quelled);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Set_LowercaseProtectedFlag_IsRejectedForNonSuperuser()
    {
        // Pin: the exact-lowercase spelling is already guarded.
        ObjectRegistry.ClearAll();
        try
        {
            var builder = GameObject.Create("builder", privilege: Privilege.Builder);
            ObjectRegistry.AddObject(builder);
            var job = CommandDispatcher.DispatchLoggedIn(builder, "set me is_pc True", immediate: true);
            RunJob(job);
            Assert.Contains("is protected", string.Join("\n", builder.PeekMessages()));
            Assert.False(builder.IsPc);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Set_LocationGuard_IsCaseInsensitive()
    {
        // audit B2: the location guard list uses Ordinal Contains, so "Location"
        // falls through to a different ("read-only") message instead of the
        // move/teleport redirect.
        ObjectRegistry.ClearAll();
        try
        {
            var admin = GameObject.Create("admin", privilege: Privilege.Admin);
            ObjectRegistry.AddObject(admin);
            var j1 = CommandDispatcher.DispatchLoggedIn(admin, "set me location null", immediate: true);
            RunJob(j1);
            admin.ClearMessages();
            var j2 = CommandDispatcher.DispatchLoggedIn(admin, "set me Location null", immediate: true);
            RunJob(j2);
            Assert.Contains("use move/teleport instead", string.Join("\n", admin.PeekMessages()));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Unset_LocationGuard_IsCaseInsensitive()
    {
        // audit B2: same Ordinal mismatch on the unset path.
        ObjectRegistry.ClearAll();
        try
        {
            var admin = GameObject.Create("admin", privilege: Privilege.Admin);
            ObjectRegistry.AddObject(admin);
            var j1 = CommandDispatcher.DispatchLoggedIn(admin, "unset me location", immediate: true);
            RunJob(j1);
            Assert.Contains("cannot be removed directly", string.Join("\n", admin.PeekMessages()));
            admin.ClearMessages();
            var j2 = CommandDispatcher.DispatchLoggedIn(admin, "unset me Location", immediate: true);
            RunJob(j2);
            Assert.Contains("cannot be removed directly", string.Join("\n", admin.PeekMessages()));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Unset_LowercaseProtectedFlag_IsReadOnlyForNonSuperuser()
    {
        // Pin: exact-lowercase unset of a protected flag is already refused.
        ObjectRegistry.ClearAll();
        try
        {
            var builder = GameObject.Create("builder", privilege: Privilege.Builder);
            ObjectRegistry.AddObject(builder);
            var job = CommandDispatcher.DispatchLoggedIn(builder, "unset me is_pc", immediate: true);
            RunJob(job);
            Assert.Contains("read-only attribute", string.Join("\n", builder.PeekMessages()));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    // --- B4: ChannelCommand phantom channel ---

    [Fact]
    public void Channel_NonChannelRegistryObject_IsNotCachedAsTransient()
    {
        // audit B4: when the registry object has IsChannel set but is not a
        // Channel instance, a fabricated transient is cached under the name.
        ObjectRegistry.ClearAll();
        ChannelCommand.ClearCache();
        try
        {
            var fake = GameObject.Create("pub");
            fake.IsChannel = true;
            ObjectRegistry.AddObject(fake);
            var puppet = GameObject.Create("listener");
            ObjectRegistry.AddObject(puppet);
            var cmd = new ChannelCommand();
            var (func, caller, args) = cmd.Execute(puppet, "-c pub -s");
            Assert.NotNull(func);
            func!(caller!, args);
            Assert.False(ChannelCommand.TryGetCached("pub", out _));
        }
        finally { ChannelCommand.ClearCache(); ObjectRegistry.ClearAll(); }
    }

    // --- B5: SpamCommand validation ---

    [Fact]
    public void Spam_ZeroCount_IsRejected_NotReportedAsCreated()
    {
        // audit B5: count<=0 is unvalidated; `spam 0` reports "Created 0".
        using var env = GlobalTestEnv.Enter();
        var origSave = AtherizSettings.Global.SavePath;
        var tmp = Path.Combine(env.TempPath, "spamdir");
        Directory.CreateDirectory(tmp);
        AtherizSettings.Global.SavePath = tmp;
        try
        {
            var admin = GameObject.Create("root", privilege: Privilege.Admin);
            ObjectRegistry.AddObject(admin);
            var job = CommandDispatcher.DispatchLoggedIn(admin, "spam 0", immediate: true);
            RunJob(job);
            var msgs = string.Join("\n", admin.PeekMessages());
            Assert.DoesNotContain("Created 0 accounts", msgs);
            Assert.Matches("(?i)(positive|at least|greater|invalid|usage)", msgs);
        }
        finally { AtherizSettings.Global.SavePath = origSave; }
    }

    // --- B6: DoorCommand stale-default settings ---

    [Fact]
    public void Door_Create_UsesGlobalGlyphSettings()
    {
        // audit B6: DoorCommand reads AtherizSettings.Default while MapOpen /
        // MapClose use Global, so mutated Global glyphs are ignored at create.
        using var env = GlobalTestEnv.Enter();
        var orig = AtherizSettings.Global.NsClosedDoor;
        AtherizSettings.Global.NsClosedDoor = "GLOBTEST";
        try
        {
            var nh = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh);
            var coord = new Coord("limbo", 0, 0, 0);
            var node = new Node(coord);
            if (ObjectRegistry.Get(node.Id).Count == 0) ObjectRegistry.AddObject(node);
            nh.AddNode(node);
            var builder = GameObject.Create("bob2", privilege: Privilege.Builder);
            Assert.True(builder.MoveTo(node));
            ObjectRegistry.AddObject(builder);
            var job = CommandDispatcher.DispatchLoggedIn(builder, "door -n -a", immediate: true);
            RunJob(job);
            var doors = nh.GetDoors(coord);
            Assert.NotNull(doors);
            var door = Assert.Single(doors!.Values, d => d.FromExit == "north");
            Assert.Equal("GLOBTEST", door.ClosedSymbol);
        }
        finally { AtherizSettings.Global.NsClosedDoor = orig; NodeHandler.SetCurrent(null); }
    }

    // --- B7: dispatcher NoAlias omits x ---

    [Fact]
    public void Dispatcher_NoAliasCommands_IncludesX()
    {
        // audit B7: NoAliasCommands lacks "x" although x is a Build direction,
        // so a lone "x" falls into auto-alias prefix search instead of the
        // NoAlias refusal every other single-char direction gets.
        var f = typeof(CommandDispatcher).GetField("NoAliasCommands", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(f);
        var arr = Assert.IsAssignableFrom<string[]>(f!.GetValue(null));
        Assert.Contains("x", arr);
    }

    // --- B8: CmdSet case + registry staleness ---

    [Fact]
    public void CmdSet_Get_IsCaseInsensitive()
    {
        // audit B8: the dict is Ordinal; only the dispatcher's lower-casing
        // hides it. Mixed-case alias lookups break.
        var set = new CmdSet();
        set.Add(new SayCommand());
        Assert.NotNull(set.Get("SAY"));
        Assert.NotNull(set.Get("Say"));
    }

    [Fact]
    public void Registry_UnloggedIn_ReflectsDisabledGuestSetting()
    {
        // audit B8: RegisterUnloggedIn snapshots Global.*Enabled once; later
        // flips (e.g. disabling guests) have no effect until ResetForTesting.
        using var env = GlobalTestEnv.Enter();
        var orig = AtherizSettings.Global.GuestEnabled;
        AtherizSettings.Global.GuestEnabled = true;
        CommandRegistry.ResetForTesting();
        _ = CommandRegistry.UnloggedIn;
        AtherizSettings.Global.GuestEnabled = false;
        try
        {
            var conn = new TestConnection();
            var job = CommandDispatcher.ResolveUnloggedIn(conn, "guest");
            Assert.Null(job);
        }
        finally { AtherizSettings.Global.GuestEnabled = orig; CommandRegistry.ResetForTesting(); }
    }

    // --- B9: silent returns + none fallback ---

    [Fact]
    public void Say_NonPuppetCaller_GetsRefusalMessage()
    {
        // audit B9: SayCommand silently returns for non-GameObject callers;
        // every other command sends "You can't do that."
        var conn = new TestConnection();
        var cmd = new SayCommand();
        var (func, caller, args) = cmd.Execute(conn, "hi there");
        Assert.NotNull(func);
        func!(caller!, args);
        Assert.Contains(conn.Sent, t => t.Args.Any(a => a != null && a.ToString()!.Contains("You can't do that.")));
    }

    [Fact]
    public void Emote_NonPuppetCaller_GetsRefusalMessage()
    {
        // audit B9: same silent return in EmoteCommand.
        var conn = new TestConnection();
        var cmd = new EmoteCommand();
        var (func, caller, args) = cmd.Execute(conn, "smiles");
        Assert.NotNull(func);
        func!(caller!, args);
        Assert.Contains(conn.Sent, t => t.Args.Any(a => a != null && a.ToString()!.Contains("You can't do that.")));
    }

    [Fact]
    public void None_UnknownCommand_SuggestsClosestMatch()
    {
        // Pin: the UseParser=false fallback suggests the closest verb
        // (covers StringDistance.BestMatch end to end).
        ObjectRegistry.ClearAll();
        try
        {
            var puppet = GameObject.Create("hero");
            ObjectRegistry.AddObject(puppet);
            var job = CommandDispatcher.DispatchLoggedIn(puppet, "sayy hello", immediate: true);
            RunJob(job);
            Assert.Contains("did you mean", string.Join("\n", puppet.PeekMessages()));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    // --- B10: Unban dead privates ---

    [Fact]
    public void UnbanCommand_DeadPrivateReasonHelpers_AreRemoved()
    {
        // audit B10: UnbanCommand.ClearBanReason/SetBanReason are never called
        // (the live path uses BanReasonHelper, which diverges); dead code must go.
        var t = typeof(UnbanCommand);
        Assert.Null(t.GetMethod("ClearBanReason", BindingFlags.NonPublic | BindingFlags.Static));
        Assert.Null(t.GetMethod("SetBanReason", BindingFlags.NonPublic | BindingFlags.Static));
    }

    [Fact]
    public void Unban_ClearsBanReasonExtra()
    {
        // Pin: the live helper path clears the ban_reason extra.
        ObjectRegistry.ClearAll();
        try
        {
            var admin = GameObject.Create("admin", privilege: Privilege.Admin);
            var target = GameObject.Create("victim", isPc: true);
            ObjectRegistry.AddObject(admin);
            ObjectRegistry.AddObject(target);
            target.IsBanned = true;
            target.BanReason = "spam";
            target.SetExtraJson("ban_reason", JsonSerializer.SerializeToElement("spam"));
            var job = CommandDispatcher.DispatchLoggedIn(admin, "unban victim", immediate: true);
            RunJob(job);
            Assert.False(target.IsBanned);
            Assert.Equal("", target.BanReason);
            Assert.Contains("Unbanned victim", string.Join("\n", admin.PeekMessages()));
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
