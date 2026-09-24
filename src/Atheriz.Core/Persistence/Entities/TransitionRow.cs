using Atheriz.Core.Persistence;

namespace Atheriz.Core.Persistence.Entities;

public sealed class TransitionRow : IJsonEntity
{
    // Composite PK is (From*, To*): fan-in edges to one destination are
    // distinct rows . Data carries the full Transition JSON.
    public string FromArea { get; set; } = "";
    public int FromX { get; set; }
    public int FromY { get; set; }
    public int FromZ { get; set; }
    public string ToArea { get; set; } = "";
    public int ToX { get; set; }
    public int ToY { get; set; }
    public int ToZ { get; set; }
    public string Data { get; set; } = "";
}
