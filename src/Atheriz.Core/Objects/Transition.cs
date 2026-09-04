namespace Atheriz.Core.Objects;

// Port of atheriz/objects/nodes.py:1426 Transition
public sealed class Transition
{
    public Coord FromCoord { get; set; }
    public Coord ToCoord { get; set; }
    public string Name { get; set; } = ""; // from_link
    public string FromLink { get => Name; set => Name = value; }
    public readonly ReaderWriterLockSlim Lock = new(LockRecursionPolicy.SupportsRecursion);

    public Transition() { }
    // Port of nodes.py:1428
    public Transition(Coord from, Coord to, string name)
    {
        FromCoord = from;
        ToCoord = to;
        Name = name;
    }
    public override string ToString() => $"Transition({FromCoord} -> {ToCoord}, '{Name}')";
}
