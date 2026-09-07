using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Globals;

// remove_area pops + clears only; nodes stay registered (node.py:639-644).
// Only clear() evicts.
[Collection("Ported")]
public class RemoveAreaRegistryTests
{
    [Fact]
    public void RemoveArea_KeepsNodesRegistered()
    {
        ObjectRegistry.ClearAll();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var node = new Node(new Coord("keeparea", 3, 3, 0));
            ObjectRegistry.AddObject(node);
            nh.AddNode(node);
            nh.RemoveArea("keeparea");
            Assert.Null(nh.GetArea("keeparea"));
            Assert.NotEmpty(ObjectRegistry.Get(node.Id));
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }
}
