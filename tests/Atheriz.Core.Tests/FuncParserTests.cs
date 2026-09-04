using Atheriz.Core.Objects;
using Atheriz.Core.Objects.VerbConjugation;

namespace Atheriz.Core.Tests;

public class FuncParserTests
{
    [Fact]
    public void You_Conj_Director()
    {
        var hero = GameObject.Create("Hero"); hero.Gender="male";
        var villain = GameObject.Create("Villain"); villain.Gender="female";
        // actor self -> you
        Assert.Equal("you", FuncParser.Parse("$you()", hero, hero, null, false));
        Assert.Equal("Hero", FuncParser.Parse("$you()", hero, villain, null, false));
        Assert.Equal("you jump", FuncParser.Parse("$you() $conj(jump)", hero, hero, null, false));
        Assert.Equal("Hero jumps", FuncParser.Parse("$you() $conj(jump)", hero, villain, null, false));
        // director
        var map = new Dictionary<string, object?> { ["attacker"]=hero, ["defender"]=villain };
        Assert.Equal("Hero attacks Villain.", FuncParser.Parse("{attacker} attacks {defender}.", hero, villain, map, false));
    }

    [Fact]
    public void Pronoun_And_Pconj()
    {
        var hero = GameObject.Create("Hero"); hero.Gender="male";
        var villain = GameObject.Create("Villain"); villain.Gender="female";
        var plural = GameObject.Create("Them"); plural.Gender="plural";
        // pronoun
        Assert.Equal("I", FuncParser.Parse("$pron(I, m)", hero, hero, new Dictionary<string,object?>{{"you",hero}}, false));
        Assert.Equal("he", FuncParser.Parse("$pron(I, m)", hero, villain, new Dictionary<string,object?>{{"you",hero}}, false));
        Assert.Equal("them", FuncParser.Parse("$pron(you,op,p)", hero, villain, new Dictionary<string,object?>{{"you",plural}}, false));
        // pconj plural
        Assert.Equal("they jump", FuncParser.Parse("$pron(you) $pconj(jump)", plural, villain, new Dictionary<string,object?>{{"you",plural}}, false));
        Assert.Equal("you jump", FuncParser.Parse("$pron(you) $pconj(jump)", plural, plural, new Dictionary<string,object?>{{"you",plural}}, false));
    }

    [Fact]
    public void MsgContents_ActorStance()
    {
        using var _env = GlobalTestEnv.Enter();
        var room = GameObject.Create("room"); room.IsContainer=true;
        var hero = GameObject.Create("Hero"); hero.Gender="male";
        var villain = GameObject.Create("Villain"); villain.Gender="female";
        room.AddContent(hero.Id); room.AddContent(villain.Id);
        Globals.ObjectRegistry.AddObject(hero);
        Globals.ObjectRegistry.AddObject(villain);
        Globals.ObjectRegistry.AddObject(room);
        hero.ClearMessages(); villain.ClearMessages();
        room.MsgContents("$You() $conj(attack) $you(defender).", fromObj: hero, mapping: new Dictionary<string,object?>{{"defender", villain}});
        Assert.Contains("You attack", hero.PeekMessages().Last());
        Assert.Contains("Hero attacks you", villain.PeekMessages().Last());
    }

    [Fact]
    public void Escape_And_Unknown()
    {
        var hero = GameObject.Create("Hero");
        Assert.Equal("$you()", FuncParser.Parse("\\$you()", hero, hero, null, false));
        Assert.Equal("$you()", FuncParser.Parse("$$you()", hero, hero, null, false));
        Assert.Equal("$unknown()", FuncParser.Parse("$unknown()", hero, hero, null, false));
        Assert.Throws<FuncParser.ParsingError>(() => FuncParser.Parse("$unknown()", hero, hero, null, true));
        Assert.Equal("$you(", FuncParser.Parse("$you(", hero, hero, null, false));
    }

    [Fact]
    public void Conjugate_Stance()
    {
        // be: you are / he is
        var (you, them) = Conjugate.VerbActorStanceComponents("is");
        Assert.Equal("are", you);
        Assert.Equal("is", them);
        // jump: you jump / Celia jumps
        var (you2, them2) = Conjugate.VerbActorStanceComponents("jump");
        Assert.Equal("jump", you2);
        Assert.Equal("jumps", them2);
    }
}
