using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Commands;

// Batch F of the commands.md rollout: chardelete, accounts, reset.
[Collection("Ported")]
public class BatchFCommandsTests
{
    private static void Run(GameObject caller, string text)
    {
        var job = CommandDispatcher.DispatchLoggedIn(caller, text, immediate: true);
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);
    }

    private static (Account Acc, GameObject First, GameObject Second) MakeFamily(string accName)
    {
        var acc = Account.Create(accName, "password1");
        var first = GameObject.Create(accName + "-one", isPc: true);
        var second = GameObject.Create(accName + "-two", isPc: true);
        ObjectRegistry.AddObject(first);
        ObjectRegistry.AddObject(second);
        acc.AddCharacter(first);
        acc.AddCharacter(second);
        first.Session = new Session(account: acc);
        return (acc, first, second);
    }

    [Fact]
    public void Chardelete_FirstCall_WarnsOnly()
    {
        using var env = GlobalTestEnv.Enter();
        var (acc, first, second) = MakeFamily("batchF-fam1");
        Run(first, $"chardelete {second.Name}");
        Assert.Single(first.PeekMessages());
        Assert.False(second.IsDeleted);
        Assert.Contains(second.Id, acc.Characters);
    }

    [Fact]
    public void Chardelete_Confirm_Deletes()
    {
        using var env = GlobalTestEnv.Enter();
        var (acc, first, second) = MakeFamily("batchF-fam2");
        Run(first, $"chardelete --confirm {second.Name}");
        Assert.True(second.IsDeleted);
        Assert.DoesNotContain(second.Id, acc.Characters);
        Assert.Empty(ObjectRegistry.FilterBy(o => o.Id == second.Id));
    }

    [Fact]
    public void Chardelete_TrailingConfirm_Deletes()
    {
        using var env = GlobalTestEnv.Enter();
        var (acc, first, second) = MakeFamily("batchF-fam5");
        Run(first, $"chardelete {second.Name} --confirm");
        Assert.True(second.IsDeleted);
        Assert.DoesNotContain(second.Id, acc.Characters);
    }

    [Fact]
    public void Chardelete_Unknown_Answered()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, first, _) = MakeFamily("batchF-fam3");
        Run(first, "chardelete --confirm zzz");
        Assert.Single(first.PeekMessages());
    }

    [Fact]
    public void Chardelete_CurrentPuppet_Refused()
    {
        using var env = GlobalTestEnv.Enter();
        var (acc, first, _) = MakeFamily("batchF-fam4");
        Run(first, $"chardelete --confirm {first.Name}");
        Assert.Single(first.PeekMessages());
        Assert.False(first.IsDeleted);
        Assert.Contains(first.Id, acc.Characters);
    }

    [Fact]
    public void Chardelete_NoAccount_Answered()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = PortedHelpers.MakeCaller("NoAcct");
        Run(hero, "chardelete --confirm Bob");
        Assert.Single(hero.PeekMessages());
    }

    [Fact]
    public void Accounts_Stats_ReportsTotals()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = PortedHelpers.MakeCaller("BatchFSuper", Privilege.Admin);
        Account.Create("batchF-stat1", "password1");
        Account.Create("batchF-stat2", "password1");
        int expected = ObjectRegistry.FilterBy(o => o.IsAccount).Count;
        Run(hero, "accounts");
        var messages = hero.PeekMessages();
        Assert.Single(messages);
        Assert.Contains($"Accounts: {expected}", messages[0]);
    }

    [Fact]
    public void Accounts_Delete_RemovesAccountCharsAndKicks()
    {
        using var env = GlobalTestEnv.Enter();
        var prev = ConnectionManager.GlobalInstance;
        var mgr = new ConnectionManager();
        ConnectionManager.GlobalInstance = mgr;
        try
        {
            var acc = Account.Create("batchF-doom", "password1");
            var pc = GameObject.Create("Doomy", isPc: true);
            ObjectRegistry.AddObject(pc);
            acc.AddCharacter(pc);
            var conn = new TestConnection("sess-doom");
            Assert.True(mgr.RegisterConnection("sess-doom", conn));
            conn.Session.Account = acc;
            conn.Session.Puppet = pc;
            var hero = PortedHelpers.MakeCaller("BatchFSuper", Privilege.Admin);
            Run(hero, "accounts --delete batchF-doom");
            Assert.True(conn.Closed);
            Assert.True(acc.IsDeleted);
            Assert.True(pc.IsDeleted);
            Assert.Empty(ObjectRegistry.FilterBy(o => o is Account a && a.Name == "batchF-doom"));
        }
        finally { ConnectionManager.GlobalInstance = prev; }
    }

    [Fact]
    public void Accounts_DeleteUnknown_Answered()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = PortedHelpers.MakeCaller("BatchFSuper", Privilege.Admin);
        Run(hero, "accounts --delete zzz");
        Assert.Single(hero.PeekMessages());
    }

    [Fact]
    public void Reset_CompletesAndReports()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = PortedHelpers.MakeCaller("BatchFSuper", Privilege.Admin);
        Run(hero, "reset");
        var messages = hero.PeekMessages();
        Assert.True(messages.Count >= 2);
        Assert.Contains(messages, m => m.Contains("Reset complete"));
    }
}
