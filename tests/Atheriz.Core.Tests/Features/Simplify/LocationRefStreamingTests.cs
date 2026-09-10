using System.Text.Json;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Features.Simplify;

// Streaming Coord-location reads: order-independent like the DOM lookups,
// same branch order (null, number, Area-object, ObjectId-object, Null) and
// byte-identical JsonException messages on corrupt saves.
[Collection("Ported")]
public class LocationRefStreamingTests
{
    private static LocationRef? Read(string json)
        => JsonSerializer.Deserialize<LocationRef>(json);

    private static string Write(LocationRef loc)
        => JsonSerializer.Serialize(loc);

    [Fact]
    public void RoundTrip_CoordLocation()
    {
        var loc = new LocationRef.CoordLocation(new Coord("limbo", 4, 4, 4));
        Assert.Equal(loc, Read(Write(loc)));
    }

    [Fact]
    public void ReorderedKeys_StillParse()
    {
        Assert.Equal(
            new LocationRef.CoordLocation(new Coord("limbo", 1, 2, 3)),
            Read("""{"Z":3,"X":1,"Area":"limbo","Y":2}"""));
    }

    [Fact]
    public void NonNumericXYZ_ThrowsIdenticalMessage()
    {
        var ex = Assert.Throws<JsonException>(() => Read("""{"Area":"a","X":"1","Y":2,"Z":3}"""));
        Assert.Equal("Coord location requires numeric X/Y/Z.", ex.Message);
    }

    [Fact]
    public void MissingXYZ_ThrowsIdenticalMessage()
    {
        var ex = Assert.Throws<JsonException>(() => Read("""{"Area":"a","X":1}"""));
        Assert.Equal("Coord location requires numeric X/Y/Z.", ex.Message);
    }

    [Fact]
    public void NonNumericObjectId_ThrowsIdenticalMessage()
    {
        var ex = Assert.Throws<JsonException>(() => Read("""{"ObjectId":"42"}"""));
        Assert.Equal("ObjectId location requires a numeric id.", ex.Message);
    }

    [Fact]
    public void Shapes_NullNumberEmpty_And_WrongKinds()
    {
        // Root JSON null bypasses the converter (HandleNull is false, as
        // before the streaming rewrite), so it deserializes to C# null —
        // not NullLocation.Instance. Only an explicit {} yields NullLocation.
        Assert.Null(Read("null"));
        Assert.Equal(new LocationRef.ObjectLocation(42), Read("42"));
        Assert.Equal(LocationRef.NullLocation.Instance, Read("{}"));
        Assert.Throws<InvalidOperationException>(() => Read("\"s\""));
        Assert.Throws<InvalidOperationException>(() => Read("[1]"));
        Assert.Throws<InvalidOperationException>(() => Read("true"));
    }

    [Fact]
    public void Truncated_FailsClosedAsJsonException()
    {
        Assert.Throws<JsonException>(() => Read("{\"Area\":\"a"));
    }
}
