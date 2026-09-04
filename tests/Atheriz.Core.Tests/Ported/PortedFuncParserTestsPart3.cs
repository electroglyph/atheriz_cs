// Port of atheriz/tests/test_at_hear_replace.py:1 + remaining funcparser integration
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedFuncParserTestsPart3
{
    private static (GameObject listener, GameObject emitter, Node loc) MakePair()
    {
        var coord=new Coord("test",0,0,0);
        var loc=new Node(coord, desc:"room");
        var listener=GameObject.Create("Listener", isPc:true);
        var emitter=GameObject.Create("Emitter", isPc:false);
        listener.IsConnected=true; emitter.IsConnected=true;
        listener.CanHear=true;
        ObjectRegistry.AddObject(loc); ObjectRegistry.AddObject(listener); ObjectRegistry.AddObject(emitter);
        listener.Location=new LocationRef.CoordLocation(coord);
        emitter.Location=new LocationRef.CoordLocation(coord);
        loc.AddObject(listener); loc.AddObject(emitter);
        listener.ClearMessages(); emitter.ClearMessages();
        return (listener, emitter, loc);
    }

    [Fact] public void AtHearReplaceLogic()
    {
        using var env=GlobalTestEnv.Enter();
        var (listener, emitter, loc)=MakePair();
        var msg="one two three four five six seven eight nine ten";
        listener.ClearMessages();
        listener.AtHear(emitter, "Someone says,", $" \"{msg}\"", 5, isSay:true);
        var txt=string.Join(" ", listener.PeekMessages());
        Assert.Contains("nearly inaudible", txt.ToLowerInvariant());
        Assert.Contains("...", txt);
    }
    [Fact] public void AtHearThresholds()
    {
        using var env=GlobalTestEnv.Enter();
        var (listener, emitter, loc)=MakePair();
        var msg=string.Join(" ", Enumerable.Repeat("word",50));
        listener.ClearMessages();
        listener.AtHear(emitter, "Someone says,", $" \"{msg}\"", 55, isSay:true);
        var txt=string.Join(" ", listener.PeekMessages());
        Assert.DoesNotContain("...", txt);
        Assert.Contains(msg.Split(' ')[0], txt);
        listener.ClearMessages();
        listener.AtHear(emitter, "Someone says,", $" \"{msg}\"", 0.5, isSay:true);
        var txt2=string.Join(" ", listener.PeekMessages());
        Assert.Contains("...", txt2);
        Assert.True(txt2.Split(new[]{"..."}, StringSplitOptions.None).Length-1 > 30);
    }
    [Fact] public void AtHearIsSayFalse()
    {
        using var env=GlobalTestEnv.Enter();
        var (listener, emitter, loc)=MakePair();
        listener.ClearMessages();
        listener.AtHear(emitter, "A crash", " (very loud message)", 5, isSay:false);
        var txt=string.Join(" ", listener.PeekMessages());
        Assert.Contains("nearly inaudible", txt.ToLowerInvariant());
        Assert.Contains("(very loud message)", txt);
        Assert.DoesNotContain("...", txt);
    }
    // Additional integration: msg_contents via parser
    [Fact] public void MsgContentsUsesReturnStr()
    {
        using var env=GlobalTestEnv.Enter();
        var coord=new Coord("test",0,0,0);
        var room=new Node(coord);
        var a=GameObject.Create("Alice", isPc:true); a.IsConnected=true; a.Location=new LocationRef.CoordLocation(coord);
        var b=GameObject.Create("Bob", isPc:true); b.IsConnected=true; b.Location=new LocationRef.CoordLocation(coord);
        ObjectRegistry.AddObject(room); ObjectRegistry.AddObject(a); ObjectRegistry.AddObject(b);
        room.AddObject(a); room.AddObject(b);
        a.ClearMessages(); b.ClearMessages();
        room.MsgContents("Hello", fromObj:a);
        Assert.NotEmpty(a.PeekMessages().Concat(b.PeekMessages()));
    }
    [Fact] public void PluralizeViaParser()
    {
        using var env=GlobalTestEnv.Enter();
        var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES);
        Assert.Equal("cat", p.Parse("$pluralize(cat, abc)")?.ToString());
        Assert.Equal("cats", p.Parse("$pluralize(cat, 2)")?.ToString());
        Assert.Throws<FuncParser.ParsingError>(()=> p.Parse("$pluralize(cat, abc)", raiseErrors:true));
    }
    [Fact] public void SafeConvertContainerFlat()
    {
        using var env=GlobalTestEnv.Enter();
        var conv = FuncParserHelpers.SafeConvertToTypes((new object[]{"py"}, new Dictionary<string,object>()), new object?[]{"(a, b)"}, new Dictionary<string,object?>(), true);
        var arr = conv.args[0] as System.Collections.IEnumerable;
        Assert.NotNull(arr); var list = arr.Cast<object>().Select(o=>o?.ToString()??"").ToList(); Assert.Equal(new[]{"a","b"}, list);
    }
    [Fact] public void SafeConvertNestedRejected()
    {
        using var env=GlobalTestEnv.Enter();
        Assert.Throws<FuncParser.ParsingError>(()=> FuncParserHelpers.SafeConvertToTypes((new object[]{"py"}, new Dictionary<string,object>()), new object?[]{"(a,(b,c))"}, new Dictionary<string,object?>(), true));
    }
}
