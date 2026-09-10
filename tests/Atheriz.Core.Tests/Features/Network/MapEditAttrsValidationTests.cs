using System.Reflection;
using System.Text.Json;
using Atheriz.Core.Network;

namespace Atheriz.Core.Tests.Features.Network;

// IsAttrs validation without per-call set allocations: the allowed set is
// exactly {bold,italic,underline} and duplicates reject (both list and
// JsonElement-array shapes).
[Collection("Ported")]
public sealed class MapEditAttrsValidationTests
{
    private static readonly MethodInfo IsAttrsMethod =
        typeof(InputFuncs).GetMethod("IsAttrs", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static bool Check(object? v) => (bool)IsAttrsMethod.Invoke(null, [v])!;

    private static JsonElement Je(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void AllowedAttrs_IsExactlyBoldItalicUnderline()
    {
        var field = typeof(InputFuncs).GetField("AllowedAttrs", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var values = ((System.Collections.Generic.IEnumerable<string>)field!.GetValue(null)!).OrderBy(x => x);
        Assert.Equal(["bold", "italic", "underline"], values);
    }

    [Fact]
    public void ListShape_MembershipAndDuplicates()
    {
        Assert.True(Check(new List<object?> { "bold" }));
        Assert.True(Check(new List<object?> { "bold", "italic", "underline" }));
        Assert.True(Check(new List<object?>()));
        Assert.False(Check(new List<object?> { "bold", "bold" }));
        Assert.False(Check(new List<object?> { "italic", "underline", "italic" }));
        Assert.False(Check(new List<object?> { "blink" }));
        Assert.False(Check(new List<object?> { "bold", "blink" }));
        Assert.False(Check(new List<object?> { 5 }));
        Assert.False(Check(new List<object?> { null }));
    }

    [Fact]
    public void ListShape_NonListInputs_Rejected()
    {
        Assert.False(Check("bold"));
        Assert.False(Check(null));
        Assert.False(Check(5));
    }

    [Fact]
    public void JsonArrayShape_MembershipAndDuplicates()
    {
        Assert.True(Check(Je("[\"bold\",\"italic\"]")));
        Assert.True(Check(Je("[]")));
        Assert.False(Check(Je("[\"bold\",\"bold\"]")));
        Assert.False(Check(Je("[\"bold\",\"blink\"]")));
        Assert.False(Check(Je("[null]")));
        Assert.False(Check(Je("\"bold\"")));
    }
}
