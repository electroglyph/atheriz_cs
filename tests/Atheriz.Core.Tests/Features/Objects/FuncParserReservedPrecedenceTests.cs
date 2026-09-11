using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Reserved caller/receiver/mapping reach the callable context through Execute
// with reserved winning over parsed kwargs and defaults.
[Collection("Ported")]
public class FuncParserReservedPrecedenceTests
{
    private static FuncParser SpyParser(string name, Action<FuncParser.ParserContext> capture)
    {
        return new FuncParser(new Dictionary<string, FuncParser.ParserCallable>
        {
            [name] = (a, k, ctx, raw) => { capture(ctx); return "ok"; },
        });
    }

    private static FuncParser.ParsedFunc Call(string name)
    {
        return new FuncParser.ParsedFunc { FuncName = name };
    }

    [Fact]
    public void Execute_ReservedCaller_OverridesParsedKwarg()
    {
        var parsedCaller = GameObject.Create("parsed");
        var reservedCaller = GameObject.Create("reserved");
        FuncParser.ParserContext? seen = null;
        var parser = SpyParser("who", ctx => seen = ctx);
        var pf = Call("who");
        pf.Kwargs["caller"] = parsedCaller;
        var reserved = new Dictionary<string, object?> { ["caller"] = reservedCaller };

        parser.Execute(pf, false, reserved);

        Assert.Same(reservedCaller, seen!.Caller);
    }

    [Fact]
    public void Execute_ReservedReceiver_OverridesParsedKwarg()
    {
        var parsedReceiver = GameObject.Create("parsed");
        var reservedReceiver = GameObject.Create("reserved");
        FuncParser.ParserContext? seen = null;
        var parser = SpyParser("who", ctx => seen = ctx);
        var pf = Call("who");
        pf.Kwargs["receiver"] = parsedReceiver;
        var reserved = new Dictionary<string, object?> { ["receiver"] = reservedReceiver };

        parser.Execute(pf, false, reserved);

        Assert.Same(reservedReceiver, seen!.Receiver);
    }

    [Fact]
    public void Execute_ReservedMapping_OverridesParsedKwarg()
    {
        var parsedMapping = new Dictionary<string, object?> { ["k"] = "parsed" };
        var reservedMapping = new Dictionary<string, object?> { ["k"] = "reserved" };
        FuncParser.ParserContext? seen = null;
        var parser = SpyParser("who", ctx => seen = ctx);
        var pf = Call("who");
        pf.Kwargs["mapping"] = parsedMapping;
        var reserved = new Dictionary<string, object?> { ["mapping"] = reservedMapping };

        parser.Execute(pf, false, reserved);

        Assert.Same(reservedMapping, seen!.Mapping);
    }

    [Fact]
    public void Execute_WithoutReserved_FallsBackToParsedKwarg()
    {
        var parsedCaller = GameObject.Create("parsed");
        var parsedReceiver = GameObject.Create("recv");
        var parsedMapping = new Dictionary<string, object?> { ["k"] = "v" };
        FuncParser.ParserContext? seen = null;
        var parser = SpyParser("who", ctx => seen = ctx);
        var pf = Call("who");
        pf.Kwargs["caller"] = parsedCaller;
        pf.Kwargs["receiver"] = parsedReceiver;
        pf.Kwargs["mapping"] = parsedMapping;

        parser.Execute(pf);

        Assert.Same(parsedCaller, seen!.Caller);
        Assert.Same(parsedReceiver, seen!.Receiver);
        Assert.Same(parsedMapping, seen!.Mapping);
    }

    [Fact]
    public void Execute_ReservedCaller_OverridesDefaultKwarg()
    {
        var defaultCaller = GameObject.Create("default");
        var reservedCaller = GameObject.Create("reserved");
        FuncParser.ParserContext? seen = null;
        var parser = new FuncParser(
            new Dictionary<string, FuncParser.ParserCallable>
            {
                ["who"] = (a, k, ctx, raw) => { seen = ctx; return "ok"; },
            },
            defaultKwargs: new Dictionary<string, object?> { ["caller"] = defaultCaller });
        var pf = Call("who");
        var reserved = new Dictionary<string, object?> { ["caller"] = reservedCaller };

        parser.Execute(pf, false, reserved);

        Assert.Same(reservedCaller, seen!.Caller);
    }

    [Fact]
    public void Execute_ReservedCaller_DecidesYouStance()
    {
        var hero = GameObject.Create("Hero");
        var villain = GameObject.Create("Villain");
        var parser = new FuncParser(FuncParser.ActorStanceCallables);
        var pf = new FuncParser.ParsedFunc { FuncName = "you" };
        pf.Kwargs["caller"] = villain;
        pf.Kwargs["receiver"] = villain;
        var reserved = new Dictionary<string, object?>
        {
            ["caller"] = hero,
            ["receiver"] = hero,
        };

        var result = parser.Execute(pf, false, reserved);

        Assert.Equal("you", result?.ToString());
    }
}
