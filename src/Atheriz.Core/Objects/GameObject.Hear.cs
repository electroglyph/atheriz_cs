// Port of atheriz/objects/base_obj.py:1776 at_hear and related
using Atheriz.Core.Globals;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Objects;

public partial class GameObject
{
    // Port of settings.LOUDNESS_LEVELS
    private static readonly (double threshold, string desc)[] LoudnessLevels = new (double, string)[]
    {
        (20, " nearly inaudible"),
        (40, " faint"),
        (60, ""),
        (80, " loud"),
        (100, " very loud"),
        (120, " extremely loud"),
    };
    // Port of settings.REPLACE_LEVELS
    private static readonly (double threshold, double pct)[] ReplaceLevels = new (double, double)[]
    {
        (1, 95.0),
        (10, 80.0),
        (20, 60.0),
        (30, 40.0),
        (40, 20.0),
        (50, 10.0),
    };

    public virtual (bool ok, GameObject emitter, string desc, string msg, double loudness, bool isSay) AtPreHear(GameObject emitter, string soundDesc, string soundMsg, double loudness, bool isSay)
    {
        // hookable wrapper simplified: just call Hookable if hooks exist
        try { return Hookable<(bool, GameObject, string, string, double, bool)>("at_pre_hear", () => (true, emitter, soundDesc, soundMsg, loudness, isSay), emitter, soundDesc, soundMsg, loudness, isSay); }
        catch { return (true, emitter, soundDesc, soundMsg, loudness, isSay); }
    }

    public virtual (bool ok, GameObject emitter, string desc, string msg, double loudness, bool isSay) AtPreEmitSound(GameObject emitter, string soundDesc, string soundMsg, double loudness, bool isSay)
    {
        try { return Hookable<(bool, GameObject, string, string, double, bool)>("at_pre_emit_sound", () => (true, emitter, soundDesc, soundMsg, loudness, isSay), emitter, soundDesc, soundMsg, loudness, isSay); }
        catch { return (true, emitter, soundDesc, soundMsg, loudness, isSay); }
    }

    // Port of base_obj.py:1776 at_hear — base returns void in Python, but for uniformity return double like Node
    public virtual double AtHear(GameObject emitter, string soundDesc, string soundMsg, double loudness, bool isSay)
    {
        return Hookable("at_hear", () =>
        {
            if (!IsPc) return 0.0;
        var loc = ResolveLocationObject();
        if (loc == null) return 0;
        string adj = " deafening";
        foreach (var (thr, desc) in LoudnessLevels) { if (loudness < thr) { adj = desc; break; } }
        if (isSay && !string.IsNullOrEmpty(soundMsg))
        {
            double replacePct = 0;
            foreach (var (thr, pct) in ReplaceLevels) { if (loudness < thr) { replacePct = pct; break; } }
            if (replacePct > 0)
            {
                soundMsg = GameUtils.WordReplace(soundMsg, replacePct / 100.0);
            }
        }
        var emitterLoc = emitter.ResolveLocationObject();
        // if same location or emitter has no location -> direct
        if (emitterLoc == loc || emitterLoc == null)
        {
            Msg($"You hear something{adj}: {soundDesc}{soundMsg}");
        }
        else
        {
                string zStr = "";
                string dirStr = "";
                try
                {
                    Coord? ec = emitterLoc is Node en ? en.Coord : (Coord?)null;
                    Coord? lc = loc is Node ln ? ln.Coord : (Coord?)null;
                    if (ec != null && lc != null && ec.Value.Area == lc.Value.Area)
                    {
                        var direction = GameUtils.GetDir(lc.Value, ec.Value);
                        int zDiff = ec.Value.Z - lc.Value.Z;
                        zStr = zDiff == 0 ? "" : (zDiff > 0 ? " from above you" : " from below you");
                        if (!string.IsNullOrEmpty(direction)) dirStr = $" to the {direction}";
                    }
            }
            catch { }
            Msg($"You hear something{adj}{zStr}{dirStr}: {soundDesc}{soundMsg}");
        }
            return 0.0;
        }, emitter, soundDesc, soundMsg, loudness, isSay);
    }

    public virtual void AtEmitSound(string soundDesc, string soundMsg, double loudness, bool isSay)
    {
        Hookable<int>("at_emit_sound", () =>
        {
            if (string.IsNullOrEmpty(soundMsg)) return 0;
            var loc = ResolveLocationObject();
            var allow1 = AtPreEmitSound(this, soundDesc, soundMsg, loudness, isSay);
            if (!allow1.ok) return 0;
            soundDesc = allow1.desc; soundMsg = allow1.msg; loudness = allow1.loudness; isSay = allow1.isSay;
            if (loc != null)
            {
                // loc pre emit
                if (loc is Node nodeLoc)
                {
                    var allow2 = nodeLoc.AtPreEmitSound(allow1.emitter, soundDesc, soundMsg, loudness, isSay);
                    if (!allow2.ok) return 0;
                    soundDesc = allow2.desc; soundMsg = allow2.msg; loudness = allow2.loudness; isSay = allow2.isSay;
                }
                // broadcast to contents that can hear in source room
                var contents = loc is Node n ? n.GetContents() : Globals.ObjectRegistry.Get(loc.ContentsSnapshot.ToList());
                foreach (var o in contents)
                {
                    if (!o.CanHear) continue;
                    var pre = o.AtPreHear(allow1.emitter, soundDesc, soundMsg, loudness, isSay);
                    if (!pre.ok) continue;
                    o.AtHear(pre.emitter, pre.desc, pre.msg, pre.loudness, pre.isSay);
                }
                // BFS propagation to neighboring nodes faithful to base_obj.py:1869-1913
                if (loc is Node srcNode)
                {
                    var nh = NodeHandler.GetCurrent() ?? GlobalServices.GetNodeHandler();
                    var c = srcNode.Coord;
                    var area = nh.GetArea(c.Area);
                    if (area != null)
                    {
                        // Determine attenuation at source
                        bool open = false;
                        var doors = nh.GetDoors(c);
                        if (doors != null && doors.Count>0)
                        {
                            foreach (var d in doors.Values) if (!d.Closed) { open=true; break; }
                        }
                        else open = true;
                        double attenuation = open ? srcNode.OpenAttenuation : srcNode.EnclosedAttenuation;
                        double nextLoud = loudness - attenuation;
                        var sourceLocal = (c.X, c.Y, c.Z);
                        var visited = new HashSet<(int,int,int)>{ sourceLocal };
                        var seen = new HashSet<(int,int,int)>{ sourceLocal };
                        var queue = new Queue<(Node node, double loud)>();
                        foreach (var neighbor in area.GetNeighbors(sourceLocal))
                        {
                            if (neighbor != null)
                            {
                                var ncoord = (neighbor.Coord.X, neighbor.Coord.Y, neighbor.Coord.Z);
                                seen.Add(ncoord);
                                queue.Enqueue((neighbor, nextLoud));
                            }
                        }
                        while (queue.Count>0)
                        {
                            var (node, nodeLoud) = queue.Dequeue();
                            var ncoord = (node.Coord.X, node.Coord.Y, node.Coord.Z);
                            if (visited.Contains(ncoord)) continue;
                            visited.Add(ncoord);
                            double ret = node.AtHear(allow1.emitter, soundDesc, soundMsg, nodeLoud, isSay);
                            if (ret > 0)
                            {
                                foreach (var neighbor in area.GetNeighbors(ncoord))
                                {
                                    if (neighbor==null) continue;
                                    var nnc = (neighbor.Coord.X, neighbor.Coord.Y, neighbor.Coord.Z);
                                    if (!seen.Contains(nnc))
                                    {
                                        seen.Add(nnc);
                                        queue.Enqueue((neighbor, ret));
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return 0;
        }, soundDesc, soundMsg, loudness, isSay);
    }

    public void EmitSound(string soundDesc, string soundMsg, double loudness, bool isSay = false)
    {
        try
        {
            var pool = GlobalServices.GetAsyncThreadPool();
            if (pool != null)
            {
                if (!pool.AddTask(() => AtEmitSound(soundDesc, soundMsg, loudness, isSay)))
                {
                    // log warning
                }
                return;
            }
        }
        catch { }
        AtEmitSound(soundDesc, soundMsg, loudness, isSay);
    }

}
