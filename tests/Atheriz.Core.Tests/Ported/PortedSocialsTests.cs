// Port of atheriz/tests/test_socials.py:1
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;

using Atheriz.Core.Commands;
namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedSocialsTests
{
    private sealed class TestCaller : GameObject
    {
        public Func<string, List<GameObject>>? SearchOverride;
        public override List<GameObject> Search(string query, bool recursive = true, GameObject? looker = null)
        {
            if (SearchOverride != null) return SearchOverride(query);
            return base.Search(query, recursive, looker);
        }
    }
    private static (Node room, GameObject alice, GameObject bob) MakeEnv()
    {
        var coord = new Coord("TestAreaSocials", 0, 0, 0);
        var room = new Node(coord, desc: "room");
        var alice = GameObject.Create("Alice", isPc: true);
        var bob = GameObject.Create("Bob", isPc: true);
        alice.IsConnected=true; bob.IsConnected=true;
        ObjectRegistry.AddObject(room); ObjectRegistry.AddObject(alice); ObjectRegistry.AddObject(bob);
        alice.Location = new LocationRef.CoordLocation(coord);
        bob.Location = new LocationRef.CoordLocation(coord);
        room.AddObject(alice); room.AddObject(bob);
        alice.ClearMessages(); bob.ClearMessages();
        return (room, alice, bob);
    }
    private static GameObject MakeCaller(string name="Alice") => PortedHelpers.MakeCaller(name);

    [Fact] public void UntargetedSocial()
    {
        using var env=GlobalTestEnv.Enter();
        var (room, alice, bob)=MakeEnv();
        var cmd=new SocialsCommand();
        var pa=new Atheriz.Core.Commands.GameArgumentParser.ParsedArgs(); pa.CmdString="smile"; pa["target"]=new List<string>();
        cmd.Run(alice, pa);
        var aliceText=string.Join(" ", alice.PeekMessages());
        Assert.Contains("You smile", aliceText);
        var bobText=string.Join(" ", bob.PeekMessages());
        Assert.Contains("Alice", bobText); Assert.Contains("smiles", bobText.ToLowerInvariant());
    }
    [Fact] public void TargetedSocial()
    {
        using var env=GlobalTestEnv.Enter();
        var (room, alice, bob)=MakeEnv();
        var cmd=new SocialsCommand();
        alice.ClearMessages(); bob.ClearMessages();
        var pa=new Atheriz.Core.Commands.GameArgumentParser.ParsedArgs(); pa.CmdString="hug"; pa["target"]=new List<string>{"Bob"};
        cmd.Run(alice, pa);
        var txt=string.Join(" ", alice.PeekMessages());
        Assert.Contains("You hug", txt); Assert.Contains("Bob", txt);
        var bobTxt=string.Join(" ", bob.PeekMessages());
        Assert.Contains("Alice", bobTxt); Assert.Contains("hugs you", bobTxt.ToLowerInvariant());
    }
    [Fact] public void TargetedMultipleMatchesError()
    {
        using var env=GlobalTestEnv.Enter();
        var (room, alice, bob)=MakeEnv();
        var caller=new TestCaller(); caller.Name="Alice"; caller.Id=999; caller.IsConnected=true; caller.Location=alice.Location; room.AddObject(caller); ObjectRegistry.AddObject(caller);
        caller.SearchOverride = q => new List<GameObject>{bob, GameObject.Create("Alice", isPc:true)};
        var cmd=new SocialsCommand();
        var pa=new Atheriz.Core.Commands.GameArgumentParser.ParsedArgs(); pa.CmdString="hug"; pa["target"]=new List<string>{"Bob"};
        cmd.Run(caller, pa);
        var all=string.Join(" ", caller.PeekMessages()).ToLowerInvariant();
        Assert.Contains("multiple", all);
        Assert.Empty(bob.PeekMessages());
    }
    [Fact] public void MissingTarget()
    {
        using var env=GlobalTestEnv.Enter();
        var (room, alice, bob)=MakeEnv();
        var cmd=new SocialsCommand();
        alice.ClearMessages(); bob.ClearMessages();
        var pa=new Atheriz.Core.Commands.GameArgumentParser.ParsedArgs(); pa.CmdString="hug"; pa["target"]=new List<string>{"Charlie"};
        cmd.Run(alice, pa);
        var txt=string.Join(" ", alice.PeekMessages());
        Assert.Contains("Could not find", txt);
        Assert.Empty(bob.PeekMessages());
    }
    [Fact] public void SocialsListsAliases()
    {
        using var env=GlobalTestEnv.Enter();
        var c=GameObject.Create("Alice"); ObjectRegistry.AddObject(c); c.ClearMessages();
        var cmd=new SocialsCommand();
        var pa=new Atheriz.Core.Commands.GameArgumentParser.ParsedArgs(); pa.CmdString="socials"; pa["target"]=new List<string>();
        cmd.Run(c, pa);
        var txt=string.Join(" ", c.PeekMessages());
        Assert.Contains("smile", txt.ToLowerInvariant()); Assert.Contains("hug", txt.ToLowerInvariant());
    }
    [Fact] public void UnknownCmdstringUsesInvocationMsg()
    {
        using var env=GlobalTestEnv.Enter();
        var c=new TestCaller(); c.Name="Alice"; c.Location=new LocationRef.CoordLocation(new Coord("test",0,0,0));
        var node=new Node(new Coord("test",0,0,0)); node.AddObject(c); ObjectRegistry.AddObject(c); ObjectRegistry.AddObject(node);
        var cmd=new SocialsCommand();
        var pa=new Atheriz.Core.Commands.GameArgumentParser.ParsedArgs(); pa.CmdString="laugh"; pa["target"]=new List<string>();
        cmd.Run(c, pa);
        // laugh is known social, so it should trigger msg_contents, not unknown
        // unknown like "foo" should use invocation msg
        var c2=new TestCaller(); c2.Name="Alice"; ObjectRegistry.AddObject(c2);
        var pa2=new Atheriz.Core.Commands.GameArgumentParser.ParsedArgs(); pa2.CmdString="notasocial"; pa2["target"]=new List<string>();
        cmd.Run(c2, pa2);
        var txt=string.Join(" ", c2.PeekMessages());
        Assert.Contains("aliases", txt.ToLowerInvariant());
    }
    [Fact] public void AllSocialsHaveTwoTemplates()
    {
        using var env=GlobalTestEnv.Enter();
        foreach(var kv in SocialsCommand.SocialsDict){
            Assert.True(kv.Value.self.Contains("$You") || kv.Value.self.Contains("$you"));
            Assert.True(kv.Value.target.Contains("$You") || kv.Value.target.Contains("$you"));
        }
    }
    [Fact] public void TargetedTemplateUsed()
    {
        using var env=GlobalTestEnv.Enter();
        var coord=new Coord("test",0,0,0);
        var node=new Node(coord); var caller=GameObject.Create("Alice", isPc:true); caller.IsConnected=true; caller.Location=new LocationRef.CoordLocation(coord); node.AddObject(caller); ObjectRegistry.AddObject(caller); ObjectRegistry.AddObject(node);
        var target=GameObject.Create("Bob", isPc:true); target.IsConnected=true; ObjectRegistry.AddObject(target); target.Location=new LocationRef.CoordLocation(coord); node.AddObject(target);
        var cmd=new SocialsCommand();
        var pa=new Atheriz.Core.Commands.GameArgumentParser.ParsedArgs(); pa.CmdString="wave"; pa["target"]=new List<string>{"Bob"};
        caller.ClearMessages(); target.ClearMessages();
        cmd.Run(caller, pa);
        var callerMsg=string.Join(" ", caller.PeekMessages());
        var targetMsg=string.Join(" ", target.PeekMessages());
        Assert.True(callerMsg.ToLowerInvariant().Contains("wave") || targetMsg.ToLowerInvariant().Contains("wave"));
    }
    [Fact] public void SocialMultipleTargetsShouldError()
    {
        using var env=GlobalTestEnv.Enter();
        var coord=new Coord("test",0,0,0); var node=new Node(coord); var caller=new TestCaller(); caller.Name="Alice"; caller.IsConnected=true; caller.Location=new LocationRef.CoordLocation(coord); node.AddObject(caller); ObjectRegistry.AddObject(caller); ObjectRegistry.AddObject(node);
        var b1=GameObject.Create("Bob", isPc:true); b1.IsConnected=true; var b2=GameObject.Create("Bob", isPc:true); b2.IsConnected=true; ObjectRegistry.AddObject(b1); ObjectRegistry.AddObject(b2);
        caller.SearchOverride = q => new List<GameObject>{b1,b2};
        var cmd=new SocialsCommand();
        var pa=new Atheriz.Core.Commands.GameArgumentParser.ParsedArgs(); pa.CmdString="hug"; pa["target"]=new List<string>{"Bob"};
        caller.ClearMessages();
        cmd.Run(caller, pa);
        var all=string.Join(" ", caller.PeekMessages()).ToLowerInvariant();
        Assert.Contains("multiple", all);
    }
    [Fact] public void Social_WithMultipleMatchingTargetsShouldError()
    {
        using var env=GlobalTestEnv.Enter();
        var coord=new Coord("test2",0,0,0); var node=new Node(coord); var caller=new TestCaller(); caller.Name="Alice"; caller.IsConnected=true; caller.Location=new LocationRef.CoordLocation(coord); node.AddObject(caller); ObjectRegistry.AddObject(caller); ObjectRegistry.AddObject(node);
        var bob1=GameObject.Create("Bob", isPc:true); var bob2=GameObject.Create("Bob", isPc:true); ObjectRegistry.AddObject(bob1); ObjectRegistry.AddObject(bob2);
        caller.SearchOverride = q => new List<GameObject>{bob1,bob2};
        var cmd=new SocialsCommand();
        var pa=new GameArgumentParser.ParsedArgs(); pa.CmdString="hug"; pa["target"]=new List<string>{"Bob"};
        caller.ClearMessages();
        cmd.Run(caller, pa);
        var all=string.Join(" ", caller.PeekMessages()).ToLowerInvariant();
        Assert.Contains("multiple", all);
        Assert.Empty(bob1.PeekMessages());
        Assert.Empty(bob2.PeekMessages());
    }
    [Fact] public void Social_AmbiguousDoesNotHugFirst()
    {
        using var env=GlobalTestEnv.Enter();
        var coord=new Coord("test3",0,0,0); var node=new Node(coord); var caller=new TestCaller(); caller.Name="Alice"; caller.IsConnected=true; caller.Location=new LocationRef.CoordLocation(coord); node.AddObject(caller); ObjectRegistry.AddObject(caller); ObjectRegistry.AddObject(node);
        var a=GameObject.Create("Alex", isPc:true); var b=GameObject.Create("Alex", isPc:true); ObjectRegistry.AddObject(a); ObjectRegistry.AddObject(b);
        caller.SearchOverride = q => new List<GameObject>{a,b};
        var cmd=new SocialsCommand();
        var pa=new GameArgumentParser.ParsedArgs(); pa.CmdString="smile"; pa["target"]=new List<string>{"Alex"};
        caller.ClearMessages(); a.ClearMessages(); b.ClearMessages();
        cmd.Run(caller, pa);
        var all=string.Join(" ", caller.PeekMessages()).ToLowerInvariant();
        Assert.Contains("multiple", all);
        Assert.Empty(a.PeekMessages());
        Assert.Empty(b.PeekMessages());
    }
}
