using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atheriz.Core.Globals;

// ---------------------------------------------------------------------------
// LegendEntry — mirrors atheriz/globals/map.py:LegendEntry
// ---------------------------------------------------------------------------

/// <summary>
/// Symbol/desc/coord/show/fg/bg faithful.
/// </summary>
public sealed class TupleCoordConverter : JsonConverter<(int X, int Y)?>
{
    public override (int X, int Y)? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            reader.Read();
            if (reader.TokenType == JsonTokenType.EndArray) return null;
            int x = reader.GetInt32();
            reader.Read();
            // A coord is exactly [x, y]: a singleton would invent y=0 and
            // extras would silently truncate, so both throw like the
            // fallthrough below instead of fabricating a coordinate.
            if (reader.TokenType == JsonTokenType.EndArray)
                throw new JsonException("Coord array must have exactly 2 elements ([x, y]); got 1.");
            int y = reader.GetInt32();
            reader.Read();
            if (reader.TokenType != JsonTokenType.EndArray)
                throw new JsonException("Coord array must have exactly 2 elements ([x, y]); extras are not allowed.");
            return (x, y);
        }
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            int? x=null, y=null;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) break;
                if (reader.TokenType != JsonTokenType.PropertyName) continue;
                string prop = reader.GetString()!;
                reader.Read();
                if (prop == "Item1" || prop == "X" || prop == "x") x = reader.GetInt32();
                else if (prop == "Item2" || prop == "Y" || prop == "y") y = reader.GetInt32();
                else reader.Skip();
            }
            if (x.HasValue && y.HasValue) return (x.Value, y.Value);
            return null;
        }
        throw new JsonException($"Unexpected token {reader.TokenType} for nullable coord; expected null, array, or object.");
    }
    public override void Write(Utf8JsonWriter writer, (int X, int Y)? value, JsonSerializerOptions options)
    {
        if (value is null) { writer.WriteNullValue(); return; }
        writer.WriteStartArray();
        writer.WriteNumberValue(value.Value.X);
        writer.WriteNumberValue(value.Value.Y);
        writer.WriteEndArray();
    }
}
