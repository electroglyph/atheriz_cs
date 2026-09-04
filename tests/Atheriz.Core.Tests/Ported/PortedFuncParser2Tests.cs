// Port of atheriz/tests/test_funcparser_2.py:1 faithful
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedFuncParser2Tests
{
    [Fact]
    public void LargeExponentIsRejected()
    {
        using var env = GlobalTestEnv.Enter();
        // Port of test_funcparser_2.py:test_large_exponent_is_rejected — 2**2**16 should exceed guard, exact ValueError/OverflowError
        // In C# maps to InvalidOperationException (exceeds safe limit) or ArgumentException (ValueError) or OverflowException
        Exception? caught=null;
        try{ FuncParserHelpers.SafeArithEval("2**2**16"); }catch(Exception ex){ caught=ex; }
        Assert.NotNull(caught);
        Assert.True(caught is InvalidOperationException || caught is ArgumentException || caught is OverflowException, $"Expected ValueError/OverflowError mapping, got {caught!.GetType().Name}: {caught.Message}");
        Assert.Contains("exceeds", caught.Message.ToLower());
    }

    [Fact]
    public void PowGuardConstantIsDefined()
    {
        using var env = GlobalTestEnv.Enter();
        var guard = FuncParserHelpers._MAX_POW_EXPONENT;
        Assert.True(guard > 0, "guard must be defined");
        Assert.True(guard < Math.Pow(9, 9), $"guard {guard} should be < 9**9");
    }

    [Fact]
    public void SafeArithLargePowBlocked()
    {
        using var env = GlobalTestEnv.Enter();
        Exception? caught=null;
        try{ FuncParserHelpers.SafeArithEval("9**9**9"); }catch(Exception ex){ caught=ex; }
        Assert.NotNull(caught);
        Assert.True(caught is InvalidOperationException || caught is ArgumentException || caught is OverflowException);
    }

    [Fact]
    public void ChainedPowSizeCapIsControlled()
    {
        using var env = GlobalTestEnv.Enter();
        Exception? caught=null;
        try{ FuncParserHelpers.SafeArithEval("(10**10000)**9999"); }catch(Exception ex){ caught=ex; }
        Assert.NotNull(caught);
        Assert.True(caught is InvalidOperationException || caught is ArgumentException);
        _ = FuncParserHelpers.SafeArithEval("2**1000");
    }

    [Fact]
    public void NormalPowAllowed()
    {
        using var env = GlobalTestEnv.Enter();
        var v = FuncParserHelpers.SafeArithEval("2**10");
        Assert.Equal(1024, v);
    }
}
