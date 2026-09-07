using System.Reflection;
using System.Text.Json;
using Atheriz.Core;
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

// Tests for logged-in command behavior: set/unset guards, channel/spam/door/say/emote/unban commands, CmdSet dispatch.
[Collection("Ported")]
public class LoggedInCommandTests
{
    private static void RunJob(CommandDispatcher.Job? job)
    {
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);
    }

    // --- set/unset protected-attribute guards ---

    [Fact]
    public void Set_CaseVariantOfProtectedFlag_DoesNotEscalate()
    {
        // Protected guard is case-insensitive while property resolution stays
        // case-sensitive (Python parity for junk attrs), so no spelling of a
        // protected flag can be set by a non-superuser.
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
    public void Set_QuelledCaseVariant_DoesNotEscalate()
    {
        // Same guard covers the quelled flag (which gates IsBuilder /
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
        // Exact-lowercase spelling is already guarded.
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
    public void Set_LocationGuard_LowercaseRedirects_CanonicalIsReadOnly()
    {
        // Location guard list is Ordinal (as in set.py:154), so lowercase
        // "location" redirects to move/teleport. "Location" is the canonical
        // C# property name, so it resolves — but a string can never convert
        // to LocationRef, hence read-only. Either way the location is never
        // set directly.
        ObjectRegistry.ClearAll();
        try
        {
            var admin = GameObject.Create("admin", privilege: Privilege.Admin);
            ObjectRegistry.AddObject(admin);
            var j1 = CommandDispatcher.DispatchLoggedIn(admin, "set me location null", immediate: true);
            RunJob(j1);
            Assert.Contains("use move/teleport instead", string.Join("\n", admin.PeekMessages()));
            admin.ClearMessages();
            var j2 = CommandDispatcher.DispatchLoggedIn(admin, "set me Location null", immediate: true);
            RunJob(j2);
            Assert.Contains("read-only attribute", string.Join("\n", admin.PeekMessages()));
            Assert.IsType<LocationRef.NullLocation>(admin.Location);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Unset_LocationGuard_LowercaseRedirects_CanonicalIsReadOnly()
    {
        // Lowercase "location" redirects (unset.py:230); the canonical C#
        // property resolves but is not removable — read-only. Either way it
        // is never removed directly.
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
            Assert.Contains("read-only attribute", string.Join("\n", admin.PeekMessages()));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Unset_LowercaseProtectedFlag_IsProtectedForNonSuperuser()
    {
        // Exact-lowercase unset of a protected flag is refused with the
        // protected wording (set.py:226-228), not the read-only wording.
        ObjectRegistry.ClearAll();
        try
        {
            var builder = GameObject.Create("builder", privilege: Privilege.Builder);
            ObjectRegistry.AddObject(builder);
            var job = CommandDispatcher.DispatchLoggedIn(builder, "unset me is_pc", immediate: true);
            RunJob(job);
            Assert.Contains("is protected and cannot be removed", string.Join("\n", builder.PeekMessages()));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    // --- channel handling ---

    [Fact]
    public void Channel_NonChannelRegistryObject_IsNotCachedAsTransient()
    {
        // When the registry object has IsChannel set but is not a Channel
        // instance, no fabricated transient is cached under the name.
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

    // --- spam command ---

    [Fact]
    public void Spam_ZeroCount_ReportsCreatedZero()
    {
        // Python parity (spam.py: has no count<=0 validation; the loop simply
        // runs zero times and reports Created 0).
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
            Assert.Contains("Created 0 accounts", msgs);
        }
        finally { AtherizSettings.Global.SavePath = origSave; }
    }

    // --- door glyph settings ---

    [Fact]
    public void Door_Create_UsesGlobalGlyphSettings()
    {
        // Door creation uses the current Global glyph settings, matching
        // MapOpen / MapClose.
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

    // --- dispatcher NoAlias commands ---

    [Fact]
    public void Dispatcher_NoAliasCommands_MatchesPython()
    {
        // Python's _NO_ALIAS_COMMANDS (inputfuncs.py:16) is exactly
        // ["n","s","e","w","u","d"] — "x" is a build direction but is NOT
        // no-alias in either implementation. Pins parity.
        var f = typeof(CommandDispatcher).GetField("NoAliasCommands", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(f);
        var arr = Assert.IsAssignableFrom<string[]>(f!.GetValue(null));
        Assert.Equal(new[] { "n", "s", "e", "w", "u", "d" }, arr);
    }

    // --- CmdSet lookup and registry freshness ---

    [Fact]
    public void CmdSet_Get_IsCaseInsensitive()
    {
        // Lookups are case-insensitive, so mixed-case alias lookups succeed.
        var set = new CmdSet();
        set.Add(new SayCommand());
        Assert.NotNull(set.Get("SAY"));
        Assert.NotNull(set.Get("Say"));
    }

    [Fact]
    public void Registry_UnloggedIn_ReflectsDisabledGuestSetting()
    {
        // A disabled verb falls through to the none fallback ("not found")
        // instead of running a stale command, even after Global.*Enabled
        // flips (e.g. disabling guests).
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
            Assert.NotNull(job);
            job!.Func(job.Caller, job.Args);
            var text = string.Join("\n", conn.Sent.SelectMany(t => t.Args.Select(a => a?.ToString() ?? "")));
            Assert.Contains("not found", text, StringComparison.OrdinalIgnoreCase);
        }
        finally { AtherizSettings.Global.GuestEnabled = orig; CommandRegistry.ResetForTesting(); }
    }

    // --- say/emote refusal and unknown-command fallback ---

    [Fact]
    public void Say_NonPuppetCaller_GetsRefusalMessage()
    {
        // SayCommand sends "You can't do that." for non-GameObject callers,
        // like every other command.
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
        // Same refusal behavior in EmoteCommand.
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
        // The UseParser=false fallback suggests the closest verb (covers
        // StringDistance.BestMatch end to end).
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

    // --- unban helpers and ban clearing ---

    [Fact]
    public void UnbanCommand_DeadPrivateReasonHelpers_AreRemoved()
    {
        // UnbanCommand has no ClearBanReason/SetBanReason helpers; the live
        // path uses BanReasonHelper.
        var t = typeof(UnbanCommand);
        Assert.Null(t.GetMethod("ClearBanReason", BindingFlags.NonPublic | BindingFlags.Static));
        Assert.Null(t.GetMethod("SetBanReason", BindingFlags.NonPublic | BindingFlags.Static));
    }

    [Fact]
    public void Unban_ClearsBanReasonExtra()
    {
        // The live helper path clears the ban_reason extra.
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
