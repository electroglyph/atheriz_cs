using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Features.Hosting;

// Node contents are runtime-only (areas rows carry no contents list), so a
// fresh load must put coord-located objects back into their rooms —
// otherwise rooms render empty after every restart.
[Collection("Ported")]
public class RoomContentsTests
{
    [Fact]
    public void RestoreRoomContents_PutsLocatedObjectsBackInRoom()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new NodeHandler(autoLoad: false);
        var area = $"restore_{Guid.NewGuid():N}";
        var coord = new Coord(area, 4, 4, 4);
        var node = new Node(coord, desc: "room");
        handler.AddNode(node);
        var npc = GameObject.Create("Grunt", isNpc: true);
        npc.Location = LocationRef.FromCoord(coord);
        ObjectRegistry.AddObject(npc);
        try
        {
            // Simulate a fresh load: the room starts with empty contents
            // while the object still points at its coord.
            Assert.Empty(node.ContentsSnapshot);
            handler.RestoreRoomContents();
            Assert.Contains(npc.Id, node.ContentsSnapshot);
        }
        finally
        {
            try { ObjectRegistry.RemoveObject(npc); } catch { }
            try { ObjectRegistry.RemoveObject(node); } catch { }
        }
    }

    [Fact]
    public void RestoreRoomContents_RoomListsRestoredCharacters()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new NodeHandler(autoLoad: false);
        var area = $"restorelook_{Guid.NewGuid():N}";
        var coord = new Coord(area, 0, 0, 0);
        var node = new Node(coord, desc: "room");
        handler.AddNode(node);
        var npc = GameObject.Create("Grunt", isNpc: true);
        npc.Location = LocationRef.FromCoord(coord);
        ObjectRegistry.AddObject(npc);
        var looker = GameObject.Create("Looker", isPc: true);
        looker.Location = LocationRef.FromCoord(coord);
        ObjectRegistry.AddObject(looker);
        try
        {
            handler.RestoreRoomContents();
            var chars = node.GetDisplayCharacters(looker);
            Assert.Contains("Grunt", chars);
            Assert.DoesNotContain("Looker", chars);
        }
        finally
        {
            try { ObjectRegistry.RemoveObject(npc); } catch { }
            try { ObjectRegistry.RemoveObject(looker); } catch { }
            try { ObjectRegistry.RemoveObject(node); } catch { }
        }
    }

    [Fact]
    public void RestoreRoomContents_SkipsCoordsWithNoRoom()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new NodeHandler(autoLoad: false);
        var obj = GameObject.Create("Lost");
        obj.Location = LocationRef.FromCoord(new Coord($"nobodyhere_{Guid.NewGuid():N}", 1, 2, 3));
        ObjectRegistry.AddObject(obj);
        try
        {
            // No room at that coord: must not throw, must not seat anywhere.
            handler.RestoreRoomContents();
            Assert.Empty(ObjectRegistry.FilterBy(o => o is Node n && n.ContentsSnapshot.Contains(obj.Id)));
        }
        finally { try { ObjectRegistry.RemoveObject(obj); } catch { } }
    }
}
