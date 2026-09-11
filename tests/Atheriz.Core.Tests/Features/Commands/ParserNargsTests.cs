// Argument-parser nargs unification: the single-string AddArgument overload
// accepts "..." exactly like the Builder overload (both map to REMAINDER).
using Atheriz.Core.Commands;

namespace Atheriz.Core.Tests.Features.Commands;

[Collection("Ported")]
public sealed class ParserNargsTests
{
    [Fact]
    public void AddArgument_EllipsisNargs_ParsesRemainderOnBothOverloads()
    {
        var viaBuilder = new GameArgumentParser("nargs_a");
        viaBuilder.AddArgument("msg").Nargs("...");
        var a = viaBuilder.ParseArgs(["hello", "world"]);
        Assert.Equal(new List<string> { "hello", "world" }, a.GetList("msg"));

        var viaSingle = new GameArgumentParser("nargs_b");
        viaSingle.AddArgument("msg", "", "...");
        var b = viaSingle.ParseArgs(["hello", "world"]);
        Assert.Equal(new List<string> { "hello", "world" }, b.GetList("msg"));

        var viaRemainder = new GameArgumentParser("nargs_c");
        viaRemainder.AddArgument("msg", "", "REMAINDER");
        var c = viaRemainder.ParseArgs(["hello", "world"]);
        Assert.Equal(new List<string> { "hello", "world" }, c.GetList("msg"));
    }
}
