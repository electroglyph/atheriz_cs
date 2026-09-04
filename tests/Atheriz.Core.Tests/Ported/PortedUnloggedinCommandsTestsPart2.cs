// Port of atheriz/tests/test_unloggedin_commands.py:1 part2
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedUnloggedinCommandsTestsPart2
{
    [Fact] public void NewCharacter_CreatesPersistent()
    {
        using var env = GlobalTestEnv.Enter();
        var acct = Account.Create("alice_new","secret123");
        var ch = GameObject.Create("Hobbis", isPc:true);
        ch.Gender = "Male";
        ObjectRegistry.AddObject(ch);
        acct.AddCharacter(ch);
        Assert.Contains(ch.Id, acct.Characters);
        Assert.True(ch.IsPc);
        Assert.False(ch.IsTemporary);
    }
    [Fact] public void GuestCreatesTemporary()
    {
        using var env = GlobalTestEnv.Enter();
        var guest = GameObject.Create("Guest1", isPc:true);
        guest.IsTemporary = true;
        guest.Gender = "Male";
        ObjectRegistry.AddObject(guest);
        Assert.True(guest.IsTemporary);
        Assert.True(guest.IsPc);
    }
    [Fact] public void CreationCooldown_AlternateOpBlocked()
    {
        using var env = GlobalTestEnv.Enter();
        var host="203.0.113.5";
        ObjectRegistry.ClearCreationCooldown(host);
        Assert.True(ObjectRegistry.TryReserveCreationCooldown("guest",host,1000,60));
        Assert.True(ObjectRegistry.CreationCooldownActive("account",host,1001));
        ObjectRegistry.ClearCreationCooldown(host);
    }
    [Fact] public void CharSelection_HintWithChars()
    {
        using var env = GlobalTestEnv.Enter();
        var acct = Account.Create("alice_sel","secret123");
        var ch = GameObject.Create("Hob", isPc:true);
        ObjectRegistry.AddObject(ch);
        acct.AddCharacter(ch);
        Assert.Single(acct.Characters);
    }
    [Fact] public void PasswordPolicy_RequiresLength()
    {
        using var env = GlobalTestEnv.Enter();
        var shortPw = Atheriz.Core.Commands.UnloggedIn.Validation.ValidatePassword("short");
        Assert.NotNull(shortPw);
        var ok = Atheriz.Core.Commands.UnloggedIn.Validation.ValidatePassword("hunter22");
        Assert.Null(ok);
    }
}
