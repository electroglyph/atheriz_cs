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

    /// <summary>
    /// Single construction point for coord-backed locations. Builds the same
    /// record as the constructor — same values, same coordinate order, same
    /// serialized JSON — so all call sites share one spelling.
    /// </summary>
    public static CoordLocation FromCoord(Coord coord) => new CoordLocation(coord);
}

public sealed class LocationRefConverter : JsonConverter<LocationRef>
{
    private const string NumericCoordRequired = "Coord location requires numeric X/Y/Z.";

    public override LocationRef? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return LocationRef.NullLocation.Instance;
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var id))
            return new LocationRef.ObjectLocation(id);

        // Streaming object scan (no JsonDocument DOM per Coord-location read
        // on the hot load path). Order-independent like the TryGetProperty
        // lookups it replaces: all properties are collected first, then the
        // same branch order decides (Area-object, ObjectId-object,
        // NullLocation) with the identical JsonException messages.
        // Non-object JSON mirrors the DOM throw (InvalidOperationException
        // from TryGetProperty on a non-object), never silent NullLocation.
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new InvalidOperationException(
                $"The requested operation requires an element of type 'Object', but the target element has type '{reader.TokenType}'.");
        string area = "";
        bool hasArea = false;
        bool hasObjectId = false;
        int objectId = 0;
        bool objectIdOk = false;
        int x = 0, y = 0, z = 0;
        bool xOk = false, yOk = false, zOk = false;
        bool complete = false;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) { complete = true; break; }
            if (reader.TokenType != JsonTokenType.PropertyName) continue;
            string? name = reader.GetString();
            if (!reader.Read()) break;
            switch (name)
            {
                case "Area":
                    hasArea = true;
                    if (reader.TokenType == JsonTokenType.String) area = reader.GetString() ?? "";
                    else if (reader.TokenType == JsonTokenType.Null) area = "";
                    else throw new InvalidOperationException(
                        $"The requested operation requires an element of type 'String', but the target element has type '{reader.TokenType}'.");
                    break;
                case "X":
                    if (reader.TokenType != JsonTokenType.Number) throw new JsonException(NumericCoordRequired);
                    x = reader.GetInt32();
                    xOk = true;
                    break;
                case "Y":
                    if (reader.TokenType != JsonTokenType.Number) throw new JsonException(NumericCoordRequired);
                    y = reader.GetInt32();
                    yOk = true;
                    break;
                case "Z":
                    if (reader.TokenType != JsonTokenType.Number) throw new JsonException(NumericCoordRequired);
                    z = reader.GetInt32();
                    zOk = true;
                    break;
                case "ObjectId":
                    hasObjectId = true;
                    objectIdOk = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out objectId);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
        // Truncated JSON never degrades to a silent NullLocation: the DOM
        // parse threw JsonException here too (fail-closed, same type).
        if (!complete) throw new JsonException("Unexpected end of location object.");
        if (hasArea)
        {
            if (!xOk || !yOk || !zOk)
                throw new JsonException(NumericCoordRequired);
            return LocationRef.FromCoord(new Coord(area, x, y, z));
        }
        if (hasObjectId)
        {
            // Loud contract: a non-numeric ObjectId is corrupt data, reported as
            // JsonException like the X/Y/Z branch above (not InvalidOperationException).
            if (objectIdOk)
                return new LocationRef.ObjectLocation(objectId);
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
