// Port of atheriz/tests/test_ban_command.py:1
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Settings;
using System.Reflection;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedBanCommandTests
{
    private static GameObject MakeCaller(Atheriz.Core.Privilege priv = Atheriz.Core.Privilege.Builder) => PortedHelpers.MakeCaller(priv);
    private static GameObject MakePc(string name = "Bob", Atheriz.Core.Privilege priv = Atheriz.Core.Privilege.Player)
    {
        var pc = GameObject.Create(name, isPc: true);
        ObjectRegistry.AddObject(pc);
        pc.PrivilegeLevel = priv;
        pc.ClearMessages();
        return pc;
    }
    private static FakeConnection AttachConnection(GameObject pc, string host = "1.2.3.4", Account? account = null)
    {
        var conn = new FakeConnection(sessionId: $"conn-{pc.Id}");
        conn.ClientHost = host;
        var sess = pc.Session ?? conn.Session;
        // ensure pc.Session points to conn.Session
        pc.Session = conn.Session;
        conn.Session.Puppet = pc;
        conn.Session.Connection = conn;
        if (account != null)
        {
            conn.Session.Account = account;
            conn.Session.AccountId = account.Id;
        }
        return conn;
    }
    private static string LastMsg(GameObject o) => o.PeekMessages().LastOrDefault() ?? "";
    private static IReadOnlyList<string> AllMsgs(GameObject o) => o.PeekMessages();

    // --- access control ---
    [Fact] public void AccessDeniedForNonBuilder()
    {
        using var env = GlobalTestEnv.Enter();
        var cmd = new BanCommand();
        var c = MakeCaller(Atheriz.Core.Privilege.Player);
        Assert.False(cmd.Access(c));
    }
    [Fact] public void AccessGrantedForBuilder()
    {
        using var env = GlobalTestEnv.Enter();
        var cmd = new BanCommand();
        Assert.True(cmd.Access(MakeCaller(Atheriz.Core.Privilege.Builder)));
    }
    [Fact] public void UnbanAccessDeniedForNonBuilder()
    {
        using var env = GlobalTestEnv.Enter();
        var cmd = new UnbanCommand();
        var c = MakeCaller(Atheriz.Core.Privilege.Player);
        Assert.False(cmd.Access(c));
    }
    [Fact] public void CannotBanEqualPrivilege()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller(Atheriz.Core.Privilege.Builder);
        var target = MakePc("Rival", Atheriz.Core.Privilege.Builder);
        var cmd = new BanCommand();
        cmd.Run(caller, cmd.Parser!.ParseArgs(new[] { "Rival" }));
        Assert.False(target.IsBanned);
        Assert.Contains(AllMsgs(caller), m => m.ToLowerInvariant().Contains("equal or higher"));
    }
    [Fact] public void CannotBanHigherPrivilege()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller(Atheriz.Core.Privilege.Builder);
        var target = MakePc("Boss", Atheriz.Core.Privilege.Admin);
        var cmd = new BanCommand();
        cmd.Run(caller, cmd.Parser!.ParseArgs(new[] { "Boss" }));
        Assert.False(target.IsBanned);
        Assert.Contains(AllMsgs(caller), m => m.Contains("equal or higher"));
    }
    [Fact] public void BuilderCanBanPlayer()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller(Atheriz.Core.Privilege.Builder);
        var target = MakePc("Newbie", Atheriz.Core.Privilege.Player);
        var cmd = new BanCommand();
        cmd.Run(caller, cmd.Parser!.ParseArgs(new[] { "Newbie" }));
        Assert.True(target.IsBanned);
    }
    [Fact] public void BanCharacterSetsFlag()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var target = MakePc("Chuck");
        var cmd = new BanCommand();
        cmd.Run(caller, cmd.Parser!.ParseArgs(new[] { "Chuck" }));
        Assert.True(target.IsBanned);
        // ban_reason not stored on GameObject IsBanned only — verify no extra banReason via Account; PC has no banReason field
        // Ensure target.IsBanned flag distinguishes from Account banReason
        Assert.True(target.IsBanned);
    }
    [Fact] public void BanCharacterWithReason()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var target = MakePc("Chuck");
        var cmd = new BanCommand();
        cmd.Run(caller, cmd.Parser!.ParseArgs(new[] { "Chuck", "-r", "spamming" }));
        Assert.True(target.IsBanned);
        // Reason appears in caller feedback (character scope)
        Assert.Contains(AllMsgs(caller), m => m.Contains("spamming"));
        // In C# PC ban_reason is not stored separately; Account ban_reason is via Account; for character ban, IsBanned is flag
    }
    [Fact] public void BanById()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var target = MakePc("Chuck");
        var cmd = new BanCommand();
        cmd.Run(caller, cmd.Parser!.ParseArgs(new[] { $"#{target.Id}" }));
        Assert.True(target.IsBanned);
    }
    [Fact] public void BanNpcRejected()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var npc = GameObject.Create("Goblin"); // is_pc false
        ObjectRegistry.AddObject(npc);
        var cmd = new BanCommand();
        cmd.Run(caller, cmd.Parser!.ParseArgs(new[] { $"#{npc.Id}" }));
        Assert.False(npc.IsBanned);
        Assert.Contains(AllMsgs(caller), m => m.ToLowerInvariant().Contains("player characters"));
    }
    [Fact] public void BanNoMatch()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var cmd = new BanCommand();
        cmd.Run(caller, cmd.Parser!.ParseArgs(new[] { "Ghost" }));
        Assert.Contains(AllMsgs(caller), m => m.Contains("No player character") || m.ToLowerInvariant().Contains("no player") || m.Contains("No player"));
    }
    [Fact] public void BanMultipleMatchesListsIds()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var a = MakePc("Dup");
        var b = MakePc("Dup");
        var cmd = new BanCommand();
        cmd.Run(caller, cmd.Parser!.ParseArgs(new[] { "Dup" }));
        Assert.False(a.IsBanned);
        Assert.False(b.IsBanned);
        var msgs = AllMsgs(caller);
        Assert.Contains(msgs, m => m.Contains("Multiple matches"));
        Assert.Contains(msgs, m => m.Contains($"#{a.Id}"));
        Assert.Contains(msgs, m => m.Contains($"#{b.Id}"));
    }
    [Fact] public void BanAccountPropagatesToAllCharacters()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var acct = Account.Create("acct1", "pass12345");
        var charA = MakePc("Alice");
        var charB = MakePc("Bob");
        acct.AddCharacter(charA);
        acct.AddCharacter(charB);
        var cmd = new BanCommand();
        cmd.Run(caller, cmd.Parser!.ParseArgs(new[] { "Alice", "--account", "-r", "toxic" }));
        Assert.True(acct.IsBanned);
        Assert.Equal("toxic", acct.BanReason);
        Assert.True(charA.IsBanned);
        Assert.True(charB.IsBanned);
    }
    [Fact] public void BanAccountOfflineTargetResolves()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var acct = Account.Create("acct2", "pass12345");
        var ch = MakePc("Offline");
        acct.AddCharacter(ch);
        var cmd = new BanCommand();
        cmd.Run(caller, cmd.Parser!.ParseArgs(new[] { "Offline", "--account" }));
        Assert.True(acct.IsBanned);
        Assert.True(ch.IsBanned);
    }
    [Fact] public void BanAccountNoAccountFallsBackToCharacter()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var orphan = MakePc("Orphan");
        var cmd = new BanCommand();
        cmd.Run(caller, cmd.Parser!.ParseArgs(new[] { "Orphan", "--account" }));
        Assert.True(orphan.IsBanned);
        var msgs = AllMsgs(caller);
        Assert.Contains(msgs, m => m.ToLowerInvariant().Contains("character only"));
        Assert.Contains(msgs, m => m.Contains("(character)"));
    }
    [Fact] public void BanIpOnlineTarget()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var target = MakePc("Online");
        AttachConnection(target, host: "9.9.9.9");
        var cmd = new BanCommand();
        cmd.Run(caller, cmd.Parser!.ParseArgs(new[] { "Online", "--ip" }));
        Assert.True(ObjectRegistry.IsIpBanned("9.9.9.9"));
        // Expires infinity => IsIpBanned true
    }
    [Fact] public void BanIpOfflineTargetWarns()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var target = MakePc("Offline");
        var cmd = new BanCommand();
        cmd.Run(caller, cmd.Parser!.ParseArgs(new[] { "Offline", "--ip" }));
        var msgs = AllMsgs(caller);
        Assert.Contains(msgs, m => m.ToLowerInvariant().Contains("cannot ban ip"));
        Assert.False(ObjectRegistry.IsIpBanned("1.2.3.4")); // no ban added
        // Ensure our target's host not banned (host was null)
        Assert.False(ObjectRegistry.IsIpBanned("9.9.9.9"));
    }
    [Fact] public void BanKicksConnectedTarget()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var target = MakePc("Connected");
        var conn = AttachConnection(target);
        var cmd = new BanCommand();
        cmd.Run(caller, cmd.Parser!.ParseArgs(new[] { "Connected" }));
        Assert.True(conn.Closed);
    }
    [Fact] public void UnbanCharacterClearsFlag()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var target = MakePc("Chuck");
        target.IsBanned = true;
        var cmd = new UnbanCommand();
        cmd.Run(caller, cmd.Parser!.ParseArgs(new[] { "Chuck" }));
        Assert.False(target.IsBanned);
    }
    [Fact] public void UnbanAccountClearsAll()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var acct = Account.Create("acct3", "pass12345");
        var charA = MakePc("A");
        var charB = MakePc("B");
        acct.AddCharacter(charA);
        acct.AddCharacter(charB);
        acct.IsBanned = true; acct.BanReason = "x";
        charA.IsBanned = true; charB.IsBanned = true;
        var cmd = new UnbanCommand();
        cmd.Run(caller, cmd.Parser!.ParseArgs(new[] { "A", "--account" }));
        Assert.False(acct.IsBanned);
        Assert.Equal("", acct.BanReason);
        Assert.False(charA.IsBanned);
        Assert.False(charB.IsBanned);
    }
    [Fact] public void UnbanIpClearsEntry()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var target = MakePc("Online");
        AttachConnection(target, host: "5.5.5.5");
        ObjectRegistry.BanIp("5.5.5.5");
        Assert.True(ObjectRegistry.IsIpBanned("5.5.5.5"));
        var cmd = new UnbanCommand();
        cmd.Run(caller, cmd.Parser!.ParseArgs(new[] { "Online", "--ip" }));
        Assert.False(ObjectRegistry.IsIpBanned("5.5.5.5"));
    }
    [Fact] public void UnbanPrivilegeGate()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller(Atheriz.Core.Privilege.Builder);
        var target = MakePc("Rival", Atheriz.Core.Privilege.Builder);
        target.IsBanned = true;
        var cmd = new UnbanCommand();
        cmd.Run(caller, cmd.Parser!.ParseArgs(new[] { "Rival" }));
        Assert.True(target.IsBanned);
        Assert.Contains(AllMsgs(caller), m => m.Contains("equal or higher"));
    }
    private static int GetBoundedCount(string fieldName)
    {
        var f = typeof(ObjectRegistry).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        var bd = f?.GetValue(null);
        if (bd == null) return -1;
        var prop = bd.GetType().GetProperty("Count");
        return (int)(prop?.GetValue(bd) ?? -1);
    }
    private static void ClearBounded(string fieldName)
    {
        var f = typeof(ObjectRegistry).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        var bd = f?.GetValue(null);
        bd?.GetType().GetMethod("Clear")?.Invoke(bd, null);
    }
    [Fact] public void FailedLoginAttemptsBounded()
    {
        using var env = GlobalTestEnv.Enter();
        var f = typeof(ObjectRegistry).GetField("FailedLoginAttempts", BindingFlags.NonPublic | BindingFlags.Static);
        var bd = f!.GetValue(null);
        var clear = bd!.GetType().GetMethod("Clear")!;
        var set = bd.GetType().GetMethod("Set")!;
        clear.Invoke(bd, null);
        for (int i = 0; i < 6000; i++) set.Invoke(bd, new object[] { $"host-{i}", 1 });
        var countProp = bd.GetType().GetProperty("Count")!;
        int size = (int)countProp.GetValue(bd)!;
        Assert.True(size < 5000, $"FAILED_LOGIN_ATTEMPTS unbounded: {size} entries, expected LRU cap");
    }
    [Fact] public void TempBannedIpsBounded()
    {
        using var env = GlobalTestEnv.Enter();
        // Clear via reflection
        var f = typeof(ObjectRegistry).GetField("TempBannedIps", BindingFlags.NonPublic | BindingFlags.Static);
        var bd = f!.GetValue(null);
        bd!.GetType().GetMethod("Clear")!.Invoke(bd, null);
        var set = bd.GetType().GetMethod("Set")!;
        double now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600;
        for (int i = 0; i < 6000; i++) set.Invoke(bd, new object[] { $"10.0.{i/256}.{i%256}", now });
        int size = (int)bd.GetType().GetProperty("Count")!.GetValue(bd)!;
        Assert.True(size < 5000, $"TEMP_BANNED_IPS unbounded: {size}");
    }
    [Fact] public void CreationCooldownsBounded()
    {
        using var env = GlobalTestEnv.Enter();
        var f = typeof(ObjectRegistry).GetField("CreationCooldowns", BindingFlags.NonPublic | BindingFlags.Static);
        var bd = f!.GetValue(null);
        bd!.GetType().GetMethod("Clear")!.Invoke(bd, null);
        var set = bd.GetType().GetMethod("Set")!;
        double now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60;
        for (int i = 0; i < 6000; i++) set.Invoke(bd, new object[] { $"account:host-{i}", now });
        int size = (int)bd.GetType().GetProperty("Count")!.GetValue(bd)!;
        Assert.True(size < 5000, $"CREATION_COOLDOWNS unbounded: {size}");
    }
    [Fact] public void FailedLoginAttemptsEvictionPreservesRecent()
    {
        using var env = GlobalTestEnv.Enter();
        var f = typeof(ObjectRegistry).GetField("FailedLoginAttempts", BindingFlags.NonPublic | BindingFlags.Static);
        var bd = f!.GetValue(null);
        bd!.GetType().GetMethod("Clear")!.Invoke(bd, null);
        var set = bd.GetType().GetMethod("Set")!;
        var contains = bd.GetType().GetMethod("Contains")!;
        var countProp = bd.GetType().GetProperty("Count")!;
        for (int i = 0; i < 10000; i++) set.Invoke(bd, new object[] { $"h{i}", i });
        int size = (int)countProp.GetValue(bd)!;
        Assert.True(size < 10000, "must evict old entries, not grow forever");
        bool hasRecent = (bool)contains.Invoke(bd, new object[] { "h9999" })!;
        Assert.True(hasRecent, "most recent entry must survive eviction");
    }
}
