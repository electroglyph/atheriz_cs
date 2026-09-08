using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Object hash must not depend on the mutable Id: transient objects sit in
// hash sets (exclusion sets in Node/ForContents paths) while ids are
// assigned, and a shifting hash strands them in the wrong bucket
// (GameObject.cs:131 with :300-306).
[Collection("Ported")]
public class ObjectIdentityTests
{
    [Fact]
    public void HashCode_FollowsRegistryId()
    {
        // Port of nodes.py:92 — hash(id), matching Equals-by-Id .
        // Same-Id instances (e.g. reload duplicates) hash equal.
        var o = new GameObject(); // transient Id == -1
        o.Id = 4242;
        Assert.Equal(4242.GetHashCode(), o.GetHashCode());
    }

    [Fact]
    public void HashSet_FindsSameIdInstance()
    {
        // Equal (same-Id) instances share a hash bucket: reload duplicates
        // are findable via the hash lookup, not just linear scan.
        // NOTE: must use the hash-bucket lookup (TryGetValue), not
        // Assert.Contains (linear scan that never consults the hash).
        var a = GameObject.Create("hash-a");
        var b = GameObject.Create("hash-b");
        b.Id = a.Id;
        var set = new HashSet<GameObject> { a };
        Assert.True(set.TryGetValue(b, out var found));
        Assert.Same(a, found);
    }
}
