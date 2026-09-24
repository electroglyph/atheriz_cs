
namespace Atheriz.Core.Persistence.Dto;

internal sealed class NodeGridDto
{
    public string Area { get; set; } = "";
    public int Z { get; set; }
    public Dictionary<string, NodeDto> Nodes { get; set; } = new();
    public Dictionary<string, JsonElement> Data { get; set; } = new();
}
