using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atheriz.Core.Persistence.Dto;

/// <summary>
/// Discriminated union for location: either an Object id or a Coord.
/// Mirrors Python's <c>__getstate__</c> converting location/home to int or Coord tuple
/// and <c>resolve_relations</c> second-pass reification.
/// </summary>
[JsonConverter(typeof(LocationRefConverter))]
public abstract record LocationRef
{
    public sealed record ObjectLocation(int ObjectId) : LocationRef;
    public sealed record CoordLocation(Coord Coord) : LocationRef;
    public sealed record NullLocation : LocationRef
    {
        public static readonly NullLocation Instance = new();
    }
}

public sealed class LocationRefConverter : JsonConverter<LocationRef>
{
    public override LocationRef? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return LocationRef.NullLocation.Instance;
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var id))
            return new LocationRef.ObjectLocation(id);

        // Expect object { "Area": "...", "X":..}
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.TryGetProperty("Area", out var areaProp))
        {
            var area = areaProp.GetString() ?? "";
            if (!root.TryGetProperty("X", out var xp) || !root.TryGetProperty("Y", out var yp) || !root.TryGetProperty("Z", out var zp)
                || xp.ValueKind != JsonValueKind.Number || yp.ValueKind != JsonValueKind.Number || zp.ValueKind != JsonValueKind.Number)
                throw new JsonException("Coord location requires numeric X/Y/Z.");
            return new LocationRef.CoordLocation(new Coord(area, xp.GetInt32(), yp.GetInt32(), zp.GetInt32()));
        }
        if (root.TryGetProperty("ObjectId", out var oid))
        {
            // Loud contract: a non-numeric ObjectId is corrupt data, reported as
            // JsonException like the X/Y/Z branch above (not InvalidOperationException).
            if (oid.ValueKind == JsonValueKind.Number && oid.TryGetInt32(out var oidNum))
                return new LocationRef.ObjectLocation(oidNum);
            throw new JsonException("ObjectId location requires a numeric id.");
        }
        return LocationRef.NullLocation.Instance;
    }

    public override void Write(Utf8JsonWriter writer, LocationRef value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case LocationRef.ObjectLocation o:
                writer.WriteNumberValue(o.ObjectId);
                break;
            case LocationRef.CoordLocation c:
                writer.WriteStartObject();
                writer.WriteString("Area", c.Coord.Area);
                writer.WriteNumber("X", c.Coord.X);
                writer.WriteNumber("Y", c.Coord.Y);
                writer.WriteNumber("Z", c.Coord.Z);
                writer.WriteEndObject();
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }
}
