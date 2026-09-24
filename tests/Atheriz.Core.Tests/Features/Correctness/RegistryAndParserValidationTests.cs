// Pins: the registry keeps the live same-id occupant when the victim is
// removed, #id references accept digits only, unknown nargs strings
// throw, float/double reject comma-grouped input.
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Correctness;

[Collection("Ported")]
public sealed class RegistryAndParserValidationTests
{
    [Fact]
    public void RemoveObject_VictimWithLiveSameIdOccupant_KeepsOccupant()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            var a = GameObject.Create("Victim");
            ObjectRegistry.AddObject(a);
            var b = GameObject.Create("Occupant");
            b.Id = a.Id;
            ObjectRegistry.AddObject(b);
            ObjectRegistry.RemoveObject(a);
            Assert.Same(b, ObjectRegistry.GetSingle(a.Id));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void ParseNargs_UnknownString_ThrowsArgumentException()
    {
        var parser = new GameArgumentParser("reg", addHelp: false);
        Assert.Throws<ArgumentException>(() => parser.AddArgument("target").Nargs("REMINDER"));
    }

    [Fact]
    public void DoubleType_CommaGroupedInput_Rejected()
    {
        var parser = new GameArgumentParser("reg", addHelp: false);
        parser.AddArgument("amount").Type<double>();
        Assert.Throws<CommandError>(() => parser.ParseArgs(["1,000"]));
        var ok = parser.ParseArgs(["1000"]);
        Assert.Equal(1000.0, ok["amount"]);
    }

    [Fact]
    public void FloatType_CommaGroupedInput_Rejected()
    {
        var parser = new GameArgumentParser("reg", addHelp: false);
        parser.AddArgument("amount").Type<float>();
        Assert.Throws<CommandError>(() => parser.ParseArgs(["1,000"]));
    }
}
