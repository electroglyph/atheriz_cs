using Atheriz.Core.Commands;

namespace Atheriz.Core.Tests.Features.Commands;

// The three REMAINDER drains (help-absorb, dash-absorb, positional) all
// consume everything remaining, and the shared value-token guard stops list
// consumers at known and unknown -flags alike.
[Collection("Ported")]
public sealed class RemainderDrainTests
{
    private static GameArgumentParser RemainderParser()
    {
        var p = new GameArgumentParser();
        p.AddArgument("msg", nargs: "REMAINDER", help: "msg");
        return p;
    }

    [Fact]
    public void Remainder_AbsorbsHelpToken()
    {
        var pa = RemainderParser().ParseArgs(["--help"]);
        Assert.Equal(["--help"], pa.GetList("msg"));
    }

    [Fact]
    public void Remainder_AbsorbsDashTokens()
    {
        var pa = RemainderParser().ParseArgs(["-x", "foo", "--bar"]);
        Assert.Equal(["-x", "foo", "--bar"], pa.GetList("msg"));
    }

    [Fact]
    public void Remainder_AbsorbsPlainTokens()
    {
        var pa = RemainderParser().ParseArgs(["hello", "world"]);
        Assert.Equal(["hello", "world"], pa.GetList("msg"));
    }

    [Fact]
    public void ValueToken_StopsAtKnownOptional()
    {
        var p = new GameArgumentParser();
        p.AddArgument("items", nargs: "*", help: "items");
        p.AddArgument("-l", action: "store_true", help: "l");
        var pa = p.ParseArgs(["a", "b", "-l"]);
        Assert.Equal(["a", "b"], pa.GetList("items"));
        Assert.True(pa.GetBool("l"));
    }

    [Fact]
    public void ValueToken_StopsAtUnknownFlag()
    {
        var p = new GameArgumentParser();
        p.AddArgument("items", nargs: "*", help: "items");
        p.AddArgument("rest", nargs: "REMAINDER", help: "rest");
        var pa = p.ParseArgs(["a", "--zz", "b"]);
        Assert.Equal(["a"], pa.GetList("items"));
        Assert.Equal(["--zz", "b"], pa.GetList("rest"));
    }

    [Fact]
    public void OptionalList_StopsAtUnknownFlag()
    {
        var p = new GameArgumentParser();
        p.AddArgument("-l", nargs: "*", help: "l");
        p.AddArgument("rest", nargs: "REMAINDER", help: "rest");
        var pa = p.ParseArgs(["-l", "a", "--zz", "b"]);
        Assert.Equal(["a"], pa.GetList("l"));
        Assert.Equal(["--zz", "b"], pa.GetList("rest"));
    }
}
