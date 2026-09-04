// Port of atheriz/tests/test_sound_propagation.py:1
// Port of atheriz/tests/test_node_sphere.py:1
using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedSoundTests
{
    const string AREA = "bfs_sound_test";
    const int GRID = 9;
    private class TrackingNode : Node
    {
        public List<(GameObject emitter, string desc, string msg, double loud, bool isSay)> Heard = new();
        public TrackingNode(Coord c) : base(c) { }
        public override double AtHear(GameObject emitter, string soundDesc, string soundMsg, double loudness, bool isSay)
        {
            Heard.Add((emitter, soundDesc, soundMsg, loudness, isSay));
            return base.AtHear(emitter, soundDesc, soundMsg, loudness, isSay);
        }
        public override (bool ok, GameObject emitter, string desc, string msg, double loudness, bool isSay) AtPreHear(GameObject emitter, string soundDesc, string soundMsg, double loudness, bool isSay) => base.AtPreHear(emitter, soundDesc, soundMsg, loudness, isSay);
    }
    private class BlockingNode : TrackingNode
    {
        public BlockingNode(Coord c) : base(c) { }
        public override (bool ok, GameObject emitter, string desc, string msg, double loudness, bool isSay) AtPreHear(GameObject emitter, string soundDesc, string soundMsg, double loudness, bool isSay) => (false, emitter, soundDesc, soundMsg, loudness, isSay);
    }
    private class TrackingObject : GameObject
    {
        public List<(GameObject emitter, string desc, string msg, double loud, bool isSay)> Heard = new();
        public override double AtHear(GameObject emitter, string soundDesc, string soundMsg, double loudness, bool isSay)
        {
            Heard.Add((emitter, soundDesc, soundMsg, loudness, isSay));
            return base.AtHear(emitter, soundDesc, soundMsg, loudness, isSay);
        }
    }
    private static (NodeHandler nh, NodeArea area) MakeCube(string name=AREA, int grid=GRID)
    {
        var nh = GlobalServices.GetNodeHandler(); NodeHandler.SetCurrent(nh);
        var area = new NodeArea(name);
        for(int z=0; z<grid; z++)
        {
            var g = new NodeGrid(name,z);
            for(int x=0;x<grid;x++) for(int y=0;y<grid;y++) g.Nodes[(x,y)] = new TrackingNode(new Coord(name,x,y,z));
            area.AddGrid(g);
        }
        nh.AddArea(area);
        return (nh, area);
    }
    private static (TrackingObject emit, TrackingNode node) Place(NodeHandler nh, Coord coord)
    {
        var node = (TrackingNode)nh.GetNode(coord)!;
        var emitter = new TrackingObject();
        emitter.Name = "Emitter"; emitter.IsNpc = true; emitter.CanHear = true;
        emitter.Id = IdGenerator.GetUniqueId(); ObjectRegistry.AddObject(emitter);
        emitter.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(coord);
        node.AddObject(emitter);
        return (emitter, node);
    }

    [Fact] public void SourceRoomObjectsHearAtFullLoudness()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh, area) = MakeCube();
        var center = new Coord(AREA,4,4,4);
        var (emitter, centerNode) = Place(nh, center);
        var listener = new TrackingObject(); listener.Name="Listener"; listener.IsNpc=true; listener.CanHear=true; listener.Id=IdGenerator.GetUniqueId(); ObjectRegistry.AddObject(listener);
        listener.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(center);
        centerNode.AddObject(listener);
        emitter.AtEmitSound("hello","Hello!",100.0,true);
        Assert.Single(listener.Heard);
        Assert.Equal(100.0, listener.Heard[0].loud, 2);
        Assert.True(listener.Heard[0].isSay);
        Assert.Single(emitter.Heard);
    }
    [Fact] public void SingleAxisHopAttenuation()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,_) = MakeCube();
        var center=new Coord(AREA,4,4,4);
        var (emitter,_) = Place(nh, center);
        emitter.AtEmitSound("bang","bang!",100.0,false);
        double atten = new AtherizSettings().DefaultOpenSoundAttenuation;
        double expected = 100.0 - atten;
        int hop=1;
        for(int x=5;x<9;x++)
        {
            var node = (TrackingNode)nh.GetNode(new Coord(AREA,x,4,4))!;
            Assert.True(node.Heard.Count>0, $"Hop {hop} no sound");
            Assert.Equal(expected, node.Heard[0].loud, 2);
            expected -= atten; hop++;
        }
    }
    [Fact] public void AllSixDirectionsPropagate()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,_) = MakeCube();
        var center=new Coord(AREA,4,4,4);
        var (emitter,_) = Place(nh, center);
        emitter.AtEmitSound("bang","bang!",100.0,false);
        double expected = 100.0 - new AtherizSettings().DefaultOpenSoundAttenuation;
        foreach(var coord in new[]{ new Coord(AREA,5,4,4), new Coord(AREA,3,4,4), new Coord(AREA,4,5,4), new Coord(AREA,4,3,4), new Coord(AREA,4,4,5), new Coord(AREA,4,4,3)})
        {
            var node=(TrackingNode)nh.GetNode(coord)!;
            Assert.True(node.Heard.Count>0);
            Assert.Equal(expected, node.Heard[0].loud, 2);
        }
    }
    [Fact] public void DiagonalNodeAttenuation2Hops()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,_) = MakeCube();
        var (emitter,_) = Place(nh, new Coord(AREA,4,4,4));
        emitter.AtEmitSound("bang","bang!",100.0,false);
        var diag=(TrackingNode)nh.GetNode(new Coord(AREA,5,5,4))!;
        Assert.True(diag.Heard.Count>0);
        Assert.Equal(100.0-2*new AtherizSettings().DefaultOpenSoundAttenuation, diag.Heard[0].loud, 2);
    }
    [Fact] public void NodeHearsOnce()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh, area)=MakeCube();
        var (emitter,_) = Place(nh, new Coord(AREA,4,4,4));
        emitter.AtEmitSound("bang","bang!",100.0,false);
        foreach(var grid in area.Grids.Values) foreach(var node in grid.Nodes.Values) Assert.True(((TrackingNode)node).Heard.Count<=1);
    }
    [Fact] public void SoundStopsAtZeroLoudness()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,_) = MakeCube();
        var (emitter,_) = Place(nh, new Coord(AREA,4,4,4));
        emitter.AtEmitSound("tap","tap.",30.0,false);
        var near=(TrackingNode)nh.GetNode(new Coord(AREA,5,4,4))!;
        Assert.True(near.Heard.Count>0);
        var far=(TrackingNode)nh.GetNode(new Coord(AREA,7,4,4))!;
        Assert.Empty(far.Heard);
    }
    [Fact] public void BlockingNodeSkipsContentsButPropagates()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = GlobalServices.GetNodeHandler(); NodeHandler.SetCurrent(nh);
        var area=new NodeArea("bfs_block_test");
        var grid=new NodeGrid("bfs_block_test",0);
        var src=new TrackingNode(new Coord("bfs_block_test",0,0,0));
        var blocker=new BlockingNode(new Coord("bfs_block_test",1,0,0));
        var beyond=new TrackingNode(new Coord("bfs_block_test",2,0,0));
        grid.Nodes[(0,0)]=src; grid.Nodes[(1,0)]=blocker; grid.Nodes[(2,0)]=beyond;
        area.AddGrid(grid); nh.AddArea(area);
        var emitter=new TrackingObject(); emitter.Name="Emitter"; emitter.IsNpc=true; emitter.CanHear=true; emitter.Id=IdGenerator.GetUniqueId(); ObjectRegistry.AddObject(emitter);
        emitter.Location=new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(src.Coord); src.AddObject(emitter);
        var inside=new TrackingObject(); inside.Name="Inside"; inside.IsNpc=true; inside.CanHear=true; inside.Id=IdGenerator.GetUniqueId(); ObjectRegistry.AddObject(inside);
        inside.Location=new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(blocker.Coord); blocker.AddObject(inside);
        var beyondObj=new TrackingObject(); beyondObj.Name="Beyond"; beyondObj.IsNpc=true; beyondObj.CanHear=true; beyondObj.Id=IdGenerator.GetUniqueId(); ObjectRegistry.AddObject(beyondObj);
        beyondObj.Location=new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(beyond.Coord); beyond.AddObject(beyondObj);
        emitter.AtEmitSound("bang","bang!",100.0,false);
        Assert.Single(blocker.Heard);
        Assert.Equal(90.0, blocker.Heard[0].loud, 2);
        Assert.Empty(inside.Heard);
        Assert.Single(beyond.Heard);
        Assert.Equal(80.0, beyond.Heard[0].loud, 2);
        Assert.Single(beyondObj.Heard);
    }
    [Fact] public void EmptyMessageNoPropagation()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,_) = MakeCube();
        var (emitter, centerNode) = Place(nh, new Coord(AREA,4,4,4));
        var l = new TrackingObject(); l.Name="L"; l.IsNpc=true; l.CanHear=true; l.Id=IdGenerator.GetUniqueId(); ObjectRegistry.AddObject(l);
        l.Location=new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(centerNode.Coord); centerNode.AddObject(l);
        emitter.AtEmitSound("desc","",100.0,false);
        Assert.Empty(l.Heard);
        emitter.AtEmitSound("desc",null!,100.0,false);
        Assert.Empty(l.Heard);
    }
    [Fact] public void NoLocationNoPropagationDoesNotThrow()
    {
        var e = new TrackingObject(); e.Name="Float"; e.IsNpc=true; e.Id=IdGenerator.GetUniqueId(); ObjectRegistry.AddObject(e);
        var ex = Record.Exception(()=> e.AtEmitSound("desc","msg",100.0,false));
        Assert.Null(ex);
    }
    [Fact] public void DoorInRoomDoesNotCrash()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,_) = MakeCube();
        var center=new Coord(AREA,4,4,4);
        var (emitter, centerNode) = Place(nh, center);
        var l=new TrackingObject(); l.Name="L"; l.IsNpc=true; l.CanHear=true; l.Id=IdGenerator.GetUniqueId(); ObjectRegistry.AddObject(l);
        l.Location=new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(centerNode.Coord); centerNode.AddObject(l);
        var door=new Door(center, new Coord(AREA,5,4,4), "north","south", null,"","",false,false);
        nh.AddDoor(door);
        var neighbor=(TrackingNode)nh.GetNode(new Coord(AREA,5,4,4))!;
        emitter.AtEmitSound("bang","bang!",100.0,false);
        Assert.Single(l.Heard);
        Assert.True(neighbor.Heard.Count>0);
    }
    // Sphere tests
    private static NodeArea BuildSparse(string name="SphereTest")
    {
        var area=new NodeArea(name);
        foreach(var z in new[]{0,10}){ var g=new NodeGrid(name,z); for(int x=0;x<25;x+=3) for(int y=0;y<25;y+=3) g.Nodes[(x,y)]=new Node(new Coord(name,x,y,z)); area.AddGrid(g); }
        return area;
    }
    [Fact] public void GetNodesInSphereCorrectness()
    {
        var area=BuildSparse();
        var center=(12,12,5); int radius=12;
        var coords=GameUtils.GetPointsInSphere(center, radius, true);
        var oldNodes=area.GetNodes(coords.Select(c=> (c.X,c.Y,c.Z)).ToList());
        var newNodes=area.GetNodesInSphere(center, radius, true);
        Assert.Equal(new HashSet<Coord>(oldNodes.Select(n=>n.Coord)), new HashSet<Coord>(newNodes.Select(n=>n.Coord)));
    }
    [Fact] public void GetRaysInSphereCorrectness()
    {
        var area=BuildSparse();
        var center=(12,12,5); int radius=12;
        var newNodes=area.GetNodesInSphere(center, radius, true);
        var rays=area.GetRaysInSphere(center, radius, true);
        var flat=rays.SelectMany(r=>r).ToList();
        Assert.Equal(new HashSet<Coord>(newNodes.Select(n=>n.Coord)), new HashSet<Coord>(flat.Select(n=>n.Coord)));
        Assert.Equal(flat.Count, flat.Select(n=>n.Coord).Distinct().Count());
        foreach(var ray in rays)
        {
            var dists=ray.Select(n=>{ int dx=n.Coord.X-center.Item1, dy=n.Coord.Y-center.Item2, dz=n.Coord.Z-center.Item3; return dx*dx+dy*dy+dz*dz; }).ToList();
            Assert.Equal(dists.OrderBy(x=>x).ToList(), dists);
        }
    }
    [Fact] public void GetNodesInSphereIgnoreCenter()
    {
        var area=new NodeArea("CenterTest");
        var g=new NodeGrid("CenterTest",0);
        g.Nodes[(0,0)]=new Node(new Coord("CenterTest",0,0,0));
        g.Nodes[(1,0)]=new Node(new Coord("CenterTest",1,0,0));
        area.AddGrid(g);
        Assert.Equal(2, area.GetNodesInSphere((0,0,0),2).Count);
        Assert.Single(area.GetNodesInSphere((0,0,0),2,true));
    }
    [Fact] public void GetPointsInSphereRadiusGuard()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=> GameUtils.GetPointsInSphere((0,0,0), -1));
        Assert.Throws<ArgumentOutOfRangeException>(()=> GameUtils.GetPointsInSphere((0,0,0), 101));
        var pts=GameUtils.GetPointsInSphere((0,0,0),0);
        Assert.Single(pts); Assert.Equal((0,0,0), pts[0]);
        var pts2=GameUtils.GetPointsInSphere((0,0,0),100);
        Assert.True(pts2.Count>0);
    }
    [Fact] public void GetNodesInSphereRadiusGuard()
    {
        var area=new NodeArea("TestSphere");
        Assert.Throws<ArgumentOutOfRangeException>(()=> area.GetNodesInSphere((0,0,0), -5));
        Assert.Throws<ArgumentOutOfRangeException>(()=> area.GetNodesInSphere((0,0,0), 500));
        Assert.Empty(area.GetNodesInSphere((0,0,0),5));
    }
    [Fact] public void GetDirDifferentArea()
    {
        Assert.Equal("", GameUtils.GetDir(new Coord("A",0,0,0), new Coord("B",5,5,0)));
        Assert.Equal("east", GameUtils.GetDir(new Coord("A",0,0,0), new Coord("A",1,0,0)));
        Assert.Equal("north", GameUtils.GetDir(new Coord("A",0,0,0), new Coord("A",0,1,0)));
    }
}
