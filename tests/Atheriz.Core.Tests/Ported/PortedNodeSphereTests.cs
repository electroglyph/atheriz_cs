// Port of atheriz/tests/test_node_sphere.py:1 — faithful 9 tests (sphere + utils)
using Atheriz.Core;
using Atheriz.Core.Objects;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedNodeSphereTests
{
    private static NodeArea BuildSparseArea(string name="SphereTest")
    {
        var area = new NodeArea(name);
        foreach(var z in new[]{0,10})
        {
            var grid = new NodeGrid(name, z);
            for(int x=0;x<25;x+=3) for(int y=0;y<25;y+=3)
                grid.Nodes[(x,y)] = new Node(new Coord(name, x, y, z));
            area.AddGrid(grid);
        }
        return area;
    }

    // test_node_sphere.py:19 test_get_nodes_in_sphere_correctness_and_speed
    [Fact] public void GetNodesInSphereCorrectnessAndSpeed() // test_node_sphere.py:19
    {
        var area = BuildSparseArea();
        var center = (12,12,5);
        int radius = 12;
        var coords = GameUtils.GetPointsInSphere(center, radius, ignoreCenter:true);
        var oldNodes = area.GetNodes(coords.Select(c=> new Coord("SphereTest", c.X, c.Y, c.Z)).ToList());
        var newNodes = area.GetNodesInSphere(center, radius, ignoreCenter:true);
        var oldSet = new HashSet<Coord>(oldNodes.Select(n=>n.Coord));
        var newSet = new HashSet<Coord>(newNodes.Select(n=>n.Coord));
        Assert.Equal(oldSet, newSet);
    }

    // test_node_sphere.py:48 test_get_rays_in_sphere_correctness_and_speed
    [Fact] public void GetRaysInSphereCorrectnessAndSpeed() // test_node_sphere.py:48
    {
        var area = BuildSparseArea();
        var center = (12,12,5);
        int radius = 12;
        var coords = GameUtils.GetPointsInSphere(center, radius, ignoreCenter:true);
        var oldNodes = area.GetNodes(coords.Select(c=> new Coord("SphereTest", c.X, c.Y, c.Z)).ToList());
        var newNodes = area.GetNodesInSphere(center, radius, ignoreCenter:true);
        var rays = area.GetRaysInSphere(center, radius, ignoreCenter:true);
        var oldSet = new HashSet<Coord>(oldNodes.Select(n=>n.Coord));
        var newSet = new HashSet<Coord>(newNodes.Select(n=>n.Coord));
        var raysFlat = rays.SelectMany(r=>r).ToList();
        var raysSet = new HashSet<Coord>(raysFlat.Select(n=>n.Coord));
        Assert.Equal(oldSet, newSet);
        Assert.Equal(oldSet, raysSet);
        Assert.Equal(raysFlat.Count, raysSet.Count); // Duplicate nodes in rays
        foreach(var ray in rays)
        {
            var dists = new List<int>();
            foreach(var n in ray)
            {
                int nx=n.Coord.X, ny=n.Coord.Y, nz=n.Coord.Z;
                int dx=nx-center.Item1, dy=ny-center.Item2, dz=nz-center.Item3;
                dists.Add(dx*dx+dy*dy+dz*dz);
            }
            var sorted = dists.OrderBy(x=>x).ToList();
            Assert.Equal(sorted, dists);
        }
    }

    // test_node_sphere.py:100 test_get_nodes_in_sphere_ignore_center
    [Fact] public void GetNodesInSphereIgnoreCenter() // test_node_sphere.py:100
    {
        var area = new NodeArea("CenterTest");
        var grid = new NodeGrid("CenterTest", 0);
        grid.Nodes[(0,0)] = new Node(new Coord("CenterTest",0,0,0));
        grid.Nodes[(1,0)] = new Node(new Coord("CenterTest",1,0,0));
        area.AddGrid(grid);
        var withCenter = area.GetNodesInSphere((0,0,0), 2);
        var withoutCenter = area.GetNodesInSphere((0,0,0), 2, ignoreCenter:true);
        Assert.Equal(2, withCenter.Count);
        Assert.Single(withoutCenter);
    }

    // test_node_sphere.py:114 test_get_nodes_in_sphere_no_grids
    [Fact] public void GetNodesInSphereNoGrids() // test_node_sphere.py:114
    {
        var area = new NodeArea("EmptyTest");
        var result = area.GetNodesInSphere((0,0,0), 10);
        Assert.Empty(result);
    }

    // test_node_sphere.py:120 test_get_rays_in_sphere_center_no_crash
    [Fact] public void GetRaysInSphereCenterNoCrash() // test_node_sphere.py:120
    {
        var area = new NodeArea("CenterRayTest");
        var grid = new NodeGrid("CenterRayTest", 0);
        grid.Nodes[(0,0)] = new Node(new Coord("CenterRayTest",0,0,0));
        grid.Nodes[(1,0)] = new Node(new Coord("CenterRayTest",1,0,0));
        grid.Nodes[(0,1)] = new Node(new Coord("CenterRayTest",0,1,0));
        area.AddGrid(grid);
        var rays = area.GetRaysInSphere((0,0,0), 2, ignoreCenter:false);
        var flat = rays.SelectMany(r=>r).ToList();
        var coords = new HashSet<Coord>(flat.Select(n=>n.Coord));
        Assert.Contains(new Coord("CenterRayTest",1,0,0), coords);
        Assert.Contains(new Coord("CenterRayTest",0,1,0), coords);
    }

    // test_node_sphere.py:135 test_get_dir_different_area_returns_empty
    [Fact] public void GetDirDifferentAreaReturnsEmpty() // test_node_sphere.py:135
    {
        Assert.Equal("", GameUtils.GetDir(new List<object?>{"AreaA",0,0,0}, new List<object?>{"AreaB",1,0,0}));
        Assert.Equal("east", GameUtils.GetDir(new List<object?>{"AreaA",0,0,0}, new List<object?>{"AreaA",1,0,0}));
        Assert.Equal("", GameUtils.GetDir(new Coord("A",0,0,0), new Coord("B",5,5,0)));
        Assert.Equal("north", GameUtils.GetDir(new Coord("A",0,0,0), new Coord("A",0,1,0)));
    }

    // test_node_sphere.py:145 test_get_points_in_sphere_radius_guard
    [Fact] public void GetPointsInSphereRadiusGuard() // test_node_sphere.py:145
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=> GameUtils.GetPointsInSphere((0,0,0), -1));
        Assert.Throws<ArgumentOutOfRangeException>(()=> GameUtils.GetPointsInSphere((0,0,0), GameUtils.MaxSphereRadius+1));
        Assert.Throws<ArgumentOutOfRangeException>(()=> GameUtils.GetPointsInSphere((0,0,0), 1000));
        var pts = GameUtils.GetPointsInSphere((0,0,0), 0);
        Assert.Equal(new List<(int,int,int)>{(0,0,0)}, pts);
        var pts2 = GameUtils.GetPointsInSphere((0,0,0), GameUtils.MaxSphereRadius);
        Assert.NotEmpty(pts2);
    }

    // test_node_sphere.py:158 test_get_nodes_in_sphere_radius_guard
    [Fact] public void GetNodesInSphereRadiusGuard() // test_node_sphere.py:158
    {
        var area = new NodeArea("TestSphere");
        Assert.Throws<ArgumentOutOfRangeException>(()=> area.GetNodesInSphere((0,0,0), -5));
        Assert.Throws<ArgumentOutOfRangeException>(()=> area.GetNodesInSphere((0,0,0), 500));
        Assert.Empty(area.GetNodesInSphere((0,0,0), 5));
    }

    // test_node_sphere.py:167 test_safe_pow_rejects_complex
    [Fact] public void SafePowRejectsComplex() // test_node_sphere.py:167
    {
        var ex1 = Assert.Throws<InvalidOperationException>(()=> FuncParserHelpers._safe_pow(-2, 0.5));
        Assert.Contains("complex", ex1.Message.ToLowerInvariant());
        var ex2 = Assert.Throws<InvalidOperationException>(()=> FuncParserHelpers._safe_arith_eval("(-2)**0.5"));
        Assert.Contains("complex", ex2.Message.ToLowerInvariant());
        Assert.Throws<InvalidOperationException>(()=> FuncParserHelpers._safe_arith_eval("(-4)**0.5"));
        Assert.Equal(8, FuncParserHelpers._safe_pow(2,3));
        Assert.Equal(8, FuncParserHelpers._safe_arith_eval("2**3"));
        Assert.Equal(3.0, FuncParserHelpers._safe_arith_eval("9**0.5"));
    }
}
