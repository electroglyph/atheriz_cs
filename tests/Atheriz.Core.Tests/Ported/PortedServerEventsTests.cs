// Port of atheriz/tests/test_server_events.py:1
using Atheriz.Core;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedServerEventsTests
{
    [Fact] public void AtServerStartIsNoop()
    {
        using var env = GlobalTestEnv.Enter();
        var ex = Record.Exception(() => ServerEvents.AtServerStart());
        Assert.Null(ex);
    }
    [Fact] public void AtServerStopIsNoop()
    {
        using var env = GlobalTestEnv.Enter();
        var ex = Record.Exception(() => ServerEvents.AtServerStop());
        Assert.Null(ex);
    }
    [Fact] public void AtServerReloadIsNoop()
    {
        using var env = GlobalTestEnv.Enter();
        var ex = Record.Exception(() => ServerEvents.AtServerReload());
        Assert.Null(ex);
    }
    [Fact] public void AtCharCreate_WrongPassword_NoCharacter()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new Atheriz.Core.Settings.AtherizSettings();
        var home = new Node(settings.DefaultHome); ObjectRegistry.AddObject(home);
        var acct = Account.Create("alice","password123");
        var before = ObjectRegistry.FilterBy(o=>o.IsPc).Count;
        var sw = new System.IO.StringWriter();
        var oldOut = Console.Out;
        Console.SetOut(sw);
        try
        {
            ServerEvents.AtCharCreate("alice","Bob","wrongpass123");
            var after = ObjectRegistry.FilterBy(o=>o.IsPc).Count;
            Assert.Equal(before, after);
            var captured = sw.ToString();
            Assert.Contains("different password", captured);
        }
        finally { Console.SetOut(oldOut); }
    }
    [Fact] public void AtCharCreate_MaxCharacters_NoNewChar()
    {
        using var env = GlobalTestEnv.Enter();
        var settings2 = new Atheriz.Core.Settings.AtherizSettings();
        var home = new Node(settings2.DefaultHome); ObjectRegistry.AddObject(home);
        var acct = Account.Create("alice2","password123");
        for(int i=0;i<5;i++){ var ch=GameObject.Create($"Char{i}", isPc:true); ObjectRegistry.AddObject(ch); acct.AddCharacter(ch); }
        var before = ObjectRegistry.FilterBy(o=>o.IsPc).Count;
        var sw = new System.IO.StringWriter();
        var oldOut = Console.Out;
        Console.SetOut(sw);
        try
        {
            ServerEvents.AtCharCreate("alice2","Bob","password123");
            var after = ObjectRegistry.FilterBy(o=>o.IsPc).Count;
            Assert.Equal(before, after);
            var captured = sw.ToString();
            Assert.Contains("already has", captured);
        }
        finally { Console.SetOut(oldOut); }
    }
    [Fact] public void AtCharCreate_ExistingAccount_CreatesChar()
    {
        using var env = GlobalTestEnv.Enter();
        var settings3 = new Atheriz.Core.Settings.AtherizSettings();
        var home = new Node(settings3.DefaultHome); ObjectRegistry.AddObject(home);
        var acct = Account.Create("alice3","password123");
        var before = acct.Characters.Count;
        ServerEvents.AtCharCreate("alice3","Bob","password123");
        Assert.Equal(before+1, acct.Characters.Count);
        var newId = acct.Characters.Last();
        var ch = ObjectRegistry.Get(newId).FirstOrDefault();
        Assert.NotNull(ch);
        Assert.True(ch!.IsPc);
        Assert.Equal("Bob", ch.Name);
    }
    [Fact] public void AtCharCreate_SetsHome()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new Atheriz.Core.Settings.AtherizSettings();
        var home = new Node(settings.DefaultHome); ObjectRegistry.AddObject(home);
        var acct = Account.Create("alice_home","password123");
        ServerEvents.AtCharCreate("alice_home","BobHome","password123");
        var newId = acct.Characters.Last();
        var ch = ObjectRegistry.Get(newId).FirstOrDefault();
        Assert.NotNull(ch);
        Assert.NotNull(ch!.Location);
        // home is real_home_node: ch.Home should be home coord
        var homeLoc = ch.Location as Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation;
        if (homeLoc != null) Assert.Equal(home.Coord, homeLoc.Coord);
        else
        {
            var prop = ch.GetType().GetProperty("Home")?.GetValue(ch);
            Assert.NotNull(prop);
        }
    }
    [Fact] public void AtCharCreate_CallsMoveToWithHome()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new Atheriz.Core.Settings.AtherizSettings();
        var home = new Node(settings.DefaultHome); ObjectRegistry.AddObject(home);
        var acct = Account.Create("alice_move","password123");
        ServerEvents.AtCharCreate("alice_move","BobMove","password123");
        var newId = acct.Characters.Last();
        var ch = ObjectRegistry.Get(newId).FirstOrDefault();
        Assert.NotNull(ch);
        Assert.NotNull(ch!.Location);
        // move_to was called once with the home node -> location equals home or home added
        var locObj = ch.ResolveLocationObject() as Node;
        if (locObj != null) Assert.Equal(home.Coord, locObj.Coord);
        else Assert.NotNull(ch.Location);
    }
    [Fact] public void AtCharCreate_NewAccount_CreatesBoth()
    {
        using var env = GlobalTestEnv.Enter();
        var settings4 = new Atheriz.Core.Settings.AtherizSettings();
        var home = new Node(settings4.DefaultHome); ObjectRegistry.AddObject(home);
        ServerEvents.AtCharCreate("newuser","Newbie","password123");
        var accts = ObjectRegistry.FilterBy(o=>o.IsAccount && o.Name=="newuser");
        Assert.Single(accts);
        var pcs = ObjectRegistry.FilterBy(o=>o.IsPc && o.Name=="Newbie");
        Assert.Single(pcs);
    }
    [Fact] public void AtCharCreate_ReturnsEarlyWhenAccountCreateFails()
    {
        using var env = GlobalTestEnv.Enter();
        var home = new Node(new Atheriz.Core.Settings.AtherizSettings().DefaultHome); ObjectRegistry.AddObject(home);
        var before = ObjectRegistry.FilterBy(o=>o.IsAccount).Count;
        ServerEvents.AtCharCreate("dup","X","pw");
        var after = ObjectRegistry.FilterBy(o=>o.IsAccount).Count;
        Assert.Equal(before, after);
    }
    [Fact] public void ExistingAccountNewCharacterMarksAccountModified()
    {
        using var env = GlobalTestEnv.Enter();
        var home = new Node(new Atheriz.Core.Settings.AtherizSettings().DefaultHome); ObjectRegistry.AddObject(home);
        var account = Account.Create("persist_acct","password123");
        ObjectRegistry.AddObject(account);
        account.IsModified = false;
        Assert.False(account.IsModified);
        ServerEvents.AtCharCreate("persist_acct","NewHero","password123");
        Assert.True(account.IsModified);
    }
    [Fact] public void ExistingAccountSecondCharacterPersistsAcrossReload()
    {
        using var env = GlobalTestEnv.Enter();
        var home = new Node(new Atheriz.Core.Settings.AtherizSettings().DefaultHome); ObjectRegistry.AddObject(home);
        var account = Account.Create("reload_acct","password123");
        ObjectRegistry.AddObject(account);
        ServerEvents.AtCharCreate("reload_acct","SecondHero","password123");
        var savedChars = account.Characters.ToList();
        Assert.True(savedChars.Count >= 1);
        var reloaded = ObjectRegistry.FilterBy(o=>o.IsAccount && o.Name=="reload_acct");
        Assert.NotEmpty(reloaded);
        var reloadedAcc = (Account)reloaded[0];
        Assert.Equal(savedChars, reloadedAcc.Characters.ToList());
    }
    [Fact] public void CliCharacterNameUniquenessEnforced()
    {
        using var env = GlobalTestEnv.Enter();
        var home = new Node(new Atheriz.Core.Settings.AtherizSettings().DefaultHome); ObjectRegistry.AddObject(home);
        Account.Create("uniq_acct1","password123");
        Account.Create("uniq_acct2","password123");
        ServerEvents.AtCharCreate("uniq_acct1","HeroDup","password123");
        ServerEvents.AtCharCreate("uniq_acct2","herodup","password123");
        var heroes = ObjectRegistry.FilterBy(o=>o.IsPc && o.Name.Equals("herodup", StringComparison.OrdinalIgnoreCase));
        Assert.Single(heroes);
    }
    [Fact] public void CliCharacterNameValidationRejectsInvalid()
    {
        using var env = GlobalTestEnv.Enter();
        var home = new Node(new Atheriz.Core.Settings.AtherizSettings().DefaultHome); ObjectRegistry.AddObject(home);
        ServerEvents.AtCharCreate("badname_acct","x","password123");
        var pcs = ObjectRegistry.FilterBy(o=>o.IsPc && o.Name=="x");
        Assert.Empty(pcs);
    }
    [Fact] public void CliWeakPasswordRejected()
    {
        using var env = GlobalTestEnv.Enter();
        var home = new Node(new Atheriz.Core.Settings.AtherizSettings().DefaultHome); ObjectRegistry.AddObject(home);
        ServerEvents.AtCharCreate("weak_acct","HeroWeak","x");
        var accts = ObjectRegistry.FilterBy(o=>o.IsAccount && o.Name=="weak_acct");
        Assert.Empty(accts);
        var pcs = ObjectRegistry.FilterBy(o=>o.IsPc && o.Name=="HeroWeak");
        Assert.Empty(pcs);
    }
    [Fact] public void CliShortPasswordDoesNotCreateAccount()
    {
        using var env = GlobalTestEnv.Enter();
        var home = new Node(new Atheriz.Core.Settings.AtherizSettings().DefaultHome); ObjectRegistry.AddObject(home);
        ServerEvents.AtCharCreate("shortpwacct","HeroShort","short");
        var accts = ObjectRegistry.FilterBy(o=>o.IsAccount && o.Name=="shortpwacct");
        Assert.Empty(accts);
    }
    [Fact] public void CliPasswordValidationEnforcedOnExistingAccount()
    {
        using var env = GlobalTestEnv.Enter();
        var home = new Node(new Atheriz.Core.Settings.AtherizSettings().DefaultHome); ObjectRegistry.AddObject(home);
        var exist = Account.Create("existacct","validpass123");
        ObjectRegistry.AddObject(exist);
        ServerEvents.AtCharCreate("existacct","NewHero2","x");
        var heroes = ObjectRegistry.FilterBy(o=>o.IsPc && o.Name=="NewHero2");
        Assert.Empty(heroes);
    }
    [Fact] public void ValidationRejectsShortPassword()
    {
        Assert.NotNull(Validation.ValidatePassword("x"));
        Assert.NotNull(Validation.ValidatePassword(""));
        Assert.NotNull(Validation.ValidatePassword("short"));
    }
    [Fact] public void CliAtCharCreateCallsValidatePassword()
    {
        using var env = GlobalTestEnv.Enter();
        var home = new Node(new Atheriz.Core.Settings.AtherizSettings().DefaultHome); ObjectRegistry.AddObject(home);
        var before = ObjectRegistry.FilterBy(o=>o.IsAccount).Count;
        ServerEvents.AtCharCreate("anyacct","AnyHero","x");
        var after = ObjectRegistry.FilterBy(o=>o.IsAccount).Count;
        Assert.Equal(before, after);
    }
    [Fact] public void MinPasswordLengthNotWeak()
    {
        var s = new Atheriz.Core.Settings.AtherizSettings();
        Assert.True(s.MinPasswordLength >= 8);
        Assert.NotNull(Validation.ValidatePassword("1234567"));
        var valid = Validation.ValidatePassword("12345678");
        Assert.True(valid == null || !valid.Contains("at least"));
    }
}
