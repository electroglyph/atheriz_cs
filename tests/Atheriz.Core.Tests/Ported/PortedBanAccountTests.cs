// Port of atheriz/tests/test_ban_account.py:1
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedBanAccountTests
{
    private static GameObject MakeCaller(Atheriz.Core.Privilege p = Atheriz.Core.Privilege.Builder) => PortedHelpers.MakeCaller(p);
    private static GameObject MakePc(string name, Atheriz.Core.Privilege p = Atheriz.Core.Privilege.Player)
    {
        var pc = GameObject.Create(name, isPc: true); ObjectRegistry.AddObject(pc);
        pc.PrivilegeLevel = p;
        pc.ClearMessages();
        return pc;
    }
    private static FakeConnection Attach(GameObject pc, string host="1.2.3.4", Account? acct=null)
    {
        var conn = new FakeConnection($"conn-{pc.Id}");
        conn.ClientHost = host;
        pc.Session = conn.Session;
        conn.Session.Puppet = pc;
        conn.Session.Connection = conn;
        if (acct != null) { conn.Session.Account = acct; conn.Session.AccountId = acct.Id; }
        return conn;
    }
    [Fact] public void BanAccountDisconnectsAllOnlineCharacters()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var acct = Account.Create("acct", "pass12345");
        var charA = MakePc("Alice");
        var charB = MakePc("Bob");
        acct.AddCharacter(charA);
        acct.AddCharacter(charB);
        var connA = Attach(charA, "10.0.0.1", acct);
        var connB = Attach(charB, "10.0.0.2", acct);
        var cmd = new BanCommand();
        cmd.Run(caller, cmd.Parser!.ParseArgs(new[] { "Alice", "--account" }));
        Assert.True(connA.Closed, "named target was not kicked");
        Assert.True(connB.Closed, "online sibling stayed connected after the account ban");
    }
    [Fact] public void BanAccountKicksOnlineSiblingWhenNamedOffline()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var acct = Account.Create("acct2", "pass12345");
        var charA = MakePc("Carol");
        var charB = MakePc("Dave");
        acct.AddCharacter(charA);
        acct.AddCharacter(charB);
        var connB = Attach(charB, "10.0.0.3", acct);
        // _find_account should resolve via scan
        var found = BanCommandFindAccount(charA);
        Assert.Same(acct, found);
        var cmd = new BanCommand();
        cmd.Run(caller, cmd.Parser!.ParseArgs(new[] { "Carol", "--account" }));
        Assert.True(connB.Closed, "online sibling stayed connected after account ban");
    }
    private static GameObject? BanCommandFindAccount(GameObject target)
    {
        // mirrors BanCommand.FindAccount private — replicate lookup logic
        var sess = target.Session;
        var acct = sess?.Account as GameObject;
        if (acct != null) return acct;
        var accounts = ObjectRegistry.FilterBy(o => o.IsAccount && (o as Account)?.Characters.Contains(target.Id) == true);
        return accounts.FirstOrDefault();
    }
}
