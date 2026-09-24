using Atheriz.Core.Persistence;

namespace Atheriz.Core.Persistence.Entities;

public sealed class MapDataRow : IJsonEntity
{
    public string Area { get; set; } = "";
    public int Z { get; set; }
    public string Data { get; set; } = "";
}
