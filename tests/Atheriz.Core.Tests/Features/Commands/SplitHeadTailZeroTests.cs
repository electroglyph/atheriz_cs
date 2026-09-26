using Atheriz.Core.Commands;

namespace Atheriz.Core.Tests.Features.Commands;

// Audit 10 finding 7: SplitHeadTail(text, 0) indexed ends[-1]. Zero head
// tokens now selects an empty head with the whole text as the tail.
public sealed class SplitHeadTailZeroTests
{
    [Fact]
    public void SplitHeadTail_ZeroHeadCount_ReturnsWholeTextAsTail()
    {
        var (head, tail) = Command.SplitHeadTail("a b c", 0);
        Assert.Empty(head);
        Assert.Equal("a b c", tail);
    }

    [Fact]
    public void SplitHeadTail_ZeroHeadCount_EmptyText_ReturnsEmpty()
    {
        var (head, tail) = Command.SplitHeadTail("", 0);
        Assert.Empty(head);
        Assert.Equal("", tail);
    }

    [Fact]
    public void SplitHeadTail_OneHeadCount_StillSplits()
    {
        var (head, tail) = Command.SplitHeadTail("a b c", 1);
        Assert.Equal(["a"], head);
        Assert.Equal("b c", tail);
    }
}
