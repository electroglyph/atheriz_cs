using Atheriz.Core.Persistence;

namespace Atheriz.Core.Persistence.Entities;

public sealed class DoorRow : IJsonEntity
{
    public string Area { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public string Data { get; set; } = "";
}
