// Pins for the shared corrupt-coord message in LocationRefConverter: the X, Y,
// Z arms and the missing-coordinate tail all throw the identical JsonException
// text (single contract), while the two InvalidOperationException messages
// stay distinct per element kind.
using System.Text.Json;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class LocationRefConstMessageTests
{
    private static JsonException ReadThrows(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LocationRef>(json));

    [Fact]
    public void Read_NonNumericX_ThrowsNumericCoordMessage()
    {
        var ex = ReadThrows("""{"Area":"a","X":"1","Y":2,"Z":3}""");
        Assert.Equal("Coord location requires numeric X/Y/Z.", ex.Message);
    }

    [Fact]
    public void Read_NonNumericY_ThrowsNumericCoordMessage()
    {
        var ex = ReadThrows("""{"Area":"a","X":1,"Y":"2","Z":3}""");
        Assert.Equal("Coord location requires numeric X/Y/Z.", ex.Message);
    }

    [Fact]
    public void Read_NonNumericZ_ThrowsNumericCoordMessage()
    {
        var ex = ReadThrows("""{"Area":"a","X":1,"Y":2,"Z":"3"}""");
        Assert.Equal("Coord location requires numeric X/Y/Z.", ex.Message);
    }

    [Fact]
    public void Read_MissingCoordinates_ThrowsNumericCoordMessage()
    {
        var ex = ReadThrows("""{"Area":"a","X":1}""");
        Assert.Equal("Coord location requires numeric X/Y/Z.", ex.Message);
    }

    [Fact]
    public void Read_WrongElementKinds_InvalidOperationMessagesStayDistinct()
    {
        var objEx = Assert.Throws<InvalidOperationException>(() => JsonSerializer.Deserialize<LocationRef>("\"s\""));
        var strEx = Assert.Throws<InvalidOperationException>(() => JsonSerializer.Deserialize<LocationRef>("""{"Area":42,"X":1,"Y":2,"Z":3}"""));
        Assert.Contains("'Object'", objEx.Message);
        Assert.Contains("'String'", strEx.Message);
        Assert.NotEqual(objEx.Message, strEx.Message);
    }
}
