// ExtractCoord reads Location only: an Extra Coord element is ignored, and
// a missing/non-coord Location falls back to the limbo origin.
using System.Text.Json;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Converters;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Features.Persistence;

[Collection("Ported")]
public sealed class ExtractCoordDirectTests
{
    [Fact]
    public void ExtractCoord_Location_WinsOverExtraCoord()
    {
        using var env = GlobalTestEnv.Enter();
        var dto = GameObjectDto.Create(2001, "CoordCarrier");
        dto.Location = LocationRef.FromCoord(new Coord("loc", 1, 2, 3));
        dto.Extra["Coord"] = JsonOptions.ToElement(new Coord("stale", 9, 9, 9));
        Assert.Equal(new Coord("loc", 1, 2, 3), GameObjectDtoConverter.ExtractCoord(dto));
    }

    [Fact]
    public void ExtractCoord_ExtraCoordAlone_FallsBackToLimboOrigin()
    {
        using var env = GlobalTestEnv.Enter();
        var dto = GameObjectDto.Create(2002, "CoordStale");
        dto.Extra["Coord"] = JsonOptions.ToElement(new Coord("stale", 9, 9, 9));
        Assert.Equal(new Coord("limbo", 0, 0, 0), GameObjectDtoConverter.ExtractCoord(dto));
    }

    [Fact]
    public void ExtractCoord_MissingExtraCoord_FallsBackToLimboOrigin()
    {
        using var env = GlobalTestEnv.Enter();
        var dto = GameObjectDto.Create(2003, "CoordMissing");
        Assert.Equal(new Coord("limbo", 0, 0, 0), GameObjectDtoConverter.ExtractCoord(dto));
    }
}
