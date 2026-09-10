using Atheriz.Core;
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;
namespace Atheriz.Core.Tests.Features.Commands;

// SearchWithFallback accepts only the narrow comma-form coord: optional
// outer parens, four comma-separated parts, last three ints. Space-separated
// "Area 1 2 3" (no comma) must keep falling through to the name search.
[Collection("Ported")]
public sealed class CommaCoordSearchTests
{
    [Fact]
    public void TryParseCommaCoord_SpaceForm_IsNotACoord()
    {
        Assert.False(CommandHelpers.TryParseCommaCoord("pinarea 1 2 3", out _));
    }

    [Fact]
    public void TryParseCommaCoord_CommaForms_AreCoords()
    {
        Assert.True(CommandHelpers.TryParseCommaCoord("(pinarea,1,2,3)", out var a));
        Assert.Equal(new Coord("pinarea", 1, 2, 3), a);
        Assert.True(CommandHelpers.TryParseCommaCoord("pinarea,1,2,3", out var b));
        Assert.Equal(new Coord("pinarea", 1, 2, 3), b);
        Assert.False(CommandHelpers.TryParseCommaCoord("(pinarea,1,2)", out _));
        Assert.False(CommandHelpers.TryParseCommaCoord("(pinarea,1,2,x)", out _));
    }

    [Fact]
    public void SearchWithFallback_CommaForm_FindsNode()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("pinarea", 1, 2, 3));
        ObjectRegistry.AddObject(node);
        var caller = new GameObject { Name = "Hero" };
        var found = CommandHelpers.SearchWithFallback(caller, "(pinarea,1,2,3)");
        Assert.Single(found);
        Assert.Same(node, found[0]);
    }

    [Fact]
    public void SearchWithFallback_SpaceForm_StaysNameSearch()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("pinarea", 1, 2, 3));
        ObjectRegistry.AddObject(node);
        var caller = new GameObject { Name = "Hero" };
        // The node exists, but without a comma the input is a name search
        // and must not resolve to it.
        var found = CommandHelpers.SearchWithFallback(caller, "pinarea 1 2 3");
        Assert.DoesNotContain(node, found);
    }
}
