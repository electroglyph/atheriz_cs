using System.Reflection;
using Atheriz.Core;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Objects;

// Node naming: name reflects coordinates and ignores setter writes.
[Collection("Ported")]
public class NodeNamingTests
{
    // --- Node.Name no-op ---

    [Fact]
    public void Node_Name_IsCoordString_SetterIgnored()
    {
        // Python parity (nodes.py:616-619: name is a read-only property
        // returning str(coord); rooms are coord-identified, get_display_name
        // is "" for non-builders): the C# no-op setter mirrors the read-only
        // property for the base-class contract.
        ObjectRegistry.ClearAll();
        try
        {
            var node = new Node(new Coord("limbo", 3, 0, 0));
            if (ObjectRegistry.Get(node.Id).Count == 0) ObjectRegistry.AddObject(node);
            Assert.Equal(node.Coord.ToString(), node.Name);
            node.Name = "Tavern";
            Assert.Equal(node.Coord.ToString(), node.Name);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
