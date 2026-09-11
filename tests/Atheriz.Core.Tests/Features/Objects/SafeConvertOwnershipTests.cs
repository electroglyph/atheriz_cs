// Pins for U4 (SafeConvertToTypes caller-ownership): the args array and the
// kwargs dictionary are both consumed in place — converted values overwrite
// the caller's slots/entries, and the returned tuple aliases the inputs.
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

[Collection("Ported")]
public sealed class SafeConvertOwnershipTests
{
    [Fact]
    public void SafeConvertToTypes_ArgsArray_MutatedInPlace()
    {
        using var env = GlobalTestEnv.Enter();
        object?[] args = ["42", "keep"];
        var result = FuncParserHelpers.SafeConvertToTypes(
            (new object?[] { typeof(int) }, new Dictionary<string, object?>()),
            args, [], true);
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
            (Array.Empty<object?>(), new Dictionary<string, object?> { ["n"] = typeof(int) }),
            [], kwargs, true);
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
            (Array.Empty<object?>(), new Dictionary<string, object?>()),
            args, kwargs, true);
        Assert.Same(args, result.args);
        Assert.Same(kwargs, result.kwargs);
        Assert.Equal("a", result.args[0]);
    }
}
