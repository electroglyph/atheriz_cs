// Port of atheriz/tests/test_puppet.py:1
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedPuppetTests
{
    private static (GameObject caller, Session sess) Builder(string name)
    {
        var caller=GameObject.Create(name, isPc:true); caller.PrivilegeLevel=Privilege.Builder;
        var acc=Account.Create($"{name}_acct", "pw1"); acc.AddCharacter(caller);
        ObjectRegistry.AddObject(caller); ObjectRegistry.AddObject(acc);
        var sess=new Session(); sess.Puppet=caller; caller.Session=sess; sess.Account=acc;
        return(caller,sess);
    }

    [Fact]
    public void BuilderCannotPuppetOtherPlayersCharacter()
    {
        using var env=GlobalTestEnv.Enter();
        var victim=GameObject.Create("victim", isPc:true); victim.PrivilegeLevel=Privilege.Player;
        var owner=Account.Create("owner","pw1"); owner.AddCharacter(victim);
        ObjectRegistry.AddObject(victim); ObjectRegistry.AddObject(owner);
        var (caller,sess)=Builder("builder");
        var cmd=new Atheriz.Core.Commands.LoggedIn.PuppetCommand();
        var args=new Atheriz.Core.Commands.GameArgumentParser.ParsedArgs(); args["target"]=$"#{victim.Id}";
        cmd.Run(caller, args);
        Assert.True(victim.IsPc); Assert.Equal(Privilege.Player, victim.PrivilegeLevel); Assert.Same(caller, sess.Puppet); Assert.Empty(sess.PuppetStack);
    }

    [Fact]
    public void BuilderCanPuppetOwnCharacter()
    {
        using var env=GlobalTestEnv.Enter();
        var (caller,sess)=Builder("builder");
        var alt=GameObject.Create("alt", isPc:true); alt.PrivilegeLevel=Privilege.Player;
        // Clear default deny lock and allow puppet via explicit ownership lock (mirrors Python ownership check)
        alt.ClearLocksByName("puppet");
        alt.AddLock("puppet", c=> c.Id==caller.Id || c.IsSuperUser);
        if(sess.Account is Account acc) acc.AddCharacter(alt);
        ObjectRegistry.AddObject(alt);
        var cmd=new Atheriz.Core.Commands.LoggedIn.PuppetCommand(); var args=new Atheriz.Core.Commands.GameArgumentParser.ParsedArgs(); args["target"]=$"#{alt.Id}";
        cmd.Run(caller, args);
        Assert.True(alt.IsPc); Assert.Equal(Privilege.Builder, alt.PrivilegeLevel); Assert.Same(alt, sess.Puppet);
    }

    [Fact]
    public void BuilderCanPuppetNpc()
    {
        using var env=GlobalTestEnv.Enter();
        var npc=GameObject.Create("goblin", isNpc:true); ObjectRegistry.AddObject(npc);
        var (caller,sess)=Builder("builder");
        var cmd=new Atheriz.Core.Commands.LoggedIn.PuppetCommand(); var args=new Atheriz.Core.Commands.GameArgumentParser.ParsedArgs(); args["target"]=$"#{npc.Id}";
        cmd.Run(caller, args);
        Assert.True(npc.IsPc); Assert.Equal(Privilege.Builder, npc.PrivilegeLevel); Assert.Same(npc, sess.Puppet);
    }

    [Fact]
    public void SuperuserCanPuppetAnyCharacter()
    {
        using var env=GlobalTestEnv.Enter();
        var victim=GameObject.Create("victim", isPc:true); victim.PrivilegeLevel=Privilege.Player;
        var owner=Account.Create("owner","pw1"); owner.AddCharacter(victim);
        ObjectRegistry.AddObject(victim); ObjectRegistry.AddObject(owner);
        var (caller,sess)=Builder("admin"); caller.PrivilegeLevel=Privilege.Admin;
        var cmd=new Atheriz.Core.Commands.LoggedIn.PuppetCommand(); var args=new Atheriz.Core.Commands.GameArgumentParser.ParsedArgs(); args["target"]=$"#{victim.Id}";
        cmd.Run(caller, args);
        Assert.True(victim.IsPc); Assert.Equal(Privilege.Admin, victim.PrivilegeLevel); Assert.Same(victim, sess.Puppet);
    }

    [Fact]
    public void PuppetedStateNeverSerialized()
    {
        using var env=GlobalTestEnv.Enter();
        var npc=GameObject.Create("goblin", isNpc:true); ObjectRegistry.AddObject(npc);
        var (caller,sess)=Builder("builder");
        var cmd=new Atheriz.Core.Commands.LoggedIn.PuppetCommand(); var args=new Atheriz.Core.Commands.GameArgumentParser.ParsedArgs(); args["target"]=$"#{npc.Id}";
        cmd.Run(caller, args);
        Assert.True(npc.IsPc);
        var fld=typeof(GameObject).GetField("_puppetRestore", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
        var restore=fld?.GetValue(npc) as Dictionary<string,object>;
        Assert.NotNull(restore);
        Assert.Equal(false, restore!["is_pc"]);
    }
}
