using System.Reflection;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Simplify3;

// The adapter decides its shape once at wrap time and invokes through the
// decided path; arity/type failures surface as ParsingError when raising
// and as "" otherwise.
[Collection("Ported")]
public class FuncParserGenericWrapperTests
{
    private static FuncParser.ParserCallable Wrap(Delegate del, string key = "fn")
    {
        var empty = new FuncParser();
        var method = typeof(FuncParser).GetMethod("BuildGenericWrapper", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (FuncParser.ParserCallable)method.Invoke(empty, [del, key])!;
    }

    private static FuncParser ParserWith(string name, FuncParser.ParserCallable callable)
    {
        return new FuncParser(new Dictionary<string, FuncParser.ParserCallable> { [name] = callable });
    }

    [Fact]
    public void BuildGenericWrapper_TwoParam_ReceivesArgsAndKwargs()
    {
        Func<string[], Dictionary<string, object?>, object?> two = (a, k) => $"{a[0]}-{a[1]}-{k["flag"]}";
        var parser = new FuncParser(new Dictionary<string, Delegate> { ["two"] = two });

        var result = parser.Parse("$two(a,b,flag=c)")?.ToString();

        Assert.Equal("a-b-c", result);
    }

    [Fact]
    public void BuildGenericWrapper_ArrayParam_ReceivesArgs()
    {
        Func<string[], object?> arr = a => string.Join("+", a);
        var parser = ParserWith("arr", Wrap(arr, "arr"));

        var result = parser.Parse("$arr(a,b)")?.ToString();

        Assert.Equal("a+b", result);
    }

    [Fact]
    public void BuildGenericWrapper_ZeroArg_IgnoresCallArgs()
    {
        Func<object?> zero = () => "Z";
        var parser = ParserWith("zero", Wrap(zero, "zero"));

        Assert.Equal("Z", parser.Parse("$zero()")?.ToString());
        Assert.Equal("Z", parser.Parse("$zero(a,b)")?.ToString());
    }

    [Fact]
    public void BuildGenericWrapper_SpreadParam_ReceivesSingleArg()
    {
        Func<string, object?> one = x => $"hi {x}";
        var parser = ParserWith("one", Wrap(one, "one"));

        var result = parser.Parse("$one(bob)")?.ToString();

        Assert.Equal("hi bob", result);
    }

    [Fact]
    public void BuildGenericWrapper_SpreadMismatch_NonRaisingReturnsEmpty()
    {
        Func<string, object?> one = x => $"hi {x}";
        var parser = ParserWith("one", Wrap(one, "one"));

        var result = parser.Parse("$one()", false)?.ToString();

        Assert.Equal("", result);
    }

    [Fact]
    public void BuildGenericWrapper_SpreadMismatch_RaisingThrowsParsingError()
    {
        Func<string, object?> one = x => $"hi {x}";
        var parser = ParserWith("one", Wrap(one, "one"));

        var ex = Assert.Throws<FuncParser.ParsingError>(() => parser.Parse("$one()", true));

        Assert.Contains("argument mismatch", ex.Message);
        Assert.Contains("one", ex.Message);
    }

    [Fact]
    public void BuildGenericWrapper_TwoParamTypeMismatch_RaisingThrowsParsingError()
    {
        Func<int, int, object?> bad = (x, y) => x + y;
        var parser = ParserWith("bad", Wrap(bad, "bad"));

        var ex = Assert.Throws<FuncParser.ParsingError>(() => parser.Parse("$bad(a,b)", true));

        Assert.Contains("argument mismatch", ex.Message);
    }

    [Fact]
    public void BuildGenericWrapper_TwoParamTypeMismatch_NonRaisingReturnsEmpty()
    {
        Func<int, int, object?> bad = (x, y) => x + y;
        var parser = ParserWith("bad", Wrap(bad, "bad"));

        var result = parser.Parse("$bad(a,b)", false)?.ToString();

        Assert.Equal("", result);
    }
}
