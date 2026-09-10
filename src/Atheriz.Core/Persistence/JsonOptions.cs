// Port of atheriz/globals/* JSON persistence (replaces dill) — single shared options
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atheriz.Core.Persistence;

/// <summary>
/// Single shared <see cref="JsonSerializerOptions"/> for all JSON table persistence.
/// Mirrors previous per-handler duplicates (NodeHandler.WriteIndented false, MapHandler/GameTime CamelCase).
/// </summary>
public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Single choke point for persisting a value as a <see cref="JsonElement"/>:
    /// avoids the string round-trip of <c>Parse(Serialize(x))</c> while using the
    /// same options instance as every other persisted payload.
    /// </summary>
    public static JsonElement ToElement<T>(T value)
        => JsonSerializer.SerializeToElement(value, Default);
}
