using System.Text.Json.Serialization;

namespace Atheriz.Core.Globals;

public sealed record LegendEntry
{
    public string? Symbol { get; set; }
    public string? Desc { get; set; }
    [JsonConverter(typeof(TupleCoordConverter))]
    public (int X, int Y)? Coord { get; set; }
    public bool Show { get; set; } = true;
    public double Fg { get; set; } = 170.0;
    public double? Bg { get; set; }
    // RGB-triple form of the colors: set when the entry arrives as [r,g,b]
    // (the validator accepts both scalar hues and triples). Null for scalar
    // entries; the scalar Fg/Bg stay at their defaults then.
    public List<int>? FgRgb { get; set; }
    public List<int>? BgRgb { get; set; }

    public LegendEntry() { }
    public LegendEntry(string? symbol = null, string? desc = null, (int X, int Y)? coord = null)
    {
        Symbol = symbol;
        Desc = desc;
        Coord = coord;
        Show = true;
        Fg = 170.0;
        Bg = null;
    }

    // Direct JSON round-trip: serializes the record itself instead of
    // unrolling to a dict first.
    public string ToJson() => JsonSerializer.Serialize(this, Persistence.JsonOptions.Default);
    public static LegendEntry FromJson(string json) =>
        JsonSerializer.Deserialize<LegendEntry>(json, Persistence.JsonOptions.Default)
        ?? throw new InvalidDataException("Failed to deserialize LegendEntry");
    public static LegendEntry FromJsonElement(JsonElement el) =>
        el.Deserialize<LegendEntry>(Persistence.JsonOptions.Default)
        ?? throw new InvalidDataException("Failed to deserialize LegendEntry");
}
