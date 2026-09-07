using System.Reflection;
using Atheriz.Core;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Objects;

// Object and node deletion counts for recursive and non-recursive deletes.
[Collection("Ported")]
public class ObjectDeletionTests
{
    private static void RegisterAll(params GameObject[] objs)
    {
        foreach (var o in objs)
            if (ObjectRegistry.Get(o.Id).Count == 0)
                ObjectRegistry.AddObject(o);
    }

    // --- Delete counts ---

    [Fact]
    public void Delete_Recursive_ReturnsTrueCount()
    {
        // Recursive Delete returns the true collected count, not a constant.
        ObjectRegistry.ClearAll();
        try
        {
            var parent = GameObject.Create("parent", isContainer: true);
            var k1 = GameObject.Create("kid1");
            var k2 = GameObject.Create("kid2");
            RegisterAll(parent, k1, k2);
            Assert.True(k1.MoveTo(parent));
            Assert.True(k2.MoveTo(parent));
            var res = parent.Delete(null, recursive: true);
            Assert.NotNull(res);
            Assert.Equal(3, res!.Value.count);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Node_Delete_Recursive_ReturnsTrueCount()
    {
        // Node.Delete returns the true collected count for recursive deletes.
        ObjectRegistry.ClearAll();
        try
        {
            var node = new Node(new Coord("limbo", 6, 0, 0));
            var item = GameObject.Create("item");
            RegisterAll(node, item);
            Assert.True(item.MoveTo(node));
            var res = node.Delete(null, recursive: true);
            Assert.NotNull(res);
            Assert.Equal(2, res!.Value.count);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Delete_NonRecursive_CountsRecursivelyDeletedChildren()
    {
        // The non-recursive Delete path counts children that had to be
        // recursively deleted when they could not move out.
        ObjectRegistry.ClearAll();
        try
        {
            var parent = GameObject.Create("parent", isContainer: true);
            var stuck = GameObject.Create("stuck");
            RegisterAll(parent, stuck);
            Assert.True(stuck.MoveTo(parent));
            stuck.InstallHook("at_pre_move", (Func<GameObject?, string?, bool>)new VetoHooks().DenyAll);
            var res = parent.Delete(null, recursive: false);
            Assert.NotNull(res);
            Assert.Equal(2, res!.Value.count);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    // [Replace]-attributed veto hook (replaces the removed At*Override seam;
    // lambdas cannot carry attributes, so a real method provides the marker).
    private sealed class VetoHooks
    {
        [Replace]
        public bool DenyAll(GameObject? a, string? b) => false;
    }
}
