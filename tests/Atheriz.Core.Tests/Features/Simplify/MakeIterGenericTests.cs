using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Simplify;

// Single generic shape MakeIter<T>(T?): matching values yield one, typed
// sequences pass through, strings never enumerate as char sequences, and
// null yields one default (never empty — callers iterate unconditionally).
public class MakeIterGenericTests
{
    [Fact]
    public void MakeIter_MatchingValue_YieldsSingle()
    {
        Assert.Equal(new[] { 5 }, GameUtils.MakeIter(5));
        Assert.Equal(new[] { "hi" }, GameUtils.MakeIter("hi"));
    }

    [Fact]
    public void MakeIter_Null_YieldsSingleNull()
    {
        var strings = GameUtils.MakeIter<string?>(null).ToList();
        Assert.Single(strings);
        Assert.Null(strings[0]);
        var nullables = GameUtils.MakeIter<int?>(null).ToList();
        Assert.Single(nullables);
        Assert.Null(nullables[0]);
    }

    [Fact]
    public void MakeIter_String_YieldsSingle_NotChars()
    {
        Assert.Equal(new[] { "ab" }, GameUtils.MakeIter("ab"));
    }

    [Fact]
    public void MakeIter_TypedSequence_PassesThrough()
    {
        // Explicit T: inference from the sequence itself would wrap it.
        // (Only element-compatible sequences pass through: a string[] is
        // not an IEnumerable<string> value, so strings always yield single.)
        Assert.Equal(new object[] { 1, 2 }, GameUtils.MakeIter<object>(new object[] { 1, 2 }));
    }

    [Fact]
    public void MakeIter_NoFuncParserCopy_SingleShape()
    {
        // The object?-shaped twin in FuncParserHelpers had zero callers;
        // GameUtils.MakeIter<T> is the only iteration helper left.
        var src = SourceScan.Read("src", "Atheriz.Core", "Objects", "FuncParser", "FuncParserHelpers.cs");
        Assert.DoesNotContain("IsIter", src);
        Assert.DoesNotContain("MakeIter", src);
    }
}
