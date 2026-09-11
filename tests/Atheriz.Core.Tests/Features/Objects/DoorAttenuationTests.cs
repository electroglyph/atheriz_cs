using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Door state controls sound attenuation: open (or absent) doors use the low
// open attenuation so sound travels further; closed doors use enclosed.
[Collection("Ported")]
public class DoorAttenuationTests
{
    private const string Area = "DoorAttenArea";

    private static (NodeHandler Nh, Node Node, GameObject Emitter) Setup()
    {
        ObjectRegistry.ClearAll();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var node = new Node(new Coord(Area, 0, 0, 0));
        var emitter = GameObject.Create("emitter");
        ObjectRegistry.AddObject(emitter);
        return (nh, node, emitter);
    }

    private static void Teardown()
    {
        NodeHandler.SetCurrent(null);
        ObjectRegistry.ClearAll();
    }

    [Fact]
    public void AtHear_NoDoors_UsesOpenAttenuation()
    {
        var (_, node, emitter) = Setup();
        try
        {
            Assert.Equal(50.0, node.AtHear(emitter, "boom", "loud!", 60.0, false));
        }
        finally { Teardown(); }
    }

    [Fact]
    public void AtHear_ClosedDoor_UsesEnclosedAttenuation()
    {
        var (nh, node, emitter) = Setup();
        try
        {
            var door = new Door(node.Coord, new Coord(Area, 0, 2, 0), "north", "south", closed: true);
            nh.AddDoor(door);
            Assert.Equal(40.0, node.AtHear(emitter, "boom", "loud!", 60.0, false));
        }
        finally { Teardown(); }
    }

    [Fact]
    public void AtHear_OpenDoor_UsesOpenAttenuation()
    {
        var (nh, node, emitter) = Setup();
        try
        {
            var door = new Door(node.Coord, new Coord(Area, 0, 2, 0), "north", "south", closed: false);
            nh.AddDoor(door);
            Assert.Equal(50.0, node.AtHear(emitter, "boom", "loud!", 60.0, false));
        }
        finally { Teardown(); }
    }

    [Fact]
    public void AtHear_RemovedLastDoor_RevertsToOpenAttenuation()
    {
        // RemoveDoor leaves an empty per-coord row behind; an empty row must
        // still count as open (sound travels further through doorways whose
        // doors were removed), matching the AtEmitSound open formula.
        var (nh, node, emitter) = Setup();
        try
        {
            var door = new Door(node.Coord, new Coord(Area, 0, 2, 0), "north", "south", closed: true);
            nh.AddDoor(door);
            Assert.Equal(40.0, node.AtHear(emitter, "boom", "loud!", 60.0, false));
            nh.RemoveDoor(door);
            var remaining = nh.GetDoors(node.Coord);
            Assert.NotNull(remaining);
            Assert.Empty(remaining);
            Assert.Equal(50.0, node.AtHear(emitter, "boom", "loud!", 60.0, false));
        }
        finally { Teardown(); }
    }
}
