// Pins for the chained FuncParser ctors: the direct, generic-delegate, and
// mixed tables all construct equivalent parsers, custom scalars and the
// default-kwargs copy survive chaining, and generic validation still fires.
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class FuncParserCtorPreambleTests
{
    private static Dictionary<string, FuncParser.ParserCallable> ShoutTable() =>
        new() { ["shout"] = (a, k, ctx, raw) => "HI" };

    [Fact]
    public void DirectTableCtor_KnownCallable_Parses()
    {
        using var env = GlobalTestEnv.Enter();
        var parser = new FuncParser(ShoutTable());

        Assert.Equal("HI", parser.Parse("$shout()")?.ToString());
        Assert.Equal(FuncParser.StartChar, parser.StartCharProp);
        Assert.Equal(FuncParser.EscapeChar, parser.EscapeCharProp);
        Assert.Equal(FuncParser.MaxNesting, parser.MaxNestingProp);
        Assert.Empty(parser.DefaultKwargs);
    }

    [Fact]
    public void DirectTableCtor_CustomScalars_Preserved()
    {
        using var env = GlobalTestEnv.Enter();
        var defaults = new Dictionary<string, object?> { ["x"] = (object?)1 };
        var parser = new FuncParser(ShoutTable(), escapeChar: '!', maxNesting: 5, defaultKwargs: defaults);

        Assert.Equal(FuncParser.StartChar, parser.StartCharProp);
        Assert.Equal('%', new FuncParser(ShoutTable(), startChar: '%').StartCharProp);
        Assert.Equal('!', parser.EscapeCharProp);
        Assert.Equal(5, parser.MaxNestingProp);
        Assert.Equal(1, parser.DefaultKwargs["x"]);
        Assert.Equal("HI", parser.Parse("$shout()")?.ToString());
    }

    [Fact]
    public void DirectTableCtor_DefaultKwargs_CopiedAtConstruction()
    {
        using var env = GlobalTestEnv.Enter();
        var defaults = new Dictionary<string, object?> { ["x"] = (object?)1 };
        var parser = new FuncParser(ShoutTable(), defaultKwargs: defaults);
        defaults["x"] = 2;
        defaults["late"] = 3;

        Assert.Equal(1, parser.DefaultKwargs["x"]);
        Assert.False(parser.DefaultKwargs.ContainsKey("late"));
    }

    [Fact]
    public void GenericDelegateCtor_TwoParamCallable_ReceivesArgsAndKwargs()
    {
        using var env = GlobalTestEnv.Enter();
        Func<string[], Dictionary<string, object?>, object?> two = (a, k) => $"{a[0]}-{k["flag"]}";
        var parser = new FuncParser(new Dictionary<string, Delegate> { ["two"] = two });

        Assert.Equal("a-b", parser.Parse("$two(a,flag=b)")?.ToString());
    }

    [Fact]
    public void GenericDelegateCtor_InvalidCallable_ThrowsParsingError()
    {
        using var env = GlobalTestEnv.Enter();
        Func<string, object?> one = x => $"hi {x}";

        Assert.Throws<FuncParser.ParsingError>(
            () => new FuncParser(new Dictionary<string, Delegate> { ["one"] = one }));
    }

    [Fact]
    public void MixedTableCtor_SplitsDirectAndAdapted()
    {
        using var env = GlobalTestEnv.Enter();
        Func<string[], Dictionary<string, object?>, object?> two = (a, k) => $"{a[0]}-{k["flag"]}";
        FuncParser.ParserCallable direct = (a, k, ctx, raw) => "D";
        var parser = new FuncParser(new Dictionary<string, object> { ["d"] = direct, ["g"] = two });

        Assert.Equal("D", parser.Parse("$d()")?.ToString());
        Assert.Equal("a-b", parser.Parse("$g(a,flag=b)")?.ToString());
    }

    [Fact]
    public void MixedTableCtor_DirectOnly_SkipsValidation()
    {
        using var env = GlobalTestEnv.Enter();
        FuncParser.ParserCallable direct = (a, k, ctx, raw) => "D";
        var parser = new FuncParser(new Dictionary<string, object> { ["d"] = direct });

        Assert.Equal("D", parser.Parse("$d()")?.ToString());
    }

    [Fact]
    public void EmptyCtor_UnknownCall_Echoes()
    {
        using var env = GlobalTestEnv.Enter();
        var parser = new FuncParser();

        Assert.Equal("$nosuch()", parser.Parse("$nosuch()")?.ToString());
    }
}
