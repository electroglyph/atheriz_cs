using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Safe arithmetic: unary minus binds outside ** (-2**2 is -4) and %
// floors like Python (-7%3 is 2). Pure functions only.
[Collection("Ported")]
public sealed class SafeArithEvalTests
{
    [Theory]
    [InlineData("-2**2", -4.0)]
    [InlineData("2**-2", 0.25)]
    [InlineData("(-2)**2", 4.0)]
    [InlineData("2**3**2", 512.0)]
    [InlineData("2**10", 1024.0)]
    public void SafeArithEval_PowerBindsTighterThanUnaryMinus(string expr, double expected)
    {
        Assert.Equal(expected, FuncParserHelpers.SafeArithEval(expr));
    }

    [Theory]
    [InlineData("-7%3", 2.0)]
    [InlineData("7%-3", -2.0)]
    [InlineData("7%3", 1.0)]
    public void SafeArithEval_NegativeModulo_Floors(string expr, double expected)
    {
        Assert.Equal(expected, FuncParserHelpers.SafeArithEval(expr));
    }
}
