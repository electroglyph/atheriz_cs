using Atheriz.Core.Commands;

namespace Atheriz.Core.Tests.Features.Commands;

// Audit 10 finding 8: AddArgument("-n", "--num", type: ...) took the long
// form as help text and registered only -n. Both aliases now register,
// with the type preserved.
public sealed class TypedOptionPairTests
{
    [Fact]
    public void TypedOptionPair_LongForm_Parses()
    {
        var p = new GameArgumentParser(prog: "t");
        p.AddArgument("-n", "--num", type: typeof(int));
        var parsed = p.ParseArgs(["--num", "5"]);
        Assert.True(parsed.Has("num"));
        // The type must survive the alias-pair path, not just the name.
        Assert.Equal(5, parsed.Get<int>("num"));
    }

    [Fact]
    public void TypedOptionPair_ShortForm_ParsesToSameDest()
    {
        var p = new GameArgumentParser(prog: "t");
        p.AddArgument("-n", "--num", type: typeof(int));
        var parsed = p.ParseArgs(["-n", "5"]);
        Assert.True(parsed.Has("num"));
        Assert.Equal(5, parsed.Get<int>("num"));
    }

    [Fact]
    public void UntypedOptionPair_StillParsesBothForms()
    {
        var p = new GameArgumentParser(prog: "t");
        p.AddArgument("-n", "--num");
        Assert.True(p.ParseArgs(["--num", "5"]).Has("num"));
        Assert.True(p.ParseArgs(["-n", "5"]).Has("num"));
    }
}
