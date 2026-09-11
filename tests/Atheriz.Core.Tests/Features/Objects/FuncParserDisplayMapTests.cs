using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Director-stance {key} substitution renders GameObjects by display name when a
// receiver is present and by plain name otherwise.
[Collection("Ported")]
public class FuncParserDisplayMapTests
{
    [Fact]
    public void Parse_DirectorWithReceiver_UsesDisplayName()
    {
        var hero = GameObject.Create("Hero", isPc: true);
        var looker = GameObject.Create("Looker");
        var mapping = new Dictionary<string, object?> { ["hero"] = hero };

        var result = FuncParser.Parse("{hero} arrives.", null, looker, mapping, false);

        Assert.Equal("Hero (offline) arrives.", result);
    }

    [Fact]
    public void Parse_DirectorWithoutReceiver_UsesPlainName()
    {
        var hero = GameObject.Create("Hero", isPc: true);
        var mapping = new Dictionary<string, object?> { ["hero"] = hero };

        var result = FuncParser.Parse("{hero} arrives.", null, null, mapping, false);

        Assert.Equal("Hero arrives.", result);
    }

    [Fact]
    public void Parse_DirectorNonObjectValue_RendersToString()
    {
        var mapping = new Dictionary<string, object?> { ["count"] = 42, ["name"] = "gate" };

        var result = FuncParser.Parse("{count} {name} open.", null, null, mapping, false);

        Assert.Equal("42 gate open.", result);
    }

    [Fact]
    public void Parse_DirectorMissingKey_LeavesPlaceholder()
    {
        var mapping = new Dictionary<string, object?> { ["known"] = "gate" };

        var result = FuncParser.Parse("{known} and {missing} stay.", null, null, mapping, false);

        Assert.Equal("gate and {missing} stay.", result);
    }

    [Fact]
    public void Parse_DirectorNullValue_RendersEmpty()
    {
        var mapping = new Dictionary<string, object?> { ["x"] = null };

        var result = FuncParser.Parse("a{x}b", null, null, mapping, false);

        Assert.Equal("ab", result);
    }

    [Fact]
    public void Parse_DirectorMixedMap_RendersEachByKind()
    {
        var hero = GameObject.Create("Hero", isPc: true);
        var looker = GameObject.Create("Looker");
        var mapping = new Dictionary<string, object?>
        {
            ["hero"] = hero,
            ["count"] = 3,
        };

        var withReceiver = FuncParser.Parse("{hero} sees {count}.", null, looker, mapping, false);
        var withoutReceiver = FuncParser.Parse("{hero} sees {count}.", null, null, mapping, false);

        Assert.Equal("Hero (offline) sees 3.", withReceiver);
        Assert.Equal("Hero sees 3.", withoutReceiver);
    }
}
