// GameArgumentParser def-map caching and invalidation: repeated parses
// share the cached def maps, adding an argument after the first parse is visible
// to the second parse, and Builder mutations after the first parse take effect
// (defs stay mutable post-build; the parser rebuilds instead of throwing).
using Atheriz.Core.Commands;

namespace Atheriz.Core.Tests.Features.Commands;

[Collection("Ported")]
public sealed class ParserDefMapsTests
{
    [Fact]
    public void ParseArgs_RepeatedParses_SameShape()
    {
        var p = new GameArgumentParser("parser_shape");
        p.AddArgument("name");
        p.AddArgument("--tag");
        var first = p.ParseArgs(["alice", "--tag", "red"]);
        var second = p.ParseArgs(["bob", "--tag", "blue"]);
        Assert.Equal("alice", first.GetString("name"));
        Assert.Equal("red", first.GetString("tag"));
        Assert.Equal("bob", second.GetString("name"));
        Assert.Equal("blue", second.GetString("tag"));
    }

    [Fact]
    public void ParseArgs_AddArgAfterFirstParse_SecondParseSeesIt()
    {
        var p = new GameArgumentParser("parser_add");
        p.AddArgument("first");
        var before = p.ParseArgs(["a"]);
        Assert.Equal("a", before.GetString("first"));
        p.AddArgument("second");
        var after = p.ParseArgs(["a", "b"]);
        Assert.Equal("a", after.GetString("first"));
        Assert.Equal("b", after.GetString("second"));
    }

    [Fact]
    public void ParseArgs_BuilderMutationAfterFirstParse_SecondParseSeesIt()
    {
        var p = new GameArgumentParser("parser_builder");
        var builder = p.AddArgument("count");
        var single = p.ParseArgs(["5"]);
        Assert.Equal("5", single.GetString("count"));
        builder.Nargs("*");
        var multi = p.ParseArgs(["a", "b"]);
        Assert.Equal(new List<string> { "a", "b" }, multi.GetList("count"));
    }
}
