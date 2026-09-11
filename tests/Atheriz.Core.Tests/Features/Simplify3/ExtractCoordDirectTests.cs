// Pins for the ExtractCoord direct-deserialize path: an Extra Coord element
// (camelCase payload from the shared options) parses without a string
// round-trip, while corrupt or missing Coord data still falls back to the
// limbo origin with the same error log.
using System.Text.Json;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Converters;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class ExtractCoordDirectTests
{
    [Fact]
    public void ExtractCoord_ExtraCoordElement_RoundTripsCoord()
    {
        using var env = GlobalTestEnv.Enter();
        var dto = GameObjectDto.Create(2001, "CoordCarrier");
        var coord = new Coord("roundtrip", 4, 5, 6);
        dto.Extra["Coord"] = JsonOptions.ToElement(coord);
        Assert.Equal(coord, GameObjectDtoConverter.ExtractCoord(dto));
    }

    [Fact]
    public void ExtractCoord_CorruptExtraCoord_FallsBackToLimboOrigin()
    {
        using var env = GlobalTestEnv.Enter();
        var dto = GameObjectDto.Create(2002, "CoordCorrupt");
        dto.Extra["Coord"] = JsonDocument.Parse("\"not-a-coord\"").RootElement.Clone();
        string log;
        Coord got;
        using (var cap = new CaptureAtherizLog())
        {
            got = GameObjectDtoConverter.ExtractCoord(dto);
            log = cap.Read();
        }
        Assert.Equal(new Coord("limbo", 0, 0, 0), got);
        Assert.Contains("Bad Extra Coord for object 2002", log);
    }

    [Fact]
    public void ExtractCoord_MissingExtraCoord_FallsBackToLimboOrigin()
    {
        using var env = GlobalTestEnv.Enter();
        var dto = GameObjectDto.Create(2003, "CoordMissing");
        Assert.Equal(new Coord("limbo", 0, 0, 0), GameObjectDtoConverter.ExtractCoord(dto));
    }
}
