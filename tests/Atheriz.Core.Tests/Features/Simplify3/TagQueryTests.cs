using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Simplify3;

// Tag queries must match set semantics for all/any, including empty input
// and duplicate query tags.
[Collection("Ported")]
public class TagQueryTests
{
    private static GameObject Tagged(params string[] tags)
    {
        var o = GameObject.Create("tagged");
        o.AddTags(tags);
        return o;
    }

    [Fact]
    public void HasTags_AllPresent_ReturnsTrue()
    {
        var o = Tagged("quest", "magic");
        Assert.True(o.HasTags(["quest", "magic"], all: true));
    }

    [Fact]
    public void HasTags_AllWithOneMissing_ReturnsFalse()
    {
        var o = Tagged("quest");
        Assert.False(o.HasTags(["quest", "magic"], all: true));
    }

    [Fact]
    public void HasTags_AnyWithOnePresent_ReturnsTrue()
    {
        var o = Tagged("quest");
        Assert.True(o.HasTags(["quest", "magic"], all: false));
    }

    [Fact]
    public void HasTags_AnyWithNonePresent_ReturnsFalse()
    {
        var o = Tagged("quest");
        Assert.False(o.HasTags(["magic", "rare"], all: false));
    }

    [Fact]
    public void HasTags_EmptyInput_AllIsTrueAnyIsFalse()
    {
        var o = Tagged("quest");
        Assert.True(o.HasTags([], all: true));
        Assert.False(o.HasTags([], all: false));
    }

    [Fact]
    public void HasTags_DuplicateQueryTags_MatchesSingletonSemantics()
    {
        var o = Tagged("quest");
        Assert.True(o.HasTags(["quest", "quest"], all: true));
        Assert.True(o.HasTags(["quest", "quest"], all: false));
        Assert.False(o.HasTags(["magic", "magic"], all: false));
    }

    [Fact]
    public void HasTag_SingleTag_DelegatesToQuery()
    {
        var o = Tagged("quest");
        Assert.True(o.HasTag("quest"));
        Assert.False(o.HasTag("magic"));
    }
}
