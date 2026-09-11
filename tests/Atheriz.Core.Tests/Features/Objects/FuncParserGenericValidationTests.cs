using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Any Dictionary<,>/IDictionary<,> parameter counts as **kwargs support;
// callables without *args or **kwargs support are rejected with the
// matching message.
[Collection("Ported")]
public class FuncParserGenericValidationTests
{
    [Fact]
    public void ValidateGenericCallables_DictionaryObjectParam_Passes()
    {
        var parser = new FuncParser();
        Func<string[], Dictionary<string, object?>, object?> good = (a, k) => "ok";

        var ex = Record.Exception(() => parser.ValidateGenericCallables(
            new Dictionary<string, Delegate> { ["good"] = good }));

        Assert.Null(ex);
    }

    [Fact]
    public void ValidateGenericCallables_DictionaryStringParam_Passes()
    {
        var parser = new FuncParser();
        Func<string[], Dictionary<string, string>, object?> good = (a, k) => "ok";

        var ex = Record.Exception(() => parser.ValidateGenericCallables(
            new Dictionary<string, Delegate> { ["good"] = good }));

        Assert.Null(ex);
    }

    [Fact]
    public void ValidateGenericCallables_IDictionaryParam_Passes()
    {
        var parser = new FuncParser();
        Func<string[], IDictionary<string, object?>, object?> good = (a, k) => "ok";

        var ex = Record.Exception(() => parser.ValidateGenericCallables(
            new Dictionary<string, Delegate> { ["good"] = good }));

        Assert.Null(ex);
    }

    [Fact]
    public void ValidateGenericCallables_PlainCallable_ThrowsArgsError()
    {
        var parser = new FuncParser();
        Func<int, object?> bad = x => x;

        var ex = Assert.Throws<FuncParser.ParsingError>(() => parser.ValidateGenericCallables(
            new Dictionary<string, Delegate> { ["bad"] = bad }));

        Assert.Contains("*args", ex.Message);
    }

    [Fact]
    public void ValidateGenericCallables_ArrayOnlyCallable_ThrowsKwargsError()
    {
        var parser = new FuncParser();
        Func<string[], object?> bad = a => "ok";

        var ex = Assert.Throws<FuncParser.ParsingError>(() => parser.ValidateGenericCallables(
            new Dictionary<string, Delegate> { ["bad"] = bad }));

        Assert.Contains("**kwargs", ex.Message);
    }

    [Fact]
    public void ValidateGenericCallables_PlainCallableCtor_ThrowsArgsError()
    {
        Func<string, Dictionary<string, object?>, object?> bad = (x, kw) => "";

        var ex = Assert.Throws<FuncParser.ParsingError>(() => new FuncParser(
            new Dictionary<string, Delegate> { ["bad"] = bad }));

        Assert.Contains("*args", ex.Message);
    }
}
