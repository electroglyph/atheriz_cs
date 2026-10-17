using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Commands;

// Batch E of the commands.md rollout: userpassword, password, perm, wall.
[Collection("Ported")]
public class BatchECommandsTests
{
    private static GameObject MakeSuper()
        => PortedHelpers.MakeCaller("BatchESuper", Privilege.Admin);

    private static void Run(GameObject caller, string text)
    {
        var job = CommandDispatcher.DispatchLoggedIn(caller, text, immediate: true);
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);
    }

    private static string SentText(TestConnection conn)
        => string.Join("\n", conn.Sent.SelectMany(t => t.Args.Select(a => a?.ToString() ?? "")));

    [Fact]
    public void Userpassword_ChangesCredentials()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeSuper();
        var acc = Account.Create("batchE-vic", "oldpassword1");
        Assert.True(acc.CheckPassword("oldpassword1"));
        Run(hero, "userpassword batchE-vic = newpassword1");
        Assert.True(acc.CheckPassword("newpassword1"));
        Assert.False(acc.CheckPassword("oldpassword1"));
    }

    [Fact]
    public void Userpassword_UnknownAccount_Answered()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeSuper();
        Run(hero, "userpassword zzz = newpassword1");
        Assert.Single(hero.PeekMessages());
    }

    [Fact]
    public void Userpassword_ShortPassword_Refused()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeSuper();
        var acc = Account.Create("batchE-vic2", "oldpassword1");
        Run(hero, "userpassword batchE-vic2 = short");
        Assert.Single(hero.PeekMessages());
        Assert.True(acc.CheckPassword("oldpassword1"));
    }

    [Fact]
    public void Password_ChangesOwnCredentials()
    {
        using var env = GlobalTestEnv.Enter();
        var acc = Account.Create("batchE-self", "oldpassword1");
        var hero = PortedHelpers.MakeCaller("Selfy");
        hero.Session = new Session(account: acc);
        Run(hero, "password oldpassword1 = brandnewpw");
        Assert.True(acc.CheckPassword("brandnewpw"));
    }

    [Fact]
    public void Password_WrongOld_Refused()
    {
        using var env = GlobalTestEnv.Enter();
        var acc = Account.Create("batchE-self2", "oldpassword1");
        var hero = PortedHelpers.MakeCaller("Selfy");
        hero.Session = new Session(account: acc);
        Run(hero, "password wrongold = brandnewpw");
        Assert.Single(hero.PeekMessages());
        Assert.True(acc.CheckPassword("oldpassword1"));
    }

    [Fact]
    public void Password_ShortNew_Refused()
    {
        using var env = GlobalTestEnv.Enter();
        var acc = Account.Create("batchE-self3", "oldpassword1");
        var hero = PortedHelpers.MakeCaller("Selfy");
        hero.Session = new Session(account: acc);
        Run(hero, "password oldpassword1 = short");
        Assert.Single(hero.PeekMessages());
        Assert.True(acc.CheckPassword("oldpassword1"));
    }

    [Fact]
    public void Password_NoAccount_Answered()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = PortedHelpers.MakeCaller("NoAcct");
        Run(hero, "password a = brandnewpw");
        Assert.Single(hero.PeekMessages());
    }

    [Fact]
    public void Perm_Show_ReportsRank()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeSuper();
        var bob = GameObject.Create("Bob", isPc: true);
        bob.PrivilegeLevel = Privilege.Builder;
        ObjectRegistry.AddObject(bob);
        Run(hero, "perm Bob");
        var messages = hero.PeekMessages();
        Assert.Single(messages);
        Assert.Contains(Privilege.Builder.ToString(), messages[0]);
    }

    [Fact]
    public void Perm_Grant_RaisesRank()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeSuper();
        var bob = GameObject.Create("Bob", isPc: true);
        ObjectRegistry.AddObject(bob);
        Assert.Equal(Privilege.Guest, bob.PrivilegeLevel);
        Run(hero, "perm Bob = builder");
        Assert.Equal(Privilege.Builder, bob.PrivilegeLevel);
    }

    [Fact]
    public void Perm_StarAccount_GrantsRank()
    {
        using var env = GlobalTestEnv.Enter();
        var acc = Account.Create("BatchEAcc", "password1");
        Assert.Equal(Privilege.Guest, acc.PrivilegeLevel);
        Run(MakeSuper(), "perm *BatchEAcc = builder");
        Assert.Equal(Privilege.Builder, acc.PrivilegeLevel);
    }

    [Fact]
    public void Perm_StarAccount_Unknown_ReportsNotFound()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeSuper();
        Run(hero, "perm *nosuchacc");
        Assert.Contains(hero.PeekMessages(), m => m.Contains("nosuchacc"));
    }

    [Fact]
    public void Perm_UnknownRank_Refused()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeSuper();
        var bob = GameObject.Create("Bob", isPc: true);
        ObjectRegistry.AddObject(bob);
        Run(hero, "perm Bob = overlord");
        Assert.Single(hero.PeekMessages());
        Assert.Equal(Privilege.Guest, bob.PrivilegeLevel);
    }

    [Fact]
    public void Perm_EqualPrivilege_Refused()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeSuper();
        var crown = GameObject.Create("Crown");
        crown.PrivilegeLevel = Privilege.Admin;
        ObjectRegistry.AddObject(crown);
        Run(hero, "perm Crown = player");
        Assert.Single(hero.PeekMessages());
        Assert.Equal(Privilege.Admin, crown.PrivilegeLevel);
    }

    [Fact]
    public void Wall_ReachesEverySessionIncludingUnlogged()
    {
        using var env = GlobalTestEnv.Enter();
        var prev = ConnectionManager.GlobalInstance;
        var mgr = new ConnectionManager();
        ConnectionManager.GlobalInstance = mgr;
        try
        {
            var accA = Account.Create("batchE-wa", "password1");
            var accB = Account.Create("batchE-wb", "password1");
            var connA = new TestConnection("sess-wa");
            var connB = new TestConnection("sess-wb");
            var connC = new TestConnection("sess-wc");
            Assert.True(mgr.RegisterConnection("sess-wa", connA));
            Assert.True(mgr.RegisterConnection("sess-wb", connB));
            Assert.True(mgr.RegisterConnection("sess-wc", connC));
            var charA = GameObject.Create("Wally", isPc: true);
            ObjectRegistry.AddObject(charA);
            charA.PrivilegeLevel = Privilege.Admin;
            accA.AddCharacter(charA);
            connA.Session.Account = accA;
            connA.Session.Puppet = charA;
            charA.Session = connA.Session;
            connB.Session.Account = accB;
            Run(charA, "wall hear ye");
            Assert.Contains("hear ye", SentText(connA));
            Assert.Contains("hear ye", SentText(connB));
            Assert.Contains("hear ye", SentText(connC));
            Assert.Single(charA.PeekMessages());
        }
        finally { ConnectionManager.GlobalInstance = prev; }
    }

    [Fact]
    public void Wall_Empty_NoBroadcast()
    {
        using var env = GlobalTestEnv.Enter();
        var prev = ConnectionManager.GlobalInstance;
        var mgr = new ConnectionManager();
        ConnectionManager.GlobalInstance = mgr;
        try
        {
            var conn = new TestConnection("sess-w");
            Assert.True(mgr.RegisterConnection("sess-w", conn));
            var hero = MakeSuper();
            Run(hero, "wall");
            Assert.Single(hero.PeekMessages());
            Assert.Empty(conn.Sent);
        }
        finally { ConnectionManager.GlobalInstance = prev; }
    }
}
