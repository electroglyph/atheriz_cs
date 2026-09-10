using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify;

// AtDisconnect temp-puppet cleanup: the coord fast path removes the puppet
// from its primary node, and a coord miss falls back to a full node scan so
// stray contents are still cleaned up.
[Collection("Ported")]
public class SessionDisconnectCleanupTests
{
    [Fact]
    public void AtDisconnect_CoordHit_RemovesPuppetFromPrimaryNode()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var coord = new Coord("limbo", 0, 0, 0);
            var node = new Node(coord);
            ObjectRegistry.AddObject(node);
            var puppet = GameObject.Create("temp");
            puppet.IsTemporary = true;
            ObjectRegistry.AddObject(puppet);
            node.AddObject(puppet);
            var session = new Session { Puppet = puppet };

            session.AtDisconnect();

            Assert.DoesNotContain(puppet.Id, node.ContentsSnapshot);
            Assert.Empty(ObjectRegistry.Get(puppet.Id));
            Assert.IsType<LocationRef.NullLocation>(puppet.Location);
            Assert.True(puppet.IsDeleted);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void AtDisconnect_CoordMiss_FallsBackToFullScan()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var node = new Node(new Coord("limbo", 1, 1, 1));
            ObjectRegistry.AddObject(node);
            var puppet = GameObject.Create("temp");
            puppet.IsTemporary = true;
            // Location points at a coord with no node; the id is also planted
            // as stray contents on an unrelated node.
            puppet.Location = new LocationRef.CoordLocation(new Coord("limbo", 9, 9, 9));
            ObjectRegistry.AddObject(puppet);
            node.AddContent(puppet.Id);
            var session = new Session { Puppet = puppet };

            session.AtDisconnect();

            Assert.DoesNotContain(puppet.Id, node.ContentsSnapshot);
            Assert.Empty(ObjectRegistry.Get(puppet.Id));
            Assert.True(puppet.IsDeleted);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
