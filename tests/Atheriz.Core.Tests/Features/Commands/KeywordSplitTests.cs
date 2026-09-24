using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// to/from/in keyword splits partition the token list on the first
// case-insensitive match and keep later copies on the right.
[Collection("Ported")]
public sealed class KeywordSplitTests
{
    [Fact]
    public void KeywordSplit_FirstTo_WinsAndKeepsRest()
    {
        // Give's 'to' split takes the FIRST keyword case-insensitively and
        // keeps any later copies in the target half.
        Assert.True(TargetResolution.SplitOnFirstKeyword(
            ["sword", "TO", "bob", "to", "x"], out var before, out var after, "to"));
        Assert.Equal(["sword"], before);
        Assert.Equal(["bob", "to", "x"], after);
        Assert.False(TargetResolution.SplitOnFirstKeyword(
            ["sword"], out _, out _, "to", "from"));
    }
}
