
namespace Atheriz.Core.Persistence.Dto;

internal sealed record NodeAreaDto
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
                Node? inst = null;
                if (!string.IsNullOrEmpty(nd.ObjectType))
                {
                    // Explicit subtype registry (replaces Type.GetType +
                    // assembly scan + Activator): only registered names
                    // reconstruct; anything else falls through to plain Node.
                    try { Node.TryCreatePersistedSubtype(nd.ObjectType!, nd.Coord, out inst); } catch { inst = null; }
                }
                if (inst is not null)
                {
                    // Remove from ObjectRegistry the auto-registered instance's temporary id collision
                    try { ObjectRegistry.RemoveObject(inst); } catch (Exception) { }
                    inst.Coord = nd.Coord;
                    HydrateNode(inst, nd);
                    node = inst;
                }
                else
                {
                    node = Node.CreateForLoad(nd.Coord);
                    HydrateNode(node, nd);
                }
                grid.AddNodeRaw(node);
            }
            area.AddGridRaw(grid);
        }
        return area;
    }

    // Shared hydration core for the subtype + plain branches above. The
    // persisted nd.Name is authoritative in both branches: the subtype factory
    // only rebuilds the concrete type at its coord (it cannot restore a custom
    // Name), so skipping the assignment here loses renames on reload.
    private static void HydrateNode(Node node, NodeDto nd)
    {
        nd.Migrate();
        node.Name = nd.Name;
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
