using System.Reflection;
using System.Text.Json;
using Atheriz.Core.Network;

namespace Atheriz.Core.Tests.Features.Network;

// IsFg/IsBg share one list-triple RGB check (differing only in the -1
// allowance) while IsColor stays a separate validator with its own accept
// table (rejects null/scalars, handles JsonElement arrays).
[Collection("Ported")]
public sealed class MapEditRgbValidationTests
{
    private static readonly MethodInfo IsLegendEntryMethod =
        typeof(InputFuncs).GetMethod("IsLegendEntry", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo IsColorMethod =
        typeof(InputFuncs).GetMethod("IsColor", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static JsonElement Je(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static bool CheckEntry(object? fg, object? bg)
    {
        var dict = new Dictionary<string, object?> { ["symbol"] = "@", ["fg"] = fg, ["bg"] = bg };
        return (bool)IsLegendEntryMethod.Invoke(null, [dict])!;
    }

    private static bool CheckColor(object? v) => (bool)IsColorMethod.Invoke(null, [v])!;

    [Fact]
    public void LegendEntry_NullAndScalars_AcceptedForFgAndBg()
    {
        Assert.True(CheckEntry(null, null));
        Assert.True(CheckEntry(5, 1.5));
        Assert.True(CheckEntry(Je("3"), Je("null")));
    }

    [Fact]
    public void LegendEntry_JsonElementArrays_RejectedForFgAndBg()
    {
        // Neither local has a JsonElement-array branch: arrays fall to false.
        Assert.False(CheckEntry(Je("[0,0,0]"), null));
        Assert.False(CheckEntry(null, Je("[0,0,0]")));
    }

    [Fact]
    public void LegendEntry_FullTransparentTriple_AcceptedBoth()
    {
        var triple = new List<object?> { -1, -1, -1 };
        Assert.True(CheckEntry(triple, triple));
    }

    [Fact]
    public void LegendEntry_InRangeTriples_AcceptedBoth()
    {
        Assert.True(CheckEntry(new List<object?> { 0, 0, 0 }, new List<object?> { 255, 255, 255 }));
    }

    [Fact]
    public void LegendEntry_PartialTransparent_FgRejects_BgAccepts()
    {
        // The single load-bearing difference between the two validators.
        Assert.False(CheckEntry(new List<object?> { -1, 0, 0 }, null));
        Assert.True(CheckEntry(null, new List<object?> { -1, 0, 0 }));
        Assert.False(CheckEntry(new List<object?> { -1, -1, 0 }, null));
        Assert.True(CheckEntry(null, new List<object?> { -1, -1, 0 }));
    }

    [Fact]
    public void LegendEntry_OutOfRange_RejectedBoth()
    {
        Assert.False(CheckEntry(new List<object?> { 256, 0, 0 }, null));
        Assert.False(CheckEntry(null, new List<object?> { 0, 0, 256 }));
        Assert.False(CheckEntry(new List<object?> { -2, 0, 0 }, null));
        Assert.False(CheckEntry(null, new List<object?> { 0, -2, 0 }));
    }

    [Fact]
    public void LegendEntry_WrongShapes_Rejected()
    {
        Assert.False(CheckEntry("red", null));
        Assert.False(CheckEntry(null, "red"));
        Assert.False(CheckEntry(new List<object?> { 0, 0 }, null));
        Assert.False(CheckEntry(null, new List<object?> { 0, 0, 0, 0 }));
    }

    [Fact]
    public void IsColor_KeepsOwnAcceptTable()
    {
        // IsColor was NOT merged: null/scalars reject, JsonElement arrays work.
        Assert.False(CheckColor(null));
        Assert.False(CheckColor(5));
        Assert.False(CheckColor(1.5));
        Assert.True(CheckColor(Je("[0,0,0]")));
        Assert.True(CheckColor(Je("[-1,-1,-1]")));
        Assert.False(CheckColor(Je("[0,0]")));
        Assert.True(CheckColor(new List<object?> { -1, -1, -1 }));
        Assert.True(CheckColor(new List<object?> { 1, 2, 3 }));
    }
}
