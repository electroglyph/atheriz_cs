
namespace Atheriz.Core.Objects;

public sealed class NodeLink
{
    public string Name { get; set; } = "";
    public Coord Coord { get; set; }
    // Snapshot copy: readers mutate the copy freely (AddExits hands the list
    // to ExitCommand), never the link's live list.
    private List<string> _aliases = [];
    public List<string> Aliases
    {
        get => [.. _aliases];
        set => _aliases = value is null ? [] : [.. value];
    }
    public NodeLink() { }
    public NodeLink(string name, Coord coord, List<string>? aliases = null)
    {
        Name = name;
        Coord = coord;
        Aliases = aliases ?? [];
    }
    public override bool Equals(object? obj) => obj is NodeLink o && Name == o.Name && Coord.Equals(o.Coord);
    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Name);
        h.Add(Coord);
        return h.ToHashCode();
    }
    public override string ToString() => $"NodeLink: {Name}, [{string.Join(",", Aliases)}], {Coord}";
}
