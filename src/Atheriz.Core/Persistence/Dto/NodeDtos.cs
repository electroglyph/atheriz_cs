
namespace Atheriz.Core.Persistence.Dto;

// Node persistence DTOs (moved from Globals/NodeHandler.cs per file-organization hygiene).
internal sealed class NodeDto
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
internal sealed class NodeGridDto
{
    public string Area { get; set; } = "";
    public int Z { get; set; }
    public Dictionary<string, NodeDto> Nodes { get; set; } = new();
    public Dictionary<string, JsonElement> Data { get; set; } = new();
}
internal sealed class NodeAreaDto
{
    public string Name { get; set; } = "";
    public string Theme { get; set; } = "";
    public Dictionary<int, NodeGridDto> Grids { get; set; } = new();
    public Dictionary<string, JsonElement> Data { get; set; } = new();
    public HashSet<string>? LinkedAreas { get; set; }
    public NodeArea ToDomain()
    {
        var area=new NodeArea(Name, Theme){ Data=Data, LinkedAreas=LinkedAreas, IsModified=false };
        foreach(var (z,gdto) in Grids)
        {
            var grid=new NodeGrid(gdto.Area, gdto.Z, gdto.Data){ IsModified=false };
            foreach(var kv in gdto.Nodes)
            {
                var nd=kv.Value;
                Node node;
                // Preserve concrete Node subclass via ObjectType (dill-like fidelity) — mirrors GameObject.FromDto __object_type handling
                if (!string.IsNullOrEmpty(nd.ObjectType))
                {
                    // Explicit subtype registry (replaces Type.GetType +
                    // assembly scan + Activator): only registered names
                    // reconstruct; anything else falls through to plain Node.
                    Node? inst = null;
                    try { Node.TryCreatePersistedSubtype(nd.ObjectType!, nd.Coord, out inst); } catch { inst = null; }
                    if (inst is not null)
                    {
                            // Remove from ObjectRegistry the auto-registered instance's temporary id collision
                            try { ObjectRegistry.RemoveObject(inst); } catch (Exception) { }
                            inst.Coord = nd.Coord;
                            HydrateNode(inst, nd, assignName: false);
                            node = inst;
                            grid.Nodes[(nd.Coord.X, nd.Coord.Y)] = node;
                            continue;
                        }
                }
                node = Node.CreateForLoad(nd.Coord);
                HydrateNode(node, nd, assignName: true);
                grid.Nodes[(nd.Coord.X, nd.Coord.Y)] = node;
            }
            area.Grids[z]=grid;
        }
        return area;
    }

    // Shared hydration core for the subtype + plain branches above. The
    // assignName flag is load-bearing: the subtype branch deliberately does NOT
    // assign Name (base Name stays the coord string) and performs
    // registry-collision cleanup at its site, while the plain branch assigns
    // nd.Name. A verbatim shared helper would overwrite subtype Names.
    private static void HydrateNode(Node node, NodeDto nd, bool assignName)
    {
        nd.Migrate();
        if (assignName) node.Name = nd.Name;
        node.Desc = nd.Desc;
        node.Theme = nd.Theme;
        node.Symbol = nd.Symbol;
        node.LegendDesc = nd.LegendDesc;
        node.Links = nd.Links;
        node.Nouns = nd.Nouns;
        node.SetIdRaw(nd.Id);
        IdGenerator.EnsureAtLeast(nd.Id);
        // Restore scripts into the shared base scripts set (typed; was _nodeScripts/_scripts reflection)
        if (nd.Scripts.Count > 0)
            node.RestoreScriptIds(nd.Scripts);
        node.IsModified = false;
    }
}
