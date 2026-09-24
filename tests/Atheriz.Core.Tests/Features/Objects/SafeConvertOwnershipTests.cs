// Pins for U4 (SafeConvertToTypes caller-ownership): the args array and the
// kwargs dictionary are both consumed in place — converted values overwrite
// the caller's slots/entries, and the returned tuple aliases the inputs.
// Converters are plain functions (a null slot leaves the value as-is).
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

[Collection("Ported")]
public sealed class SafeConvertOwnershipTests
{
    private static object? ParseInt(object? o) =>
        o is string s && int.TryParse(s, out var iv) ? iv : o;

    [Fact]
    public void SafeConvertToTypes_ArgsArray_MutatedInPlace()
    {
        using var env = GlobalTestEnv.Enter();
        object?[] args = ["42", "keep"];
        var result = FuncParserHelpers.SafeConvertToTypes(
            [ParseInt],
            args, [], raiseErrors: true);
        Assert.Same(args, result.args);
        Assert.Equal(42, args[0]);
        Assert.Equal("keep", args[1]);
    }

    [Fact]
    public void SafeConvertToTypes_KwargsDict_MutatedInPlace()
    {
        using var env = GlobalTestEnv.Enter();
        var kwargs = new Dictionary<string, object?> { ["n"] = "7", ["other"] = "x" };
        var result = FuncParserHelpers.SafeConvertToTypes(
            [],
            [], kwargs, new Dictionary<string, Func<object?, object?>> { ["n"] = ParseInt }, raiseErrors: true);
        Assert.Same(kwargs, result.kwargs);
        Assert.Equal(7, kwargs["n"]);
        Assert.Equal("x", kwargs["other"]);
    }

    [Fact]
    public void SafeConvertToTypes_NoConverters_ReturnsSameInstances()
    {
        using var env = GlobalTestEnv.Enter();
        object?[] args = ["a"];
        var kwargs = new Dictionary<string, object?> { ["k"] = "v" };
        var result = FuncParserHelpers.SafeConvertToTypes(
            [],
            args, kwargs, raiseErrors: true);
        Assert.Same(args, result.args);
        Assert.Same(kwargs, result.kwargs);
        Assert.Equal("a", result.args[0]);
    }

    [Fact]
    public void SafeConvertToTypes_NullSlot_LeavesValue()
    {
        using var env = GlobalTestEnv.Enter();
        object?[] args = ["a", "b"];
        var result = FuncParserHelpers.SafeConvertToTypes(
            [null, ParseInt],
            args, [], raiseErrors: true);
        Assert.Equal("a", args[0]);
        Assert.Equal("b", args[1]);
    }
}
