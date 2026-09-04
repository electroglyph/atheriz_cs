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
            var x = root.GetProperty("X").GetInt32();
            var y = root.GetProperty("Y").GetInt32();
            var z = root.GetProperty("Z").GetInt32();
            return new LocationRef.CoordLocation(new Coord(area, x, y, z));
        }
        if (root.TryGetProperty("ObjectId", out var oid))
            return new LocationRef.ObjectLocation(oid.GetInt32());
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
