// Pins: max-faces dice rolls without overflow, $random/$randint long
// bounds, explicit capitalize=false winning over the capital stanza,
// non-finite arithmetic results never reaching chat.
using Atheriz.Core.Objects;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Correctness;

[Collection("Ported")]
public sealed class RandomAndArithmeticTests
{
    [Fact]
    public void DiceRoll_MaxFaces_ReturnsRollInRange()
    {
        var roll = GameUtils.DiceRoll(1, int.MaxValue);
        Assert.InRange(roll, 1, int.MaxValue);
    }

    [Fact]
    public void Randint_MaxBound_ReturnsValueInRange()
    {
        var parser = new FuncParser(FuncParser.ActorStanceCallables);
        var raising = parser.Parse("$randint(0, 2147483647)", raiseErrors: true)?.ToString();
        Assert.True(int.TryParse(raising, out var rv) && rv >= 0, $"not a non-negative int: {raising}");
        var quiet = parser.Parse("$randint(0, 2147483647)", raiseErrors: false)?.ToString();
        Assert.True(int.TryParse(quiet, out var qv) && qv >= 0, $"not a non-negative int: {quiet}");
    }

    [Fact]
    public void Random_MaxBound_ReturnsValueInRange()
    {
        var parser = new FuncParser(FuncParser.ActorStanceCallables);
        var raising = parser.Parse("$random(2147483647)", raiseErrors: true)?.ToString();
        Assert.True(int.TryParse(raising, out var rv) && rv >= 0, $"not a non-negative int: {raising}");
    }

    [Fact]
    public void You_ExplicitCapitalizeFalse_RendersLowercase()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = GameObject.Create("Hero");
        var parser = new FuncParser(FuncParser.ActorStanceCallables);
        Assert.Equal("you", parser.Parse("$You(x, capitalize=false)", hero, hero, null)?.ToString());
        Assert.Equal("you", parser.Parse("$you(x, capitalize=false)", hero, hero, null)?.ToString());
        Assert.Equal("You", parser.Parse("$You(x)", hero, hero, null)?.ToString());
        Assert.Equal("You", parser.Parse("$you(x, capitalize=true)", hero, hero, null)?.ToString());
    }

    [Fact]
    public void Mult_Overflow_RendersEmptyAndRaisesWhenAsked()
    {
        var parser = new FuncParser(FuncParser.ActorStanceCallables);
        Assert.Equal("", parser.Parse("$mult(1e308, 10)", raiseErrors: false)?.ToString());
        Assert.Throws<FuncParser.ParsingError>(() => parser.Parse("$mult(1e308, 10)", raiseErrors: true));
        Assert.Equal("6", parser.Parse("$mult(2, 3)", raiseErrors: false)?.ToString());
    }
}
