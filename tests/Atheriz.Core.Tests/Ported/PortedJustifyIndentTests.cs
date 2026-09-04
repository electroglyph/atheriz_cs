// Port of atheriz/tests/test_justify_indent.py:1 faithful
using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedJustifyIndentTests
{
    [Fact]
    public void DirectUnitHugeIndentIsBounded()
    {
        using var env=GlobalTestEnv.Enter();
        var outStr = FuncParserHelpers.Justify("hi", width:40, align:"l", indent:1000000000);
        Assert.IsType<string>(outStr);
        Assert.True(outStr.Length < 100, $"len {outStr.Length} should be <100, got {outStr}");
    }

    [Fact]
    public void CallableKwargsHugeIndent()
    {
        using var env=GlobalTestEnv.Enter();
        // faithful to funcparser_callable_justify("hi", indent=1e9) with default width 78, align f
        var outStr = FuncParserHelpers.Justify("hi", width:78, align:"f", indent:1000000000);
        Assert.True(outStr.Split("\n").All(l=> l.Length <= 80), $"found line >80: {outStr}");
        var viaParser = new FuncParser(FuncParser.ACTOR_STANCE_CALLABLES).Parse("$just(hi, indent=1000000000)")?.ToString() ?? "";
        Assert.True(viaParser.Length < 500);
        // also direct via callable helper (align f)
        var viaHelper = FuncParserHelpers.Justify("hi", indent:1000000000, align:"f");
        Assert.True(viaHelper.Split("\n").All(l=> l.Length <= 80));
    }

    [Fact]
    public void CallablePositionalHugeIndent()
    {
        using var env=GlobalTestEnv.Enter();
        // funcparser_callable_justify("hi", 40, "f", 10**9) positional
        var outStr = FuncParserHelpers.Justify("hi", width:40, align:"f", indent:1000000000);
        Assert.True(outStr.Split("\n").All(l=> l.Length <= 80));
    }

    [Fact]
    public void NegativeIndentMatchesZero()
    {
        using var env=GlobalTestEnv.Enter();
        var neg = FuncParserHelpers.Justify("hi there friend", width:20, align:"l", indent:-5);
        var zero = FuncParserHelpers.Justify("hi there friend", width:20, align:"l", indent:0);
        Assert.Equal(zero, neg);
    }

    [Fact]
    public void ReasonableIndentStillApplied()
    {
        using var env=GlobalTestEnv.Enter();
        var outStr = FuncParserHelpers.Justify("hi", width:40, align:"l", indent:4);
        var firstLine = outStr.Split("\n")[0];
        Assert.StartsWith("    ", firstLine);
        Assert.Contains("hi", firstLine);
    }

    [Fact]
    public void ParserPathBounded()
    {
        using var env=GlobalTestEnv.Enter();
        var parser = new FuncParser(FuncParser.ACTOR_STANCE_CALLABLES);
        var outStr = parser.Parse("$just(hi, indent=100000000)")?.ToString() ?? "";
        Assert.IsType<string>(outStr);
        Assert.True(outStr.Length < 200, $"len {outStr.Length} should be <200");
    }

    [Fact]
    public void PlayerTextViaMsgContentsIsBounded()
    {
        using var env=GlobalTestEnv.Enter();
        var coord = new Coord("test",0,0,0);
        var node = new Node(coord, desc:"room");
        var alice = GameObject.Create("Alice", isPc:true); alice.IsConnected=true;
        var bob = GameObject.Create("Bob", isPc:true); bob.IsConnected=true;
        bob.Location = new LocationRef.CoordLocation(coord);
        alice.Location = new LocationRef.CoordLocation(coord);
        ObjectRegistry.AddObject(alice); ObjectRegistry.AddObject(bob);
        node.AddObject(alice);
        node.AddObject(bob);
        bob.ClearMessages();
        // 500x len(sent)<4096 not Assert.True(true) – faithful
        node.MsgContents("Alice says: $just(hi, indent=100000000)");
        var sent = bob.PeekMessages().FirstOrDefault() ?? "";
        // In original, they check bob.msg call_args length <4096; we check our stored message
        Assert.True(sent.Length < 4096, $"msg length {sent.Length} should be <4096, got {sent.Length} preview: {sent.Substring(0, Math.Min(200, sent.Length))}");
        // Also ensure not huge
        foreach(var msg in bob.PeekMessages()){
            Assert.True(msg.Length < 4096);
        }
    }
}
