using Atheriz.Core.Commands;

namespace Atheriz.Core.Tests.Features.Commands;

// Both AddArgument overloads derive Dest identically: the long (--) alias
// wins, dashes strip, inner dashes become underscores.
[Collection("Ported")]
public sealed class ArgumentDestDerivationTests
{
    [Fact]
    public void TwoAliasHack_MatchesParamsOverload()
    {
        var viaHack = new GameArgumentParser();
        viaHack.AddArgument("-f", "--flag").Action(GameArgumentParser.ArgAction.StoreTrue);
        var viaParams = new GameArgumentParser();
        viaParams.AddArgument(["-f", "--flag"]).Action(GameArgumentParser.ArgAction.StoreTrue);
        var h = viaHack.ParseArgs(["--flag"]);
        var p = viaParams.ParseArgs(["--flag"]);
        Assert.True(h.GetBool("flag"));
        Assert.True(p.GetBool("flag"));
        Assert.Equal(p.GetBool("flag"), h.GetBool("flag"));
    }

    [Fact]
    public void ShortOnly_StripsDash()
    {
        var parser = new GameArgumentParser();
        parser.AddArgument("-x").Type(typeof(int));
        var pa = parser.ParseArgs(["-x", "3"]);
        Assert.Equal(3, pa.Get<int>("x"));
    }

    [Fact]
    public void LongWithInnerDashes_BecomesUnderscores()
    {
        var parser = new GameArgumentParser();
        parser.AddArgument("--dry-run").Action(GameArgumentParser.ArgAction.StoreTrue);
        Assert.True(parser.ParseArgs(["--dry-run"]).GetBool("dry_run"));
    }

    [Fact]
    public void Positional_KeepsName()
    {
        var parser = new GameArgumentParser();
        parser.AddArgument("target", help: "t");
        Assert.Equal("v", parser.ParseArgs(["v"]).GetString("target"));
    }
}
