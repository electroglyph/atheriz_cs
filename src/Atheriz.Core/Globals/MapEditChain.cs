using Atheriz.Core;

namespace Atheriz.Core.Globals;

public sealed class MapEditChain
{
    public string Key { get; set; }
    public string PreviousKey { get; set; } = "";
    public int Seq { get; set; } = -1;
    public string Ip { get; set; }
    public string Area { get; set; }
    public int Z { get; set; }
    public List<int>? Validation { get; set; }
    // In C# we keep both monotonic seconds and wall DateTime for spec's CreatedAt
    public double CreatedMonotonic { get; set; }
    public DateTime CreatedAt { get; set; }
    // Owning game session. A chain is valid for as long as this session is
    // open (see DiscardSession); null (e.g. tests) lives until cap eviction.
    public Session? Session { get; set; }
    public List<Coord> Chain { get; set; } = new();

    public MapEditChain(string key, string ip, string area, int z, Session? session = null)
        : this(key, ip, area, z, session, stamp: true)
    {
    }

    // Copy paths use stamp:false and restore the source stamps: stamping here
    // just to overwrite burns two clock reads per copy.
    internal MapEditChain(string key, string ip, string area, int z, Session? session, bool stamp)
    {
        Key = key;
        Ip = ip;
        Area = area;
        Z = z;
        Session = session;
        if (stamp)
        {
            CreatedAt = DateTime.UtcNow;
            CreatedMonotonic = MapEdit.GetMonotonic();
        }
        Chain = [];
    }
}
