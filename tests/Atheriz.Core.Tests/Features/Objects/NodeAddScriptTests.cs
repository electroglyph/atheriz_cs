using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Node.AddScript records only real scripts: anything else (plain objects,
// raw ids) must not land in the script id list.
[Collection("Ported")]
public sealed class NodeAddScriptTests
{
    [Fact]
    public void NodeAddScript_NonScriptId_NotRecorded()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var node = new Node(new Coord("f7room", 0, 0, 0));
            var junk = GameObject.Create("f7junk");
            ObjectRegistry.AddObject(node);
            ObjectRegistry.AddObject(junk);

            node.AddScript(junk);
            node.AddScript(987654321);

            Assert.DoesNotContain(junk.Id, node.ScriptsSnapshot);
            Assert.DoesNotContain(987654321, node.ScriptsSnapshot);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
