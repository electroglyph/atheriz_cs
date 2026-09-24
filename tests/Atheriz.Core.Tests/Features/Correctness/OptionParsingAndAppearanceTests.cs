// Pins: single-value options stop at a following flag, AddArgument
// validates names, no dangling colon on empty desc. The $conj
// unknown-key echo is pinned by Conj_UnmappedKey_StaysVisible in
// FuncParserActorResolutionTests.
using Atheriz.Core.Commands;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Correctness;

[Collection("Ported")]
public sealed class OptionParsingAndAppearanceTests
{
    [Fact]
    public void SingleStoreOption_FollowingFlag_ErrorsInsteadOfSwallowing()
    {
        var parser = new GameArgumentParser("reg", addHelp: false);
        parser.AddArgument("--desc");
        parser.AddArgument("--help2").Action(GameArgumentParser.ArgAction.StoreTrue);
        var ex = Assert.Throws<CommandError>(() => parser.ParseArgs(["--desc", "--help2"]));
        Assert.Contains("expected one argument", ex.Message);
    }

    [Fact]
    public void SingleStoreOption_NegativeNumberValue_StillConsumed()
    {
        var parser = new GameArgumentParser("reg", addHelp: false);
        parser.AddArgument("--count").Type<int>();
        var args = parser.ParseArgs(["--count", "-5"]);
        Assert.Equal(-5, args["count"]);
    }

    [Fact]
    public void AddArgument_NoNames_ThrowsArgumentException()
    {
        var parser = new GameArgumentParser("reg", addHelp: false);
        Assert.Throws<ArgumentException>(() => parser.AddArgument());
        Assert.Throws<ArgumentException>(() => parser.AddArgument(""));
        Assert.Throws<ArgumentException>(() => parser.AddArgument(new[] { "--ok", "" }));
        Assert.Throws<ArgumentException>(() => parser.AddArgument(new[] { (string)null! }));
        Assert.Throws<ArgumentNullException>(() => parser.AddArgument(null!));
    }

    [Fact]
    public void ReturnAppearance_EmptyDesc_OmitsColon()
    {
        using var env = GlobalTestEnv.Enter();
        var bare = GameObject.Create("Bare");
        bare.Desc = "";
        var looker = GameObject.Create("Looker");
        Assert.Equal("Bare", bare.ReturnAppearance(looker));
        bare.Desc = "A plain thing.";
        Assert.Equal("Bare: A plain thing.", bare.ReturnAppearance(looker));
    }
}
