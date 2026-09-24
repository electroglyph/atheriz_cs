
namespace Atheriz.Core.Persistence.Dto;

// Node persistence DTOs (moved from Globals/NodeHandler.cs per file-organization hygiene).
internal sealed record NodeDto
{
    public Coord Coord { get; set; }
    public string Name { get; set; } = "";
    public string Desc { get; set; } = "";
    public string Theme { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string? LegendDesc { get; set; }
    public List<NodeLink> Links { get; set; } = [];
    public Dictionary<string,string> Nouns { get; set; } = new();
    public int Id { get; set; }
    public HashSet<int> Scripts { get; set; } = new();
    public string? ObjectType { get; set; }

    // Centralized null backfill mirroring GameObjectDtoSerializer.Migrate: an
    // explicit JSON null overwrites the property initializers above, and old
    // saves may omit collections — restore defaults instead of throwing downstream.
    // Non-null collections keep their deserialized comparer (only nulls backfill).
    internal void Migrate()
    {
        Name ??= "";
        Desc ??= "";
        Theme ??= "";
        Symbol ??= "";
        Links ??= [];
        Nouns ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Scripts ??= [];
    }
}
