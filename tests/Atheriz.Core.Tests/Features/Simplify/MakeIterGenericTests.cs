using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Simplify;

// Dead try/cast removal in MakeIter<T>: non-matching non-null values yield an
// empty sequence (never throw), and the string exclusion is preserved.
public class MakeIterGenericTests
{
    [Fact]
    public void MakeIter_MatchingValue_YieldsSingle()
    {
        Assert.Equal(new[] { 5 }, GameUtils.MakeIter<int>(5));
        Assert.Equal(new[] { "hi" }, GameUtils.MakeIter<string>("hi"));
    }

    [Fact]
    public void MakeIter_Null_YieldsSingleDefault()
    {
        Assert.Equal(new int[] { 0 }, GameUtils.MakeIter<int>(null));
        var strings = GameUtils.MakeIter<string>(null).ToList();
        Assert.Single(strings);
        Assert.Null(strings[0]);
    }

    [Fact]
    public void MakeIter_NonMatchingValue_YieldsEmptyWithoutThrowing()
    {
        Assert.Empty(GameUtils.MakeIter<int>("hi"));
        Assert.Empty(GameUtils.MakeIter<int>(new object()));
    }

    [Fact]
    public void MakeIter_StringSequence_ExcludedFromCharEnumeration()
    {
        Assert.Empty(GameUtils.MakeIter<char>("ab"));
    }

    [Fact]
    public void MakeIter_TypedSequence_PassesThrough()
    {
        Assert.Equal(new[] { 1, 2 }, GameUtils.MakeIter<int>(new[] { 1, 2 }));
    }
}
