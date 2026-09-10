using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify;

// Director-stance formatting: one shared compiled pattern; missing keys and
// null values pass through untouched.
[Collection("Ported")]
public class SafeFormatMapTests
{
    [Fact]
    public void Format_SubstitutesPresentKeys_AndKeepsMissing()
    {
        var map = new FuncParserHelpers.SafeFormatMap { ["a"] = 1, ["b"] = null };

        Assert.Equal("1 and {b} and {c}!", map.Format("{a} and {b} and {c}!"));
    }

    [Fact]
    public void Format_EmptyOrPlain_PassesThrough()
    {
        var map = new FuncParserHelpers.SafeFormatMap();

        Assert.Equal("", map.Format(""));
        Assert.Equal("no keys here", map.Format("no keys here"));
    }
}
