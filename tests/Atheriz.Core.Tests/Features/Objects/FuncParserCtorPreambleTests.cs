// Pins for the chained FuncParser ctors: the direct table constructs an
// equivalent parser, custom scalars and the default-kwargs copy survive,
// and game callables use the ParserCallable shape directly (no
// signature-sniffed generic-Delegate path).
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

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
    public void EmptyCtor_UnknownCall_Echoes()
    {
        using var env = GlobalTestEnv.Enter();
        var parser = new FuncParser();

        Assert.Equal("$nosuch()", parser.Parse("$nosuch()")?.ToString());
    }
}
