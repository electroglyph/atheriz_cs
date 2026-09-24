using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Objects;

public sealed record DoorDto
{
    public Coord FromCoord { get; set; }
    public string FromExit { get; set; } = "";
    public Coord ToCoord { get; set; }
    public string ToExit { get; set; } = "";
    public (int X, int Y)? SymbolCoord { get; set; }
    public string ClosedSymbol { get; set; } = "";
    public string OpenSymbol { get; set; } = "";
    public bool Closed { get; set; } = true;
    public bool Locked { get; set; } = false;
    public string Name { get; set; } = "";
    public string Desc { get; set; } = "";
    public int? KeyId { get; set; }
    // Persisted lock policies as typed LockDefDto rows (mirrors GameObject
    // Locks; bare-lambda "custom" entries are dropped with a loud log).
    public List<LockDefDto> Locks { get; set; } = [];
}
