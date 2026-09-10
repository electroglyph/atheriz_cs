using System.Text.Json;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Extra-JSON lookup keeps the missing-key vs explicit-null distinction: a
// missing key reports absent, while a stored JSON null reports present with
// Null-kind value.
[Collection("Ported")]
public class ExtraJsonNullTests
{
    [Fact]
    public void MissingKey_ReturnsFalse()
    {
        var o = GameObject.Create("keeper");
        Assert.False(o.TryGetExtraJson("nope", out _));
    }

    [Fact]
    public void ExplicitNull_ReturnsTrueWithNullKind()
    {
        var o = GameObject.Create("keeper");
        o.SetExtraJson("nada", JsonSerializer.SerializeToElement((object?)null));
        Assert.True(o.TryGetExtraJson("nada", out var value));
        Assert.Equal(JsonValueKind.Null, value.ValueKind);
    }

    [Fact]
    public void StoredValue_RoundTrips()
    {
        var o = GameObject.Create("keeper");
        o.SetExtraJson("greeting", JsonSerializer.SerializeToElement("hi"));
        Assert.True(o.TryGetExtraJson("greeting", out var value));
        Assert.Equal("hi", value.GetString());
        Assert.True(o.TryRemoveExtraJson("greeting"));
        Assert.False(o.TryGetExtraJson("greeting", out _));
    }
}
