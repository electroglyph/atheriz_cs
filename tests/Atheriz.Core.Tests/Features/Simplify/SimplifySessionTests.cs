using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Core.Tests;
using Atheriz.Core;
using System.Reflection;

namespace Atheriz.Core.Tests.Features.Simplify;

// Merged from ConnectionScreenCountTests.cs
// One snapshot pass for both PC counts: same materialized list, so one loop
// is arithmetically identical to Count(predicate) + Count. Static predicate
// (no per-call closure); the 5 s render cache untouched.
[Collection("Ported")]
public class ConnectionScreenCountTests
{
    private static void ResetCache()
    {
        typeof(ConnectionScreen).GetField("_cacheTs", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, 0.0);
    }

    [Fact]
    public void GetOnline_EmptyRegistry_ZeroZero()
    {
        using var env = GlobalTestEnv.Enter();
        ResetCache();
        Assert.Equal((0, 0), ConnectionScreen.GetOnline());
    }

    [Fact]
    public void GetOnline_SinglePass_CountsBoth()
    {
        using var env = GlobalTestEnv.Enter();
        ObjectRegistry.AddObject(GameObject.Create("known_pc", isPc: true));
        ObjectRegistry.AddObject(GameObject.Create("mob"));
        ResetCache();
        var (online, known) = ConnectionScreen.GetOnline();
        Assert.Equal(1, known);
        Assert.Equal(0, online);
        Assert.True(known >= online);
    }

    [Fact]
    public void Counting_UsesOneLoop_AndStaticPredicate()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "ConnectionScreen.cs");
        Assert.Contains("static readonly Func<", src);
        Assert.Contains("IsPcFilter", src);
        Assert.DoesNotContain(".Count(o =>", src);
    }
}

// Merged from ConnectionScreenProbeTests.cs
// Static gate probes: GuestCommand/CreateAccountCommand are stateless
// (get-only Key/Desc; IsUnloggedInEnabled does type tests only, Run never
// invoked), so shared instances render byte-identical hints.
[Collection("Ported")]
public class ConnectionScreenProbeTests
{
    [Fact]
    public void ProbeCommands_AreStateless()
    {
        var g = new GuestCommand();
        Assert.Equal("guest", g.Key);
        Assert.Equal("Create a temporary guest character and enter the game.", g.Desc);
        var c = new CreateAccountCommand();
        Assert.Equal("create", c.Key);
        Assert.Equal("Create a new account.", c.Desc);
    }

    [Fact]
    public void Render_HintsFollowSettingsAndGate()
    {
        using var env = GlobalTestEnv.Enter();
        var g = AtherizSettings.Global;
        bool og = g.GuestEnabled, oc = g.AccountCreationEnabled;
        g.GuestEnabled = true;
        g.AccountCreationEnabled = true;
        CommandDispatcher.SetSettings(new AtherizSettings());
        var sess = new Session { ScreenReader = true };
        try
        {
            string full = ConnectionScreen.Render(new AtherizSettings(), sess);
            Assert.Contains("enter 'guest' to create a temporary character", full);
            Assert.Contains("enter 'create' to make a new account", full);
            var off = new AtherizSettings { GuestEnabled = false, AccountCreationEnabled = false };
            string bare = ConnectionScreen.Render(off, sess);
            Assert.DoesNotContain("'guest'", bare);
            Assert.DoesNotContain("'create'", bare);
        }
        finally
        {
            g.GuestEnabled = og;
            g.AccountCreationEnabled = oc;
            CommandDispatcher.SetSettings(new AtherizSettings());
        }
    }

    [Fact]
    public void Probes_AreSharedStatics()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "ConnectionScreen.cs");
        Assert.Contains("static readonly", src);
        Assert.Contains("GuestProbe", src);
        Assert.Contains("CreateProbe", src);
        Assert.DoesNotContain("new Commands.UnloggedIn.GuestCommand()", src);
        Assert.DoesNotContain("new Commands.UnloggedIn.CreateAccountCommand()", src);
    }
}

// Merged from SessionCommandWireTests.cs
// AtPostPuppet session commands: the snapshot+guard+send core delivers each
// command with byte-identical wire strings — the bare logged_in call and the
// payload-carrying calls arrive at SendCommand with the same normalization.
[Collection("Ported")]
public class SessionCommandWireTests
{
    [Fact]
    public void AtPostPuppet_SendsLoggedIn_And_PlayerCommands()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var conn = new TestConnection("wire");
            var session = new Session(conn);
            var obj = GameObject.Create("pc");
            ObjectRegistry.AddObject(obj);
            obj.Session = session;

            obj.AtPostPuppet();

            var loggedIn = conn.Sent.Where(s => s.Cmd == "logged_in").ToList();
            Assert.Single(loggedIn);
            Assert.Empty(loggedIn[0].Args);
            var playerCommands = conn.Sent.Where(s => s.Cmd == "player_commands").ToList();
            Assert.Single(playerCommands);
            Assert.Single(playerCommands[0].Args);
            // No location: the map block is skipped, so no map_enable goes out.
            Assert.DoesNotContain(conn.Sent, s => s.Cmd == "map_enable");
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}

// Merged from SessionDisconnectCleanupTests.cs
// AtDisconnect temp-puppet cleanup: the coord fast path removes the puppet
// from its primary node, and a coord miss falls back to a full node scan so
// stray contents are still cleaned up.
[Collection("Ported")]
public class SessionDisconnectCleanupTests
{
    [Fact]
    public void AtDisconnect_CoordHit_RemovesPuppetFromPrimaryNode()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var coord = new Coord("limbo", 0, 0, 0);
            var node = new Node(coord);
            ObjectRegistry.AddObject(node);
            var puppet = GameObject.Create("temp");
            puppet.IsTemporary = true;
            ObjectRegistry.AddObject(puppet);
            node.AddObject(puppet);
            var session = new Session { Puppet = puppet };

            session.AtDisconnect();

            Assert.DoesNotContain(puppet.Id, node.ContentsSnapshot);
            Assert.Empty(ObjectRegistry.Get(puppet.Id));
            Assert.IsType<LocationRef.NullLocation>(puppet.Location);
            Assert.True(puppet.IsDeleted);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void AtDisconnect_CoordMiss_FallsBackToFullScan()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var node = new Node(new Coord("limbo", 1, 1, 1));
            ObjectRegistry.AddObject(node);
            var puppet = GameObject.Create("temp");
            puppet.IsTemporary = true;
            // Location points at a coord with no node; the id is also planted
            // as stray contents on an unrelated node.
            puppet.Location = new LocationRef.CoordLocation(new Coord("limbo", 9, 9, 9));
            ObjectRegistry.AddObject(puppet);
            node.AddContent(puppet.Id);
            var session = new Session { Puppet = puppet };

            session.AtDisconnect();

            Assert.DoesNotContain(puppet.Id, node.ContentsSnapshot);
            Assert.Empty(ObjectRegistry.Get(puppet.Id));
            Assert.True(puppet.IsDeleted);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}

// Merged from SessionPromptCompletionTests.cs
// Session prompt bookkeeping: a completed previous prompt is cleared (not
// re-completed), and disconnect cancels a pending prompt without throwing.
[Collection("Ported")]
public class SessionPromptCompletionTests
{
    [Fact]
    public async Task Prompt_WithCompletedPrev_InstallsFreshFuture()
    {
        var session = new Session();
        session.InputFuture = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.InputFuture.TrySetResult("old");
        try
        {
            var pending = session.Prompt("hello");
            Assert.True(session.CancelPrompt());
            Assert.Equal("", await pending);
        }
        finally { session.InputFuture = null; }
    }

    [Fact]
    public void AtDisconnect_CancelsPendingPrompt()
    {
        var session = new Session();
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.InputFuture = tcs;

        session.AtDisconnect();

        Assert.True(tcs.Task.IsCanceled);
        Assert.Null(session.InputFuture);
    }
}

// Merged from BanPreambleMessageTests.cs
[Collection("Ported")]
public sealed class BanPreambleMessageTests
{
    private static GameObject MakeCaller()
    {
        var c = GameObject.Create("Warden", privilege: Atheriz.Core.Privilege.Builder);
        ObjectRegistry.AddObject(c);
        c.ClearMessages();
        return c;
    }

    private static GameObject MakePc(string name)
    {
        var pc = GameObject.Create(name, isPc: true);
        ObjectRegistry.AddObject(pc);
        pc.PrivilegeLevel = Atheriz.Core.Privilege.Player;
        pc.ClearMessages();
        return pc;
    }

    [Fact]
    public void Ban_AccountWithoutAccount_FallsBackWithBanGerund()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var orphan = MakePc("Orphan");
        new BanCommand().Run(caller, new BanCommand().Parser!.ParseArgs(["Orphan", "--account"]));
        Assert.True(orphan.IsBanned);
        Assert.Contains(caller.PeekMessages(), m => m.Contains("banning character only"));
        Assert.Contains(caller.PeekMessages(), m => m.Contains("Banned Orphan (character)."));
    }

    [Fact]
    public void Unban_AccountWithoutAccount_FallsBackWithUnbanGerund()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var orphan = MakePc("Orphan");
        orphan.IsBanned = true;
        new UnbanCommand().Run(caller, new UnbanCommand().Parser!.ParseArgs(["Orphan", "--account"]));
        Assert.False(orphan.IsBanned);
        Assert.Contains(caller.PeekMessages(), m => m.Contains("unbanning character only"));
        Assert.Contains(caller.PeekMessages(), m => m.Contains("Unbanned Orphan (character)."));
    }

    [Fact]
    public void Ban_ReasonIsOptionalText_NotAGate()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var target = MakePc("Newbie");
        new BanCommand().Run(caller, new BanCommand().Parser!.ParseArgs(["Newbie"]));
        Assert.True(target.IsBanned);
        Assert.Contains(caller.PeekMessages(), m => m == "Banned Newbie (character).");
    }

    [Fact]
    public void Ban_ReasonAppendedToScopeMessage()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var target = MakePc("Spammer");
        new BanCommand().Run(caller, new BanCommand().Parser!.ParseArgs(["Spammer", "-r", "spamming"]));
        Assert.True(target.IsBanned);
        Assert.Contains(caller.PeekMessages(), m => m == "Banned Spammer (character, reason: spamming).");
    }
}

// Merged from CharCreateExistenceTests.cs
// Early-exit registry lookups in AtCharCreate: same snapshot-then-scan lock
// discipline as FilterBy (snapshot under the read lock, predicates outside),
// first match in registry order wins. The account lookup returns the object
// (password check, MaxCharacters, AddCharacter need it), not just existence.
[Collection("Ported")]
public class CharCreateExistenceTests
{
    private static string Create(string acc, string ch, string pw)
    {
        var home = new Node(AtherizSettings.Global.DefaultHome);
        ObjectRegistry.AddObject(home);
        var sw = new StringWriter();
        ServerEvents.AtCharCreate(acc, ch, pw, sw);
        return sw.ToString();
    }

    [Fact]
    public void DuplicateCharacterName_Refused()
    {
        using var env = GlobalTestEnv.Enter();
        Assert.Contains("Success! Account and character created.", Create("ac1", "HeroX", "password123"));
        Assert.Contains("already exists", Create("ac2", "HeroX", "password123"));
    }

    [Fact]
    public void SameAccount_SamePassword_AddsCharacter_WrongPassword_Refused()
    {
        using var env = GlobalTestEnv.Enter();
        Assert.Contains("Success! Account and character created.", Create("ac1", "HeroA", "password123"));
        Assert.Contains("Success! Character created.", Create("ac1", "HeroB", "password123"));
        Assert.Contains("different password", Create("ac1", "HeroC", "otherpass99"));
    }

    [Fact]
    public void ExistenceHelpers_SnapshotThenScan_EarlyExit()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "ServerEvents.cs");
        Assert.Equal(1, SourceScan.Count(src, "private static bool RegistryExists("));
        Assert.Equal(1, SourceScan.Count(src, "private static GameObject? RegistryFindFirst("));
        Assert.Contains("ObjectRegistry.FilterBy(_ => true)", src);
        Assert.DoesNotContain("FilterBy(o => o.IsPc", src);
        Assert.DoesNotContain(".Count > 0", src);
    }
}

// Merged from PuppetSnapshotRoundTripTests.cs
// Puppet snapshot restore: the typed record carries both fields, so restore
// applies exactly what the snapshot holds — no lookup arms, no ignored types.
[Collection("Ported")]
public class PuppetSnapshotRoundTripTests
{
    [Fact]
    public void RestorePuppetSnapshot_AppliesRecordFields()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var obj = GameObject.Create("npc");
            obj.IsPc = false;
            obj.PrivilegeLevel = Privilege.Player;

            obj.RestorePuppetSnapshot(new GameObject.PuppetRestoreSnapshot(true, Privilege.Admin));
            Assert.True(obj.IsPc);
            Assert.Equal(Privilege.Admin, obj.PrivilegeLevel);

            obj.RestorePuppetSnapshot(new GameObject.PuppetRestoreSnapshot(false, Privilege.Helper));
            Assert.False(obj.IsPc);
            Assert.Equal(Privilege.Helper, obj.PrivilegeLevel);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void PuppetRestoreSnapshot_ComparesByValue()
    {
        Assert.Equal(
            new GameObject.PuppetRestoreSnapshot(true, Privilege.Admin),
            new GameObject.PuppetRestoreSnapshot(true, Privilege.Admin));
        Assert.NotEqual(
            new GameObject.PuppetRestoreSnapshot(true, Privilege.Admin),
            new GameObject.PuppetRestoreSnapshot(false, Privilege.Admin));
        Assert.NotEqual(
            new GameObject.PuppetRestoreSnapshot(true, Privilege.Admin),
            new GameObject.PuppetRestoreSnapshot(true, Privilege.Guest));
    }
}

// Merged from PuppetSuppressLogTests.cs
// Suppressed-log fan-out: every site catches Exception, logs only the message
// under its own byte-identical context prefix, and swallows — verified here
// for the Puppet tail (a throwing game hook still completes the puppet).
[Collection("Ported")]
public class PuppetSuppressLogTests
{
    private sealed class ThrowingPuppet : GameObject
    {
        public override void AtPuppet(GameObject caller) => throw new InvalidOperationException("boom");
    }

    [Fact]
    public void Puppet_ThrowingHook_LogsContextPrefix_AndCompletes()
    {
        using var env = GlobalTestEnv.Enter();
        AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "debug", SavePath = env.TempPath });
        ObjectRegistry.ClearAll();
        try
        {
            var conn = new TestConnection("suppress");
            var session = new Session(conn);
            var caller = GameObject.Create("caller");
            ObjectRegistry.AddObject(caller);
            var npc = new ThrowingPuppet();
            npc.Id = GameObject.GetNextId();
            npc.Name = "npc";
            ObjectRegistry.AddObject(npc);

            string log;
            using (var cap = new CaptureAtherizLog())
            {
                Assert.True(caller.Puppet(session, npc));
                log = cap.Read();
            }

            Assert.Contains("Suppressed GameObject.Puppet: boom", log);
        }
        finally
        {
            ObjectRegistry.ClearAll();
            AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "info", SavePath = env.TempPath });
        }
    }
}
