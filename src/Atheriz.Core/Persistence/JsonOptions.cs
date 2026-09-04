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
}
